namespace LicenciasCarpetas.Dashboard.Auth;

public sealed class DashboardUser
{
    public long Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public int Iterations { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public UserRole Role { get; set; } = UserRole.Administrador;

    /// <summary>Solo se consulta cuando <see cref="Role"/> es Administrativo — para el resto del
    /// escalafón el acceso a módulos externos es incondicional.</summary>
    public bool CanAccessCambioDomicilio { get; set; }
    public bool CanAccessF8Urgentes { get; set; }

    /// <summary>Personal signature appended to outgoing Cambio de Domicilio emails (confirmación,
    /// rectificación) — set once, reused on every send. Null/empty means no footer.</summary>
    public string? EmailFooter { get; set; }

    /// <summary>Changes whenever the account's role, modules or offices change; open sessions whose
    /// "stamp" claim no longer matches are rebuilt on their next request.</summary>
    public string? SecurityStamp { get; set; }
}
