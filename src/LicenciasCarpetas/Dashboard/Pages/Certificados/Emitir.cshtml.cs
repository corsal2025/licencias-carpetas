using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.Certificados.Data;
using LicenciasCarpetas.Certificados.Domain;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Dashboard.Pages.Certificados;

[Authorize(Policy = "CambioDomicilioAccess")]
public class EmitirModel(
    ICertificadoRequestRepository repository,
    IOutboundAddressChangeRequestRepository? outboundRepo = null,
    LicenciasCarpetas.Persistence.IFolderCaseRepository? cases = null) : PageModel
{
    public CertificadoRequest? CaseItem { get; private set; }
    public string NumeroFolio { get; private set; } = string.Empty;
    public DateOnly FechaHoy { get; private set; } = DateOnly.FromDateTime(DateTime.Today);

    public IActionResult OnGet(long id)
    {
        CaseItem = repository.FindById(id);
        if (CaseItem is null)
        {
            return RedirectToPage("/Certificados/Index");
        }

        NumeroFolio = CaseItem.Folio ?? $"CD-{DateTime.Today.Year}-{CaseItem.Id:D4}";
        return Page();
    }

    public IActionResult OnPostConfirmarEmision(long id, string folio, string direccion)
    {
        var item = repository.FindById(id);
        if (item is null)
        {
            return RedirectToPage("/Certificados/Index");
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        item.Direccion = direccion;
        repository.SetEmitido(id, today, folio);

        if (outboundRepo is not null)
        {
            LicenciasCarpetas.CambioDomicilio.Domain.OutboundAddressChangeRequest? outbound = null;
            if (item.SourceId is { } sourceId and > 0)
            {
                outbound = outboundRepo.FindById(sourceId);
            }
            if (outbound is null && !string.IsNullOrWhiteSpace(item.Rut))
            {
                var normalizedRut = RutValidator.NormalizeAndValidate(item.Rut) ?? item.Rut.Trim();
                outbound = outboundRepo.GetAll().FirstOrDefault(r =>
                    (RutValidator.NormalizeAndValidate(r.Rut) ?? r.Rut.Trim()) == normalizedRut
                    && r.WorkflowState != FolderState.CambioDomicilioSubidoAConaset);
            }

            if (outbound is not null)
            {
                outbound.WorkflowState = FolderState.CambioDomicilioSubidoAConaset;
                outbound.UploadedAt = DateTimeOffset.UtcNow;
                outboundRepo.Update(outbound);

                if (cases is not null && outbound.SourceFolderCaseId is { } caseId)
                {
                    var fc = cases.FindById(caseId);
                    if (fc is not null)
                    {
                        cases.UpdateEditableFields(fc.Id, fc.FullName, fc.Rut, fc.CitationDate,
                            today, fc.LastFolderDate, fc.LastFolderComuna,
                            FolderState.CambioDomicilioSubidoAConaset, fc.FinalDecision, fc.MoralIdoneity,
                            fc.AttentionNote, fc.NeedsReview, cambioDomicilioComuna: fc.CambioDomicilioComuna);
                    }
                }
            }
        }

        return RedirectToPage("/Certificados/Emitir", new { id });
    }
}
