using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Data;

namespace LicenciasCarpetas.Dashboard.Pages.CambioDomicilio;

[Authorize(Policy = "CambioDomicilioAccess")]
public class SectorModel(ICambioDomicilioRequestRepository repository) : PageModel
{
    public FolderSector SelectedSector { get; private set; }
    public IReadOnlyList<PersonRequest> Cases { get; private set; } = [];
    public string OperatorName => User.Identity?.Name ?? "—";

    /// <summary>Cases the operator checked (Marcar, on Casos) show up here — that checkbox is how
    /// the operator picks which contributors get categorized into a sector document, before
    /// printing.</summary>
    public void OnGet(FolderSector sector)
    {
        SelectedSector = sector;
        var sectorCases = repository.GetAll()
            .Where(c => c.Sector == sector && c.SectorPdfGeneratedAt is null && c.TransferredAt is null)
            .ToList();

        Cases = sectorCases.Any(c => c.Marked)
            ? sectorCases.Where(c => c.Marked).OrderBy(c => c.MarkedAt).ToList()
            : sectorCases.OrderBy(c => c.CreatedAt).ToList();
    }

    /// <summary>Marks every case currently shown for this sector as already printed, so the next
    /// visit to this page starts blank instead of repeating names already requested. Called via
    /// fetch() right before window.print(), so the page being printed still shows the full list.</summary>
    public IActionResult OnPostMarkPrinted(FolderSector sector)
    {
        MarkAllVisibleAsPrinted(sector);
        return new EmptyResult();
    }

    /// <summary>Operator-triggered removal of a single case from this sector's document — the case
    /// itself is untouched (still visible in Casos), it just stops appearing here.</summary>
    public IActionResult OnPostRemoveOne(long id, FolderSector sector)
    {
        repository.SetSectorPdfGenerated(id, DateTimeOffset.UtcNow);
        return RedirectToPage(new { sector });
    }

    /// <summary>Operator-triggered: empties the entire sector document (marks every currently-shown
    /// case as printed) without going through the print dialog — lets the operator start a fresh
    /// list on demand. Only affects this sector's document; the cases themselves (and their data)
    /// are untouched and remain visible in Casos.</summary>
    public IActionResult OnPostClearAll(FolderSector sector)
    {
        MarkAllVisibleAsPrinted(sector);
        return RedirectToPage(new { sector });
    }

    private void MarkAllVisibleAsPrinted(FolderSector sector)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in repository.GetAll().Where(c => c.Sector == sector && c.Marked && c.SectorPdfGeneratedAt is null && c.TransferredAt is null))
        {
            repository.SetSectorPdfGenerated(item.Id, now);
        }
    }
}
