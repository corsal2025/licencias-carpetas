using System.Security.Claims;

namespace LicenciasCarpetas.Dashboard.Auth;

public enum SessionCheck
{
    Valid,
    Rejected,
    Replaced
}

/// <summary>Decides what to do with an open session on each request: keep it, drop it (account
/// deleted) or rebuild it (role/modules/offices changed, detected by the security stamp).</summary>
public sealed class SessionRevalidator(IUserRepository users, IUserOfficeRepository userOffices)
{
    public (SessionCheck Result, ClaimsPrincipal? Principal) Check(ClaimsPrincipal current)
    {
        var idClaim = current.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(idClaim, out var userId) || users.FindById(userId) is not { } user)
        {
            return (SessionCheck.Rejected, null);
        }

        if (current.FindFirst(ClaimsFactory.SecurityStampClaim)?.Value == user.SecurityStamp)
        {
            return (SessionCheck.Valid, current);
        }

        return (SessionCheck.Replaced, ClaimsFactory.Create(user, userOffices.For(user.Id)));
    }
}
