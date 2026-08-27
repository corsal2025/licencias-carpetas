namespace LicenciasCarpetas.Domain;

/// <summary>"IDONEIDAD MORAL" column — flags a citation whose background check needs attention.</summary>
public enum MoralIdoneity
{
    Alertada,
    Revisar,
    Aceptada
}

public static class MoralIdoneityCatalog
{
    private static readonly Dictionary<MoralIdoneity, string> Displays = new()
    {
        [MoralIdoneity.Alertada] = "ALERTADA",
        [MoralIdoneity.Revisar] = "REVISAR",
        [MoralIdoneity.Aceptada] = "ACEPTADA"
    };

    public static IReadOnlyList<MoralIdoneity> All { get; } = [.. Displays.Keys];

    public static string Display(MoralIdoneity value) => Displays[value];

    public static MoralIdoneity? TryResolve(string? text)
    {
        var key = TextNormalizer.NormalizeLoose(text);
        if (key.Length == 0)
        {
            return null;
        }

        foreach (var (value, display) in Displays)
        {
            if (key == TextNormalizer.NormalizeLoose(display))
            {
                return value;
            }
        }

        return null;
    }
}
