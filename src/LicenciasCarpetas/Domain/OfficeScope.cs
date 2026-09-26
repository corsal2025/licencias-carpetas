namespace LicenciasCarpetas.Domain;

/// <summary>Which offices (sedes) a user may see. <see cref="AllowedOffices"/> null = every office.
/// Built from the user's claims (per-office roles) and passed to every query that reads cases.</summary>
public sealed record OfficeScope(IReadOnlyCollection<Office>? AllowedOffices)
{
    public static OfficeScope All { get; } = new((IReadOnlyCollection<Office>?)null);

    public bool IsUnrestricted => AllowedOffices is null;

    public bool Allows(Office office) => AllowedOffices is null || AllowedOffices.Contains(office);

    /// <summary>The offices to show, in enum order.</summary>
    public IReadOnlyList<Office> Offices => [.. Enum.GetValues<Office>().Where(Allows)];

    /// <summary>Narrows the scope to one office (a screen filter). An office outside the scope
    /// yields an empty scope, never a wider one.</summary>
    public OfficeScope Narrow(Office? office) => office is { } only
        ? new OfficeScope(Allows(only) ? [only] : [])
        : this;
}
