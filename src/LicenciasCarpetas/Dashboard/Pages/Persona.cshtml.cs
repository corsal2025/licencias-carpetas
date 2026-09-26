using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

/// <summary>
/// Ficha de persona: todo lo registrado para un RUT, en solo lectura. El RUT viaja por POST y se
/// muestra tras un redirect (PRG) guardado en TempData, así nunca queda en la URL, el historial
/// del navegador ni los logs de un proxy.
/// </summary>
[Authorize]
public class PersonaModel(IPersonFileQuery query) : PageModel
{
    internal const string RutKey = "PersonaRut";

    public PersonFile? File { get; private set; }

    public string? Error { get; private set; }

    public bool CanSeeF8 => User.IsInRole("Administrador") || User.IsInRole("Jefatura") || User.HasClaim("mod:f8-urgentes", "true");

    public bool CanSeeCambioDomicilio => User.IsInRole("Administrador") || User.IsInRole("Jefatura") || User.HasClaim("mod:cambio-domicilio", "true");

    public void OnGet()
    {
        Error = TempData["PersonaError"] as string;
        if (TempData.Peek(RutKey) is string rut)
        {
            File = query.Load(rut, PersonFileScope.All);
        }
    }

    public IActionResult OnPost(string? rut)
    {
        var normalized = RutValidator.NormalizeAndValidate(rut);
        if (normalized is null)
        {
            TempData.Remove(RutKey);
            TempData["PersonaError"] = string.IsNullOrWhiteSpace(rut)
                ? "Ingrese un RUT."
                : "RUT inválido: revise el dígito verificador.";
            return RedirectToPage();
        }

        TempData[RutKey] = normalized;
        return RedirectToPage();
    }
}
