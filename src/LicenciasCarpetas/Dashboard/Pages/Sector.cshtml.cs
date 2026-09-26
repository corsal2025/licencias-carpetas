using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize(Roles = "Administrador,Jefatura,Coordinador")]
public class SectorModel(IScopedCaseRepository cases) : PageModel
{
    public IReadOnlyList<FolderCase> Cases { get; private set; } = [];

    public FolderSector SelectedSector { get; private set; } = FolderSector.Archivo;

    /// <summary>Only the cases the operator ticked — the usual way to build the list of folders to
    /// physically pull. Unticked lists are available too, for a full sector inventory.</summary>
    public bool OnlyMarked { get; private set; } = true;

    /// <summary>Include folders already requested on an earlier list. Off by default, so a reprint
    /// does not ask Archivo for the same folder twice.</summary>
    public bool IncludePrinted { get; private set; }

    public string? Message { get; private set; }

    /// <summary>La lista llegó al tope de filas: el documento no trae todas las carpetas del sector.
    /// Decirlo importa — es un papel que se firma, y un total incompleto presentado como total es
    /// peor que no imprimirlo.</summary>
    public bool ReachedLimit => Cases.Count >= FolderCaseRepository.SectorListLimit;

    /// <summary>Citation day the document is being prepared for, if the operator picked one.</summary>
    public DateOnly? CitationDay { get; private set; }

    public int? Year { get; private set; }

    public int? Month { get; private set; }

    /// <summary>Human wording of the chosen period, printed on the document itself so the paper
    /// says what it covers.</summary>
    public string PeriodLabel => CitationDay is { } day
        ? $"Citados el {SpanishDate.FormatWithMonthName(day)}"
        : Year is { } year
            ? Month is { } month
                ? $"Citados en {SpanishDate.MonthName(month)} {year}"
                : $"Citados en {year}"
            : "Todas las carpetas pendientes";

    /// <summary>Who is signing the document and when — printed in the header of the report.</summary>
    public string OperatorName => User.Identity?.Name ?? "—";

    public DateOnly IssuedOn => DateOnly.FromDateTime(DateTime.Today);

    public void OnGet(string? sector, bool onlyMarked = true, bool includePrinted = false,
        string? dia = null, int? anio = null, int? mes = null)
    {
        Load(sector, onlyMarked, includePrinted, dia, anio, mes);
    }

    /// <summary>
    /// Records the listed folders as requested, so the next document only brings what is still
    /// pending. Deliberately a separate button from printing: the browser cannot tell this page
    /// whether the print dialog ended in paper or in "cancel".
    /// </summary>
    public IActionResult OnPostMarkPrinted(string? sector, bool onlyMarked = true, bool includePrinted = false,
        string? dia = null, int? anio = null, int? mes = null)
    {
        // Se marca exactamente lo que salió en el documento, período incluido: si el operador
        // imprimió solo el martes, marcar todo el mes daría por pedidas carpetas que nadie pidió.
        Load(sector, onlyMarked, includePrinted, dia, anio, mes);

        var ids = Cases.Select(item => item.Id).ToList();
        cases.MarkSectorPrinted(ids);

        if (TempData is not null)
        {
            TempData["Message"] = $"{ids.Count} carpeta(s) quedaron registradas como pedidas.";
        }

        return RedirectToPage(new { sector = SelectedSector.ToString(), onlyMarked, includePrinted, dia, anio, mes });
    }

    public IActionResult OnPostRequestAgain(long id, string? sector, bool onlyMarked = true)
    {
        cases.ClearSectorPrinted(id);
        if (TempData is not null)
        {
            TempData["Message"] = "La carpeta vuelve a la lista de pendientes.";
        }

        return RedirectToPage(new { sector, onlyMarked, includePrinted = true });
    }

    private void Load(string? sector, bool onlyMarked, bool includePrinted,
        string? dia = null, int? anio = null, int? mes = null)
    {
        SelectedSector = Enum.TryParse<FolderSector>(sector, ignoreCase: true, out var parsed)
            ? parsed
            : FolderSector.Archivo;
        OnlyMarked = onlyMarked;
        IncludePrinted = includePrinted;

        // El día llega del <input type="date"> como aaaa-mm-dd.
        CitationDay = DateOnly.TryParse(dia, out var day) ? day : null;
        Year = anio;
        Month = mes;

        Cases = cases.ForSector(SelectedSector, onlyMarked, includePrinted, CitationDay, Year, Month);
        Message = TempData?["Message"] as string;
    }

    public static string SectorTitle(FolderSector sector) => FolderSectorCatalog.Display(sector);
}
