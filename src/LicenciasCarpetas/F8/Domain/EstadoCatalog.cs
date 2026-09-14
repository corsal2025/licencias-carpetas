namespace LicenciasCarpetas.F8.Domain;

/// <summary>
/// Known ESTADO / ESTADO ACTUAL values observed in the historical workbook.
/// Both fields are stored as canonicalized open strings (not enums) because
/// operators keep introducing new values; this catalog only powers dropdown
/// options and "is this a plausible known value" flagging, never rejection.
/// </summary>
public static class EstadoCatalog
{
    // Extracted from the source workbook (URGENTES DIARIOS.xlsx, all monthly sheets) — every
    // distinct value that actually appears in the ESTADO / ESTADO ACTUAL columns, ordered by
    // frequency descending.
    public static readonly IReadOnlyCollection<string> KnownEstados = new[]
    {
        "SUBIR CON F8",
        "CAMBIO DE DOMICILIO",
        "CREAR CERTIFICADO",
        "CARPETA SUBIDA",
        "PRIMERA LICENCIA",
        "PENDIENTE",
        "DENEGADA",
    };

    // No blank entry here on purpose — blank/null EstadoActual is treated as equivalent to the
    // literal "PENDIENTE" value everywhere it's displayed, so the dropdown doesn't show two
    // entries that both read "Pendiente".
    public static readonly IReadOnlyCollection<string> KnownEstadosActuales = new[]
    {
        "PENDIENTE",
        "SUBIDA A CONASET",
    };

    public static string Canonicalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var upper = value.Trim().ToUpperInvariant();
        if (upper is "SUBIDO" or "SUBIDA" or "SUBIDO A CONASET")
        {
            return "SUBIDA A CONASET";
        }
        return upper;
    }

    public static string? NormalizeForPersistence(string? value, bool isEstado)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return isEstado
            ? IsKnownEstado(trimmed) ? Canonicalize(trimmed) : trimmed
            : IsKnownEstadoActual(trimmed) ? Canonicalize(trimmed) : trimmed;
    }

    public static bool IsKnownEstado(string? value) => KnownEstados.Contains(Canonicalize(value));

    public static bool IsKnownEstadoActual(string? value) =>
        Canonicalize(value) is "" || KnownEstadosActuales.Contains(Canonicalize(value));

    public static bool IsUnknownEstado(string? value) => !string.IsNullOrWhiteSpace(value) && !IsKnownEstado(value);

    public static bool IsUnknownEstadoActual(string? value) => !string.IsNullOrWhiteSpace(value) && !IsKnownEstadoActual(value);
}
