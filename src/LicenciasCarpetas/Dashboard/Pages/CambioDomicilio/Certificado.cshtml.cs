using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Extraction;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Notifications;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Routing;

namespace LicenciasCarpetas.Dashboard.Pages.CambioDomicilio;

/// <summary>Lists only cases transferred to the Certificado destination
/// (<see cref="PersonRequest.Destination"/> == <see cref="CaseDestination.Certificado"/>) — the
/// dedicated screen for cases whose physical folder could not be located, resolved via the
/// certificate-request flow instead of the normal upload/confirm flow. Cases here never go
/// through Marcar subida/Confirmar/Deshacer y rectificar (that's F8/Casos-only); the only special
/// action is "Solicitar certificado" per row, which emails Secretaría Municipal the predetermined
/// request for that one contributor plus an acknowledgement to their comuna.</summary>
[Authorize(Policy = "CambioDomicilioAccess")]
public class CertificadoModel(
    ICambioDomicilioRequestRepository repository,
    AddressChangeRoutingService routingService,
    IMailSender mailSender,
    CambioDomicilioOptions options) : PageModel
{
    public IReadOnlyList<PersonRequest> Cases { get; private set; } = [];
    public string? Message { get; set; }
    public bool MessageIsError { get; set; }
    public int PlazoDiasHabiles => options.PlazoDiasHabiles;

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPostSetFecha(long id, string fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha))
        {
            repository.ClearFechaUltimaCarpeta(id);
            return RedirectToPage();
        }

        if (!SpanishDate.TryParse(fecha, out var parsed))
        {
            Message = "Fecha no reconocida. Formatos aceptados: 15/03/2024 o 15 marzo 2024.";
            MessageIsError = true;
            Load();
            return Page();
        }

        repository.SetFechaUltimaCarpeta(id, parsed);
        return RedirectToPage();
    }

    public IActionResult OnPostSetPersonData(long id, string nombre, string rut)
    {
        var normalizedRut = RutValidator.NormalizeAndValidate(rut);
        nombre = (nombre ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizedRut is null || nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            Message = normalizedRut is null
                ? "El RUT ingresado no es válido (revise el dígito verificador)."
                : "Ingrese el nombre completo (al menos nombre y apellido).";
            MessageIsError = true;
            Load();
            return Page();
        }

        repository.SetPersonData(id, nombre, normalizedRut);
        Message = "Datos guardados. El caso ya no requiere revisión.";
        Load();
        return Page();
    }

    public IActionResult OnPostToggleMarked(long id, string? markedValue)
    {
        var marked = markedValue == "on";
        repository.SetMarked(id, marked);
        return RedirectToPage();
    }

    public IActionResult OnPostTogglePendienteCarpeta(long id, string? pendienteCarpetaValue)
    {
        var pendienteCarpeta = pendienteCarpetaValue == "on";
        repository.SetPendienteCarpeta(id, pendienteCarpeta);
        return RedirectToPage();
    }

    public IActionResult OnPostDeleteCase(long id)
    {
        var sourceMessageId = repository.FindById(id)?.SourceMessageId;
        repository.Delete(id);
        if (sourceMessageId is not null)
        {
            repository.RecordDeletedSourceMessage(sourceMessageId);
        }

        Message = "Caso eliminado.";
        return RedirectToPage();
    }

    /// <summary>Undoes "Traspaso a Certificado" — clears <see cref="PersonRequest.Destination"/> so
    /// the case leaves this screen and reappears in Casos (still F8-ticked, ready to be re-transferred).</summary>
    public IActionResult OnPostUndoTransfer(long id)
    {
        repository.ClearDestination(id);
        return RedirectToPage();
    }

    /// <summary>Sends the predetermined certificate-request email for ONE case to Secretaría
    /// Municipal (<see cref="CambioDomicilioOptions.CertificateRequestEmailAddress"/>), carrying that
    /// contributor's name and RUT, plus an acknowledgement to the requesting comuna, then marks the
    /// case as notified so the button can't be pressed twice.</summary>
    public async Task<IActionResult> OnPostSolicitarCertificadoAsync(long id)
    {
        var item = repository.FindById(id);
        if (item is null || item.Destination != CaseDestination.Certificado)
        {
            Message = "El caso no está en la bandeja de Certificado.";
            MessageIsError = true;
            Load();
            return Page();
        }

        if (item.CertificadoNotifiedAt is not null)
        {
            Message = "El certificado de este caso ya fue solicitado.";
            MessageIsError = true;
            Load();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(item.FullName) || string.IsNullOrWhiteSpace(item.Rut))
        {
            Message = "Complete el nombre y el RUT del contribuyente antes de solicitar el certificado.";
            MessageIsError = true;
            Load();
            return Page();
        }

        var comuna = item.Comuna ?? string.Empty;
        var (subject, body) = EmailTemplates.CertificateRequest(item.FullName, item.Rut, comuna);
        await mailSender.SendAsync(options.CertificateRequestEmailAddress, subject, body, HttpContext.RequestAborted);

        var contact = routingService.LoadDirectory()
            .FirstOrDefault(c => string.Equals(c.Comuna, comuna, StringComparison.OrdinalIgnoreCase));
        if (contact is not null)
        {
            var (ackSubject, ackBody) = EmailTemplates.CertificateAcknowledgement(item.FullName, item.Rut);
            await mailSender.SendAsync(contact.ContactEmail, ackSubject, ackBody, HttpContext.RequestAborted);
        }

        repository.SetCertificadoNotified(item.Id, DateTimeOffset.UtcNow);
        Message = $"Certificado solicitado a {options.CertificateRequestEmailAddress} para {item.FullName}, RUT {item.Rut}.";
        Load();
        return Page();
    }

    /// <summary>Business days remaining until the legal upload deadline for this case, from today.</summary>
    public int DiasHabilesRestantes(PersonRequest request)
    {
        var received = DateOnly.FromDateTime(request.ReceivedAt.LocalDateTime);
        var deadline = DeadlineCalculator.AddBusinessDays(received, options.PlazoDiasHabiles);
        return DeadlineCalculator.BusinessDaysRemaining(DateOnly.FromDateTime(DateTime.Today), deadline);
    }

    private void Load()
    {
        Cases = repository.GetAll()
            .Where(c => c.Destination == CaseDestination.Certificado)
            .OrderBy(c => c.Status == RequestStatus.Confirmed)
            .ThenBy(c => c.ConfirmedAt)
            .ThenByDescending(c => c.ReceivedAt)
            .ToList();
    }
}
