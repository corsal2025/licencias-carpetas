namespace LicenciasCarpetas.Domain;

/// <summary>"DECISIÓN FINAL" column — the outcome of the citation once the folder was reviewed.</summary>
public enum FinalDecision
{
    // El valor se guarda en la base como el número ordinal (FolderCaseRepository), así que los
    // valores nuevos van SIEMPRE al final: reordenar los de arriba reinterpreta datos ya guardados.
    Otorgado,
    Denegado,
    ParaDenegar,
    EsperaExamen,
    SinSgl,
    ClasePendiente,
    ExamenMedico,
    ExamenTeorico,
    ExamenPractico
}

public static class FinalDecisionCatalog
{
    private static readonly Dictionary<FinalDecision, string> Displays = new()
    {
        [FinalDecision.Otorgado] = "OTORGADO",
        [FinalDecision.Denegado] = "DENEGADO",
        [FinalDecision.ParaDenegar] = "PARA DENEGAR",
        [FinalDecision.EsperaExamen] = "ESPERA EXAMEN",
        [FinalDecision.SinSgl] = "S/SGL",
        [FinalDecision.ClasePendiente] = "CLASE PENDIENTE",
        [FinalDecision.ExamenMedico] = "EX. MÉDICO",
        [FinalDecision.ExamenTeorico] = "EX. TEÓRICO",
        [FinalDecision.ExamenPractico] = "EX. PRÁCTICO"
    };

    private static readonly Dictionary<string, FinalDecision> Aliases = BuildAliases();

    public static IReadOnlyList<FinalDecision> All { get; } = [.. Displays.Keys];

    public static string Display(FinalDecision decision) => Displays[decision];

    public static FinalDecision? TryResolve(string? text)
    {
        var key = TextNormalizer.NormalizeLoose(text);
        return key.Length == 0 ? null : Aliases.TryGetValue(key, out var decision) ? decision : null;
    }

    private static Dictionary<string, FinalDecision> BuildAliases()
    {
        var aliases = new Dictionary<string, FinalDecision>();

        foreach (var (decision, display) in Displays)
        {
            aliases[TextNormalizer.NormalizeLoose(display)] = decision;
            aliases[TextNormalizer.NormalizeLoose(decision.ToString())] = decision;
        }

        // "ESPERA EXÁMEN" only differs by the accent, which NormalizeLoose already strips.
        aliases[TextNormalizer.NormalizeLoose("SIN SGL")] = FinalDecision.SinSgl;
        aliases[TextNormalizer.NormalizeLoose("S SGL")] = FinalDecision.SinSgl;

        return aliases;
    }
}
