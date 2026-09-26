using System.Security.Claims;
using LicenciasCarpetas.Domain;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LicenciasCarpetas.Dashboard.Auth;

/// <summary>The one place that turns an account into session claims — used at login and when a
/// session is rebuilt after its permissions changed, so the two can never disagree.</summary>
public static class ClaimsFactory
{
    public const string SecurityStampClaim = "stamp";

    public static ClaimsPrincipal Create(DashboardUser user, IReadOnlyCollection<Office> offices)
    {
        // Acceso a módulos externos: incondicional salvo para Administrativo, donde se decide
        // persona por persona (ver DashboardUser.CanAccessCambioDomicilio/F8Urgentes).
        var canAccessCambioDomicilio = UserRoleCatalog.HasFullModuleAccess(user.Role) || user.CanAccessCambioDomicilio;
        var canAccessF8Urgentes = UserRoleCatalog.HasFullModuleAccess(user.Role) || user.CanAccessF8Urgentes;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("mod:cambio-domicilio", canAccessCambioDomicilio ? "true" : "false"),
            new("mod:f8-urgentes", canAccessF8Urgentes ? "true" : "false"),
            new(SecurityStampClaim, user.SecurityStamp ?? string.Empty)
        };

        // Administrador y Jefatura no llevan sedes: su alcance total sale del rol.
        if (!OfficeScopeClaims.IsUnrestrictedRole(user.Role))
        {
            claims.AddRange(offices.Distinct().Order().Select(office => new Claim(OfficeScopeClaims.ClaimType, office.ToString())));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
