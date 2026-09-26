using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize(Roles = "Administrador,Jefatura,Coordinador")]
public class PapeleraModel(IScopedCaseRepository cases) : PageModel
{
    public IReadOnlyList<FolderCase> Cases { get; private set; } = [];

    public string? Message { get; set; }

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPostRestore(long id)
    {
        cases.Restore(id);
        TempData["Message"] = "Caso restaurado. Vuelve a aparecer en Casos.";
        return RedirectToPage();
    }

    /// <summary>The only place in the app where data really disappears.</summary>
    public IActionResult OnPostDeletePermanently(long id)
    {
        cases.DeletePermanently(id);
        TempData["Message"] = "Caso eliminado definitivamente.";
        return RedirectToPage();
    }

    private void Load()
    {
        Cases = cases.Deleted();
        Message = TempData["Message"] as string;
    }
}
