using System.Security.Claims;
using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Dashboard.Auth;

/// <summary>Reads the per-office scope off the signed-in principal. Administrador and Jefatura see
/// every office; everyone else sees exactly the offices in their <c>office</c> claims — none if
/// they have none (the safe default for a principal built before offices existed).</summary>
public static class OfficeScopeClaims
{
    public const string ClaimType = "office";

    public static bool IsUnrestrictedRole(UserRole role) => role is UserRole.Administrador or UserRole.Jefatura;

    public static OfficeScope From(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return new OfficeScope([]);
        }

        if (user.IsInRole(nameof(UserRole.Administrador)) || user.IsInRole(nameof(UserRole.Jefatura)))
        {
            return OfficeScope.All;
        }

        var offices = user.FindAll(ClaimType)
            .Select(claim => Enum.TryParse<Office>(claim.Value, out var office) ? office : (Office?)null)
            .OfType<Office>()
            .Distinct()
            .ToList();
        return new OfficeScope(offices);
    }
}
