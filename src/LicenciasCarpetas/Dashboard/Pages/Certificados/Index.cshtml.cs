using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.Certificados.Data;
using LicenciasCarpetas.Certificados.Domain;
using LicenciasCarpetas.CambioDomicilio.Extraction;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Dashboard.Pages.Certificados;

[Authorize(Policy = "CambioDomicilioAccess")]
public class IndexModel(
    ICertificadoRequestRepository repository,
    LicenciasCarpetas.CambioDomicilio.Data.IOutboundAddressChangeRequestRepository? outboundRepo = null) : PageModel
{
    public IReadOnlyList<CertificadoRequest> Requests { get; private set; } = [];
    public string? Tab { get; set; }
    public string? Search { get; set; }
    public string? Message { get; set; }
    public bool MessageIsError { get; set; }
    public int PendientesCount { get; private set; }
    public int EmitidosCount { get; private set; }

    public void OnGet(string? tab, string? search)
    {
        Tab = string.IsNullOrWhiteSpace(tab) ? "Todos" : tab;
        Search = search;
        Load();
    }

    public IActionResult OnPostToggleMarked(long id, string? markedValue)
    {
        var marked = markedValue == "on";
        repository.SetMarked(id, marked);
        return RedirectToPage(new { tab = Tab, search = Search });
    }

    public IActionResult OnPostTogglePendienteCarpeta(long id, string? pendienteCarpetaValue)
    {
        var pendiente = pendienteCarpetaValue == "on";
        repository.SetPendienteCarpeta(id, pendiente);
        return RedirectToPage(new { tab = Tab, search = Search });
    }

    public IActionResult OnPostSetPersonData(long id, string nombreCompleto, string rut)
    {
        var item = repository.FindById(id);
        if (item is null) return RedirectToPage(new { tab = Tab, search = Search });

        var normalizedRut = LicenciasCarpetas.Domain.RutValidator.NormalizeAndValidate(rut);
        var nombre = (nombreCompleto ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizedRut is null || nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            Message = normalizedRut is null
                ? "El RUT ingresado no es válido."
                : "Ingrese el nombre completo (al menos nombre y apellido).";
            MessageIsError = true;
            Load();
            return Page();
        }

        item.NombreCompleto = nombre;
        item.Rut = normalizedRut;
        repository.Update(item);
        Message = "Datos del contribuyente guardados.";
        return RedirectToPage(new { tab = Tab, search = Search });
    }

    public IActionResult OnPostSetEstado(long id, string estado)
    {
        var item = repository.FindById(id);
        if (item is not null && !string.IsNullOrWhiteSpace(estado))
        {
            item.Estado = estado.Trim().ToUpperInvariant();
            item.EstadoActual = item.Estado;
            repository.Update(item);

            if ((item.Estado == "CERTIFICADO EMITIDO" || item.Estado == "EMITIDO") && outboundRepo is not null)
            {
                LicenciasCarpetas.CambioDomicilio.Domain.OutboundAddressChangeRequest? outbound = null;
                if (item.SourceId is { } sourceId and > 0)
                {
                    outbound = outboundRepo.FindById(sourceId);
                }
                if (outbound is null && !string.IsNullOrWhiteSpace(item.Rut))
                {
                    var normalizedRut = LicenciasCarpetas.Domain.RutValidator.NormalizeAndValidate(item.Rut) ?? item.Rut.Trim();
                    outbound = outboundRepo.GetAll().FirstOrDefault(r =>
                        (LicenciasCarpetas.Domain.RutValidator.NormalizeAndValidate(r.Rut) ?? r.Rut.Trim()) == normalizedRut
                        && r.WorkflowState != FolderState.CambioDomicilioSubidoAConaset);
                }

                if (outbound is not null)
                {
                    outbound.WorkflowState = FolderState.CambioDomicilioSubidoAConaset;
                    outbound.UploadedAt = DateTimeOffset.UtcNow;
                    outboundRepo.Update(outbound);
                }
            }
        }
        return RedirectToPage(new { tab = Tab, search = Search });
    }

    public IActionResult OnPostAddManualCase(string nombreCompleto, string rut, string comuna)
    {
        var normalizedRut = LicenciasCarpetas.Domain.RutValidator.NormalizeAndValidate(rut);
        var nombre = (nombreCompleto ?? string.Empty).Trim().ToUpperInvariant();
        var com = (comuna ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizedRut is null || nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            Message = normalizedRut is null ? "RUT no válido." : "Ingrese nombre y apellido.";
            MessageIsError = true;
            Load();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(com))
        {
            com = "OTRA COMUNA";
        }

        repository.Insert(new CertificadoRequest
        {
            NombreCompleto = nombre,
            Rut = normalizedRut,
            Comuna = com,
            FechaIngreso = DateOnly.FromDateTime(DateTime.Today),
            Estado = "PENDIENTE",
            EstadoActual = "PENDIENTE",
            Origin = "Manual",
            CreatedAt = DateTimeOffset.UtcNow
        });

        Message = "Caso agregado a Carpetas para Certificados.";
        return RedirectToPage(new { tab = Tab, search = Search });
    }

    public IActionResult OnPostDelete(long id)
    {
        repository.Delete(id);
        Message = "Registro eliminado.";
        return RedirectToPage(new { tab = Tab, search = Search });
    }

    private void Load()
    {
        var all = repository.GetAll();
        PendientesCount = all.Count(r => r.Estado != "CERTIFICADO EMITIDO");
        EmitidosCount = all.Count(r => r.Estado == "CERTIFICADO EMITIDO");

        var filtered = all.AsEnumerable();

        if (Tab == "Pendientes")
        {
            filtered = filtered.Where(r => r.Estado != "CERTIFICADO EMITIDO");
        }
        else if (Tab == "Emitidos")
        {
            filtered = filtered.Where(r => r.Estado == "CERTIFICADO EMITIDO");
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var query = Search.Trim().ToUpperInvariant();
            var cleanQuery = query.Replace(".", "").Replace("-", "");
            filtered = filtered.Where(r =>
                r.NombreCompleto.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                r.Rut.Replace(".", "").Replace("-", "").Contains(cleanQuery, StringComparison.OrdinalIgnoreCase) ||
                r.Comuna.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        Requests = filtered.ToList();
    }
}
