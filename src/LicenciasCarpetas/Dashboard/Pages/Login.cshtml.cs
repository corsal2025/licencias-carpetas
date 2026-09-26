using LicenciasCarpetas.Dashboard.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

public class LoginModel(ILoginService loginService, IUserRepository users, UserProvisioning provisioning,
    IUserOfficeRepository userOffices) : PageModel
{
    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    /// <summary>Confirmation carried over from the first-run screen.</summary>
    public string? Notice { get; set; }

    /// <summary>A brand-new installation has nowhere to sign in from: send it to create an account
    /// instead of showing a login form that cannot possibly succeed.</summary>
    public IActionResult OnGet()
    {
        if (provisioning.HasNoUsers())
        {
            return RedirectToPage("/Setup");
        }

        Notice = TempData["Message"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var outcome = await loginService.TryLoginAsync(Username, Password);

        if (outcome != LoginOutcome.Success)
        {
            ErrorMessage = outcome == LoginOutcome.LockedOut
                ? "Cuenta bloqueada temporalmente por intentos fallidos. Intente más tarde."
                : "Usuario o contraseña incorrectos.";
            return Page();
        }

        var user = users.FindByUsername(Username)!;
        var principal = ClaimsFactory.Create(user, userOffices.For(user.Id));
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
            AllowRefresh = true
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        return RedirectToPage("/Index");
    }
}
