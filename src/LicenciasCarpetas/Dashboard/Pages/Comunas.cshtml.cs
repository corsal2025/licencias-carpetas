using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize(Roles = "Administrador,Jefatura,Coordinador")]
public class ComunasModel(IComunaContactRepository contacts) : PageModel
{
    public IReadOnlyList<ComunaContact> Contacts { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public string? Message { get; set; }

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPostAdd(string comuna, string correo)
    {
        if (string.IsNullOrWhiteSpace(comuna) || string.IsNullOrWhiteSpace(correo) || !correo.Contains('@'))
        {
            TempData["Message"] = "Comuna y correo válido son obligatorios.";
            return RedirectToPage(new { search = Search });
        }

        contacts.Upsert(new ComunaContact { Comuna = comuna.Trim(), Email = correo.Trim() });
        TempData["Message"] = $"Contacto de {comuna.Trim()} guardado.";
        return RedirectToPage(new { search = Search });
    }

    public IActionResult OnPostEdit(long id, string comuna, string correo)
    {
        var isAjax = string.Equals(HttpContext?.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(comuna) || string.IsNullOrWhiteSpace(correo) || !correo.Contains('@'))
        {
            if (isAjax) return new JsonResult(new { ok = false, message = "Comuna y correo válido son obligatorios." });
            TempData["Message"] = "Comuna y correo válido son obligatorios.";
            return RedirectToPage(new { search = Search });
        }

        contacts.Update(id, comuna.Trim().ToUpperInvariant(), correo.Trim());
        if (isAjax) return new JsonResult(new { ok = true, message = $"Comuna {comuna.Trim().ToUpperInvariant()} actualizada." });

        TempData["Message"] = $"Comuna {comuna.Trim().ToUpperInvariant()} actualizada.";
        return RedirectToPage(new { search = Search });
    }

    public IActionResult OnPostDelete(long id)
    {
        contacts.Delete(id);
        TempData["Message"] = "Contacto eliminado.";
        return RedirectToPage(new { search = Search });
    }

    private void Load()
    {
        Contacts = contacts.All(Search);
        Message = TempData["Message"] as string;
    }
}
