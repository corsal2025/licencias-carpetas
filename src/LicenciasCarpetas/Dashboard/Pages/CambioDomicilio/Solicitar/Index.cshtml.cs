using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.F8.Data;

namespace LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.Solicitar;

/// <summary>Los únicos FolderState que tienen sentido para el desplegable "Estado" de una
/// solicitud saliente — un subconjunto elegido por el operador, no todo FolderStateCatalog. El
/// texto de SubidaConOficio se muestra distinto acá ("SUBIDO CON CERTIFICADO", lo que de verdad
/// ocurre cuando se elige esta opción en este flujo) sin tocar FolderStateCatalog.Display, que
/// sigue usando "SUBIDA CON OFICIO" en Casos y en el resto de la app.</summary>
public static class WorkflowStateCatalog
{
    public static readonly IReadOnlyList<FolderState> Options =
    [
        FolderState.CambioDomicilioSubidoAConaset,
        FolderState.SubidaConF8,
        FolderState.SubidaConOficio,
        FolderState.PendienteF8,
        FolderState.PendienteCertificado
    ];

    public static string Display(FolderState state) => state switch
    {
        FolderState.PendienteF8 or FolderState.SubidaConF8 => "SUBIR CON F8",
        FolderState.PendienteCertificado or FolderState.SubidaConOficio => "PEDIR CERTIFICADO",
        _ => FolderStateCatalog.Display(state)
    };
}

