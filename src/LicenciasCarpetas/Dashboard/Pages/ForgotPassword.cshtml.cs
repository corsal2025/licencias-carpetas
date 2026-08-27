using LicenciasCarpetas.Dashboard.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

public class ForgotPasswordModel(UserProvisioning provisioning, IUserRepository users) : PageModel
{
    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public IReadOnlyList<string> AvailableUsernames { get; set; } = [];

    public void OnGet()
    {
        AvailableUsernames = users.AllUsernames();
    }

    public IActionResult OnPost()
    {
        AvailableUsernames = users.AllUsernames();

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Seleccione o escriba el nombre de usuario.";
            return Page();
        }

        var result = provisioning.SetPassword(Username, NewPassword, ConfirmPassword);
        if (result != ProvisioningResult.Created)
        {
            ErrorMessage = UserProvisioning.Describe(result);
            return Page();
        }

        TempData["Message"] = $"Contraseña para '{Username}' actualizada correctamente. Ingrese con su nueva clave.";
        return RedirectToPage("/Login");
    }
}
