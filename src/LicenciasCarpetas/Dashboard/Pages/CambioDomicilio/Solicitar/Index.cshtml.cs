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
        FolderState.SubidaConF8,
        FolderState.SubidaConOficio,
        FolderState.CambioDomicilioSubidoAConaset
    ];

    public static string Display(FolderState state) => state == FolderState.SubidaConOficio
        ? "SUBIDO CON CERTIFICADO"
        : FolderStateCatalog.Display(state);
}

/// <summary>Worklist for outbound "solicitud de cambio de domicilio" requests — the mirror
/// direction of the module's Index (which tracks requests other comunas send TO Valparaíso):
/// here Valparaíso builds and sends its own requests to other comunas.</summary>
[Authorize(Policy = "CambioDomicilioAccess")]
public sealed class IndexModel(
    IOutboundAddressChangeRequestRepository repository,
    OutboundRequestSender sender,
    IFolderCaseRepository cases,
    CambioDomicilioOptions options) : PageModel
{
    [TempData(Key = "SolicitarMessage")]
    public string? Message { get; set; }

    public IReadOnlyList<OutboundAddressChangeRequest> Requests { get; private set; } = [];

    public void OnGet() => Requests = repository.GetAll();

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
        if (request is null || request.Status != OutboundRequestStatus.Borrador)
        {
            Message = "La solicitud no existe o ya fue enviada.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Rut)
            || string.IsNullOrWhiteSpace(request.DestinationComuna))
        {
            // Ya no hay pantalla de edición desde que se sacó "+ Nueva Solicitud" — esto solo
            // puede pasar por un dato incompleto en el Excel de sincronización, así que el aviso
            // apunta ahí en vez de a una acción que ya no existe.
            Message = "Falta nombre, RUT o comuna — revise la fila en el Excel de sincronización.";
            return RedirectToPage();
        }

        var attachments = repository.GetAttachments(id);
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await sender.SendAsync(request, attachments, userId);

        Message = result.Outcome switch
        {
            OutboundSendOutcome.NoContact =>
                $"No hay correo de contacto registrado para '{result.DestinationComuna}'. Agréguelo en Comunas antes de enviar.",
            OutboundSendOutcome.SendFailed =>
                "No se pudo enviar el correo: el servidor de correo institucional (EWS) no respondió. " +
                "La solicitud sigue como Borrador — reintente cuando el correo vuelva.",
            OutboundSendOutcome.Sent => $"Solicitud enviada a {result.DestinationComuna}.",
            _ => "El correo se envió, pero la solicitud ya figuraba como enviada (posiblemente por otra pestaña/operador)."
        };
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(long id)
    {
        repository.Delete(id);
        Message = "Solicitud eliminada.";
        return RedirectToPage();
    }
}