/// <summary>Worklist for outbound "solicitud de cambio de domicilio" requests — the mirror
/// direction of the module's Index (which tracks requests other comunas send TO Valparaíso):
/// here Valparaíso builds and sends its own requests to other comunas.</summary>
[Authorize(Policy = "CambioDomicilioAccess")]
public sealed class IndexModel(
    IOutboundAddressChangeRequestRepository repository,
    OutboundRequestSender sender,
    IFolderCaseRepository cases,
    CambioDomicilioOptions options,
    IUrgentRequestRepository? urgentRequests = null,
    LicenciasCarpetas.Certificados.Data.ICertificadoRequestRepository? certificadoRepo = null) : PageModel
{
    [TempData(Key = "SolicitarMessage")]
    public string? Message { get; set; }

    public IReadOnlyList<OutboundAddressChangeRequest> Requests { get; private set; } = [];

    public void OnGet()
    {
        SyncFromFolderCases();
        Requests = repository.GetAll();
    }

    public int? GetDaysRemaining(OutboundAddressChangeRequest request)
    {
        if (request.UploadedAt.HasValue || request.WorkflowState == FolderState.CambioDomicilioSubidoAConaset)
        {
            return null;
        }

        var petitionDate = request.SentAt ?? (request.CreatedAt != default ? request.CreatedAt : (DateTimeOffset?)null);
        if (!petitionDate.HasValue)
        {
            return null;
        }

        var startDate = DateOnly.FromDateTime(petitionDate.Value.LocalDateTime);
        var deadline = DeadlineCalculator.AddBusinessDays(startDate, 15);
        return DeadlineCalculator.BusinessDaysRemaining(DateOnly.FromDateTime(DateTime.Today), deadline);
    }


    private DateTimeOffset GetFechaIngreso(FolderCase c)
    {
        try
        {
            var logs = cases.GetAuditLog(c.Id);
            var entry = logs.FirstOrDefault(l =>
                l.FieldName == "Estado carpeta" &&
                (l.NewValue == nameof(FolderState.CambioDomicilio) || l.NewValue == nameof(FolderState.CambioDomicilioSolicitado) || l.NewValue == "CAMBIO DE DOMICILIO"));
            if (entry is not null)
            {
                return entry.ChangedAt;
            }
        }
        catch { }

        return c.UpdatedAt != default ? c.UpdatedAt : c.CreatedAt;
    }

    private void SyncFromFolderCases()
    {
        var allCases = cases.QueryAll(new CaseFilter());
        var cambioCases = allCases
            .Where(c => c.FolderState == FolderState.CambioDomicilio || c.FolderState == FolderState.CambioDomicilioSolicitado)
            .ToList();

        var existingOutbound = repository.GetAll();
        var existingBySourceId = existingOutbound
            .Where(r => r.SourceFolderCaseId.HasValue)
            .GroupBy(r => r.SourceFolderCaseId!.Value).ToDictionary(g => g.Key, g => g.First());

        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) is { } uid && long.TryParse(uid, out var u) ? u : 1L;

        foreach (var c in cambioCases)
        {
            if (repository.IsSourceFolderCaseDeleted(c.Id))
            {
                continue;
            }

            var effectiveRut = c.Rut ?? string.Empty;
            var destComuna = c.CambioDomicilioComuna ?? c.LastFolderComuna ?? string.Empty;

            if (!existingBySourceId.TryGetValue(c.Id, out var existing))
            {
                var isSolicitado = c.FolderState == FolderState.CambioDomicilioSolicitado;
                var now = DateTimeOffset.UtcNow;
                var fechaIngreso = GetFechaIngreso(c);
                repository.Insert(new OutboundAddressChangeRequest
                {
                    FullName = c.FullName ?? string.Empty,
                    Rut = effectiveRut,
                    DestinationComuna = destComuna,
                    CreatedByUserId = userId,
                    SourceFolderCaseId = c.Id,
                    Status = isSolicitado ? OutboundRequestStatus.Enviada : OutboundRequestStatus.Borrador,
                    CreatedAt = fechaIngreso,
                    SentAt = isSolicitado ? now : null,
                    WorkflowState = c.FolderState
                });
            }
            else
            {
                var changed = false;
                if (!string.IsNullOrWhiteSpace(c.FullName) && existing.FullName != c.FullName)
                {
                    existing.FullName = c.FullName;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(effectiveRut) && existing.Rut != effectiveRut)
                {
                    existing.Rut = effectiveRut;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(destComuna) && existing.DestinationComuna != destComuna)
                {
                    existing.DestinationComuna = destComuna;
                    changed = true;
                }
                if (c.FolderState == FolderState.CambioDomicilioSolicitado)
                {
                    // Solo inicializar como Solicitado si no tiene estado o está en CambioDomicilio base
                    // NO sobreescribir si ya avanzó a PendienteCertificado, PendienteF8 o Subido
                    if (existing.WorkflowState is null || existing.WorkflowState == FolderState.CambioDomicilio)
                    {
                        existing.WorkflowState = FolderState.CambioDomicilioSolicitado;
                        changed = true;
                    }
                    if (existing.SentAt is null)
                    {
                        existing.SentAt = DateTimeOffset.UtcNow;
                        changed = true;
                    }
                }
                if (changed)
                {
                    repository.Update(existing);
                }
            }
        }
    }

    /// <summary>Mismo mecanismo que el "Sincronizar ahora" de F8 (MatrizSyncService): lee un Excel
    /// configurado por ruta (CambioDomicilio:SolicitarMatrizExcelPath) en vez de un buzón de
    /// correo — ver SolicitarMatrizSyncService. Sin configurar, lo indica y no hace nada, igual
    /// que F8 cuando falta F8:MatrizExcelPath.</summary>
    public IActionResult OnPostSincronizar()
    {
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = SolicitarMatrizSyncService.Sync(options.SolicitarMatrizExcelPath, repository, userId);

        Message = result.NoOp
            ? "Sincronización: configure CambioDomicilio:SolicitarMatrizExcelPath en appsettings para habilitarla."
            : result.Summary() + (result.Alerts.Count > 0 ? " Detalle: " + string.Join(" | ", result.Alerts) : string.Empty);
        return RedirectToPage();
    }

    /// <summary>Cambia el estado de la carpeta (subconjunto de WorkflowStateCatalog.Options) y, si
    /// la solicitud nació del botón "Solicitar" en Casos (SourceFolderCaseId no nulo), propaga el
    /// mismo FolderState al caso de origen — así el operador no repite el cambio en las dos
    /// pantallas. Si el caso ya no existe (borrado mientras tanto), el estado de la solicitud igual
    /// se guarda; solo la propagación se salta.</summary>
    public IActionResult OnPostGuardarEstado(long id, FolderState? workflowState)
    {
        var request = repository.FindById(id);
        if (request is null)
        {
            Message = "La solicitud ya no existe.";
            return RedirectToPage();
        }

        if (workflowState is { } state && !WorkflowStateCatalog.Options.Contains(state))
        {
            Message = "Ese estado no está disponible para solicitudes.";
            return RedirectToPage();
        }

        request.WorkflowState = workflowState;
        if (workflowState == FolderState.CambioDomicilioSolicitado && request.SentAt is null)
        {
            request.SentAt = DateTimeOffset.UtcNow;
            request.Status = OutboundRequestStatus.Enviada;
        }
        else if (workflowState == FolderState.CambioDomicilioSubidoAConaset && request.UploadedAt is null)
        {
            request.UploadedAt = DateTimeOffset.UtcNow;
        }
        repository.Update(request);

        // Solo se propaga un estado REAL (uno de los 5 del catálogo) — volver a "—" limpia el
        // campo de esta solicitud nada más. Sin este chequeo, elegir "—" mandaba null a
        // UpdateEditableFields y borraba silenciosamente el FolderState del caso de origen, que es
        // el registro autoritativo (puede tener un estado fuera de este catálogo reducido, como
        // "1° LICENCIA" o cualquier otro paso del flujo normal de Casos).
        if (workflowState is { } newState && request.SourceFolderCaseId is { } folderCaseId)
        {
            var folderCase = cases.FindById(folderCaseId);
            if (folderCase is not null)
            {
                cases.UpdateEditableFields(folderCase.Id, folderCase.FullName, folderCase.Rut, folderCase.CitationDate,
                    folderCase.FolderUploadedDate, folderCase.LastFolderDate, folderCase.LastFolderComuna,
                    newState, folderCase.FinalDecision, folderCase.MoralIdoneity,
                    folderCase.AttentionNote, folderCase.NeedsReview, cambioDomicilioComuna: folderCase.CambioDomicilioComuna);
            }
        }

        return RedirectToPage();
    }

    /// <summary>Envía una solicitud Borrador directo desde el listado — vía OutboundRequestSender,
    /// el mismo servicio que usa el botón "Solicitar" de Casos, para no duplicar la búsqueda de
    /// contacto ni el armado del correo.</summary>
    public async Task<IActionResult> OnPostSolicitar(long id)
    {
        var request = repository.FindById(id);
        if (request is null)
        {
            Message = "La solicitud no existe.";
            return RedirectToPage();
        }

        // Auto-resolve DestinationComuna from linked case if missing
        if (string.IsNullOrWhiteSpace(request.DestinationComuna) && request.SourceFolderCaseId is { } scId)
        {
            var fc = cases.FindById(scId);
            if (fc is not null)
            {
                var resolved = fc.CambioDomicilioComuna ?? fc.LastFolderComuna;
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    request.DestinationComuna = resolved;
                    repository.Update(request);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Rut))
        {
            Message = "Falta nombre o RUT — revise los datos de la solicitud.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(request.DestinationComuna))
        {
            Message = $"Debe indicar la comuna de destino para solicitar la carpeta de {request.FullName}.";
            return RedirectToPage();
        }

        var attachments = repository.GetAttachments(id);
        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) is { } uid && long.TryParse(uid, out var u) ? u : 1L;
        
        var result = request.Status == OutboundRequestStatus.Borrador
            ? await sender.SendAsync(request, attachments, userId)
            : new OutboundSendResult(OutboundSendOutcome.AlreadySent, request.DestinationComuna);

        var now = DateTimeOffset.UtcNow;
        var updated = repository.FindById(id) ?? request;
        updated.SentAt ??= now;
        updated.WorkflowState = FolderState.CambioDomicilioSolicitado;
        if (result.Outcome == OutboundSendOutcome.Sent)
        {
            updated.Status = OutboundRequestStatus.Enviada;
        }
        repository.Update(updated);

        if (updated.SourceFolderCaseId is { } folderCaseId)
        {
            var folderCase = cases.FindById(folderCaseId);
            if (folderCase is not null)
            {
                var effectiveComuna = !string.IsNullOrWhiteSpace(folderCase.CambioDomicilioComuna)
                    ? folderCase.CambioDomicilioComuna
                    : updated.DestinationComuna;

                cases.UpdateEditableFields(folderCase.Id, folderCase.FullName, folderCase.Rut, folderCase.CitationDate,
                    folderCase.FolderUploadedDate, folderCase.LastFolderDate, folderCase.LastFolderComuna,
                    FolderState.CambioDomicilioSolicitado, folderCase.FinalDecision, folderCase.MoralIdoneity,
                    folderCase.AttentionNote, folderCase.NeedsReview, cambioDomicilioComuna: effectiveComuna);
            }
        }

        Message = result.Outcome switch
        {
            OutboundSendOutcome.NoContact =>
                $"Carpeta de {updated.FullName} registrada como CARPETA SOLICITADA el {now.LocalDateTime:dd-MM-yyyy}. (Aviso: No hay correo registrado para '{result.DestinationComuna}').",
            OutboundSendOutcome.SendFailed =>
                $"Carpeta de {updated.FullName} registrada como CARPETA SOLICITADA el {now.LocalDateTime:dd-MM-yyyy}. (Aviso: el servidor de correo institucional no respondió).",
            OutboundSendOutcome.Sent => options.DisableOutgoingEmails
                ? $"Carpeta de {updated.FullName} registrada como CARPETA SOLICITADA a {result.DestinationComuna} el {now.LocalDateTime:dd-MM-yyyy} (Modo prueba: correo simulado sin envío real)."
                : $"Solicitud enviada exitosamente a {result.DestinationComuna}. Estado: CARPETA SOLICITADA.",
            _ => $"Carpeta de {updated.FullName} registrada con fecha de petición y estado CARPETA SOLICITADA."
        };
        return RedirectToPage();
    }

    public IActionResult OnPostSubida(long id)
    {
        var request = repository.FindById(id);
        if (request is null)
        {
            Message = "La solicitud no existe.";
            return RedirectToPage();
        }

// No bloquear ejecución de Subida

        var now = DateTimeOffset.UtcNow;
        request.UploadedAt = now;
        request.WorkflowState = FolderState.CambioDomicilioSubidoAConaset;
        repository.Update(request);

        if (request.SourceFolderCaseId is { } folderCaseId)
        {
            var folderCase = cases.FindById(folderCaseId);
            if (folderCase is not null)
            {
                var effectiveComuna = !string.IsNullOrWhiteSpace(folderCase.CambioDomicilioComuna)
                    ? folderCase.CambioDomicilioComuna
                    : request.DestinationComuna;

                cases.UpdateEditableFields(folderCase.Id, folderCase.FullName, folderCase.Rut, folderCase.CitationDate,
                    DateOnly.FromDateTime(DateTime.Today), folderCase.LastFolderDate, folderCase.LastFolderComuna,
                    FolderState.CambioDomicilioSubidoAConaset, folderCase.FinalDecision, folderCase.MoralIdoneity,
                    folderCase.AttentionNote, folderCase.NeedsReview, cambioDomicilioComuna: effectiveComuna);
            }
        }

        Message = $"Carpeta de {request.FullName} marcada como CAMBIO DE DOMICILIO SUBIDO A CONASET el {now.LocalDateTime:dd-MM-yyyy}.";
        return RedirectToPage();
    }

        public IActionResult OnPostCertificado(long id)
    {
        var request = repository.FindById(id);
        if (request is null)
        {
            Message = "La solicitud no existe.";
            return RedirectToPage();
        }

// No bloquear ejecución de Certificado

        request.WorkflowState = FolderState.PendienteCertificado;
        repository.Update(request);

        // No se sincroniza con Gestión de Licencias aquí — Carpetas para Certificados
        // será la sección responsable de actualizar el estado en FolderCase al confirmar.

        if (certificadoRepo is not null)
        {
            var existing = certificadoRepo.FindByRut(request.Rut);
            if (existing is null)
            {
                var cert = new LicenciasCarpetas.Certificados.Domain.CertificadoRequest
                {
                    NombreCompleto = request.FullName,
                    Rut = request.Rut,
                    Comuna = request.DestinationComuna,
                    FechaIngreso = DateOnly.FromDateTime(request.CreatedAt.LocalDateTime),
                    FechaPeticion = request.SentAt.HasValue ? DateOnly.FromDateTime(request.SentAt.Value.LocalDateTime) : null,
                    Estado = "Pendiente",
                    EstadoActual = "PENDIENTE CERTIFICADO",
                    Origin = "Solicitar",
                    SourceId = request.Id
                };
                certificadoRepo.Insert(cert);
            }
            else
            {
                existing.EstadoActual = "PENDIENTE CERTIFICADO";
                if (string.IsNullOrWhiteSpace(existing.Comuna) && !string.IsNullOrWhiteSpace(request.DestinationComuna))
                {
                    existing.Comuna = request.DestinationComuna;
                }
                certificadoRepo.Update(existing);
            }
        }

        Message = $"Caso de {request.FullName} transferido a Carpetas para Certificados (PEDIR CERTIFICADO).";
        return RedirectToPage();
    }
    public IActionResult OnPostTransferToF8(long id)
    {
        var request = repository.FindById(id);
        if (request is null)
        {
            Message = "La solicitud no existe.";
            return RedirectToPage();
        }

// No bloquear ejecución de TransferToF8

        if (urgentRequests is not null)
        {
            var f8Rut = LicenciasCarpetas.F8.Domain.Rut.TryParse(request.Rut, out var parsedRut)
                ? parsedRut.ToString()
                : request.Rut;

            FolderCase? folderCase = null;
            if (request.SourceFolderCaseId is { } scId)
            {
                folderCase = cases.FindById(scId);
            }
            if (folderCase is null && !string.IsNullOrWhiteSpace(request.Rut))
            {
                var normRut = RutValidator.NormalizeAndValidate(request.Rut) ?? request.Rut.Trim();
                folderCase = cases.QueryAll(new CaseFilter { Search = normRut }).FirstOrDefault();
            }

            var existing = urgentRequests.FindByRut(f8Rut);
            if (existing is null)
            {
                urgentRequests.Insert(new LicenciasCarpetas.F8.Domain.UrgentRequest
                {
                    NombreCompleto = request.FullName,
                    Rut = f8Rut,
                    RutRaw = request.Rut?.Trim(),
                    FechaPeticion = request.SentAt.HasValue
                        ? DateOnly.FromDateTime(request.SentAt.Value.LocalDateTime)
                        : DateOnly.FromDateTime(DateTime.Today),
                    FechaUltimaCarpeta = folderCase?.LastFolderDate,
                    CodigoF8 = folderCase?.CodigoF8,
                    FechaPenultimaCarpeta = folderCase?.PenultimateFolderDate,
                    Origin = "CambioDomicilio",
                    Estado = "SOLICITADA",
                    EstadoActual = "PENDIENTE",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                var changed = false;
                if (!string.IsNullOrWhiteSpace(folderCase?.CodigoF8) && existing.CodigoF8 != folderCase.CodigoF8)
                {
                    existing.CodigoF8 = folderCase.CodigoF8;
                    changed = true;
                }
                if (folderCase?.PenultimateFolderDate is not null && existing.FechaPenultimaCarpeta != folderCase.PenultimateFolderDate)
                {
                    existing.FechaPenultimaCarpeta = folderCase.PenultimateFolderDate;
                    changed = true;
                }
                if (folderCase?.LastFolderDate is not null && existing.FechaUltimaCarpeta != folderCase.LastFolderDate)
                {
                    existing.FechaUltimaCarpeta = folderCase.LastFolderDate;
                    changed = true;
                }
                if (changed)
                {
                    urgentRequests.Update(existing);
                }
            }
        }

        request.WorkflowState = FolderState.PendienteF8;
        repository.Update(request);

        // No se sincroniza con Gestión de Licencias aquí — F8 Urgentes
        // será la sección responsable de actualizar el estado en FolderCase al confirmar.

        Message = $"Datos de {request.FullName} traspasados exitosamente a la sección F8 Urgentes (SUBIR CON F8).";
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(long id)
    {
        var request = repository.FindById(id);
        if (request is not null)
        {
            if (request.SourceFolderCaseId is { } folderCaseId)
            {
                repository.RecordDeletedSourceFolderCase(folderCaseId);
                var folderCase = cases.FindById(folderCaseId);
                if (folderCase is not null)
                {
                    cases.UpdateEditableFields(folderCase.Id, folderCase.FullName, folderCase.Rut, folderCase.CitationDate,
                        folderCase.FolderUploadedDate, folderCase.LastFolderDate, folderCase.LastFolderComuna,
                        folderState: null, folderCase.FinalDecision, folderCase.MoralIdoneity,
                        folderCase.AttentionNote, folderCase.NeedsReview, cambioDomicilioComuna: null);
                }
            }
            repository.Delete(id);
        }
        Message = "Solicitud eliminada.";
        return RedirectToPage();
    }
}
