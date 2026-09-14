namespace LicenciasCarpetas.Domain;

/// <summary>
/// "ESTADO DE LA CARPETA" — the workbook's own dropdown catalog (see the PLANTILLA MODELO sheets),
/// plus the values that were typed straight into cells over the year.
/// </summary>
public enum FolderState
{
    PrimeraLicencia,
    SubidaAConaset,
    SubidaConF8,
    SubidaConOficio,
    CambioDomicilioSubidoAConaset,
    CambioDomicilioSubidoConCorreo,
    CambioDomicilioSolicitado,
    CambioDomicilio,
    CanjeLicenciaExtranjera,
    SeEncuentraEnArchivos,
    SeEncuentraEnOficina43,
    NoExisteCarpeta,
    CrearOficio,
    CrearCertificado,
    PendienteF8,
    PendienteCertificado
}

public static class FolderStateCatalog
{
    private static readonly Dictionary<FolderState, string> Displays = new()
    {
        [FolderState.PrimeraLicencia] = "1° LICENCIA",
        [FolderState.SubidaAConaset] = "SUBIDA A CONASET",
        [FolderState.SubidaConF8] = "SUBIDA CON F8",
        [FolderState.SubidaConOficio] = "SUBIDA CON CERTIFICADO",
        [FolderState.CambioDomicilioSubidoAConaset] = "CAMBIO DOM. SUBIDO A CONASET",
        [FolderState.CambioDomicilioSubidoConCorreo] = "CAMBIO DOM. SUBIDO CON CORREO",
        [FolderState.CambioDomicilioSolicitado] = "CAMBIO DE DOMICILIO SOLICITADO",
        [FolderState.CambioDomicilio] = "CAMBIO DE DOMICILIO",
        [FolderState.CanjeLicenciaExtranjera] = "CANJE LIC. EXTRANJERA",
        [FolderState.SeEncuentraEnArchivos] = "SE ENCUENTRA EN ARCHIVOS",
        [FolderState.SeEncuentraEnOficina43] = "SE ENCUENTRA EN OF. 43",
        [FolderState.NoExisteCarpeta] = "NO EXISTE CARPETA",
        [FolderState.CrearOficio] = "CREAR OFICIO",
        [FolderState.CrearCertificado] = "CREAR CERTIFICADO",
        [FolderState.PendienteF8] = "PENDIENTE F8",
        [FolderState.PendienteCertificado] = "PENDIENTE CERTIFICADO"
    };

    /// <summary>Loose-normalized spellings found in the 2026 workbook, mapped to their canonical value.</summary>
    private static readonly Dictionary<string, FolderState> Aliases = BuildAliases();

    /// <summary>
    /// Row colours taken from the workbook's own conditional formatting: the operator reads the
    /// agenda by colour before reading any text, so these are copied exactly rather than redesigned.
    /// "CANJE LIC. EXTRANJERA" has no rule in the workbook and therefore has no colour here either.
    /// </summary>
    private static readonly Dictionary<FolderState, string> Colors = new()
    {
        [FolderState.PrimeraLicencia] = "#FF00FF",
        // El libro lo pintaba amarillo; el operador lo cambió a azul, porque una carpeta ya subida
        // a Conaset es trabajo terminado y debe distinguirse de un vistazo del resto.
        [FolderState.SubidaAConaset] = "#1155CC",
        [FolderState.SubidaConF8] = "#BF9000",
        [FolderState.SubidaConOficio] = "#FFE599",
        [FolderState.CambioDomicilioSubidoAConaset] = "#00FFFF",
        [FolderState.CambioDomicilioSubidoConCorreo] = "#D0E0E3",
        [FolderState.CambioDomicilioSolicitado] = "#9FC5E8",
        [FolderState.CambioDomicilio] = "#3D85C6",
        [FolderState.SeEncuentraEnArchivos] = "#6AA84F",
        [FolderState.SeEncuentraEnOficina43] = "#8E7CC3",
        [FolderState.NoExisteCarpeta] = "#FF0000",
        [FolderState.CrearOficio] = "#C27BA0",
        [FolderState.CrearCertificado] = "#C27BA0",
        [FolderState.PendienteF8] = "#E69138",
        [FolderState.PendienteCertificado] = "#E69138"
    };

    /// <summary>
    /// States retired from the dropdowns at the operator's request. They stay in the catalog on
    /// purpose: cases imported from the 2026 workbook already carry them, and they must keep
    /// displaying, keep their colour and keep being recognised on import — they simply can no
    /// longer be picked for new work.
    /// </summary>
    private static readonly HashSet<FolderState> RetiredStates =
    [
        FolderState.SeEncuentraEnArchivos,
        FolderState.SeEncuentraEnOficina43,
        FolderState.CrearOficio,
        FolderState.PendienteF8,
        FolderState.PendienteCertificado
    ];

    public static IReadOnlyList<FolderState> All { get; } = [.. Displays.Keys];

    /// <summary>What the dropdowns offer: everything except the retired states.</summary>
    public static IReadOnlyList<FolderState> Selectable { get; } =
        [.. Displays.Keys.Where(state => !RetiredStates.Contains(state))];

    public static bool IsRetired(FolderState state) => RetiredStates.Contains(state);

    public static string Display(FolderState state) => Displays[state];

    /// <summary>Row colour for a state, or null when the workbook paints nothing for it.</summary>
    public static string? Color(FolderState state) => Colors.GetValueOrDefault(state);

    /// <summary>CSS class the cases table puts on the row, derived from the enum name.</summary>
    public static string CssClass(FolderState? state)
        => state is null ? string.Empty : $"estado-{state.ToString()!.ToLowerInvariant()}";

    public static FolderState? TryResolve(string? text)
    {
        var key = TextNormalizer.NormalizeLoose(text);
        return key.Length == 0 ? null : Aliases.TryGetValue(key, out var state) ? state : null;
    }

    private static Dictionary<string, FolderState> BuildAliases()
    {
        var aliases = new Dictionary<string, FolderState>();

        foreach (var (state, display) in Displays)
        {
            aliases[TextNormalizer.NormalizeLoose(display)] = state;
            aliases[TextNormalizer.NormalizeLoose(state.ToString())] = state;
        }

        void Add(string spelling, FolderState state) => aliases[TextNormalizer.NormalizeLoose(spelling)] = state;

        Add("1 LICENCIA", FolderState.PrimeraLicencia);
        Add("PRIMERA LICENCIA", FolderState.PrimeraLicencia);
        Add("CAMBIO DE DOM SUBIDO CON CORREO", FolderState.CambioDomicilioSubidoConCorreo);
        Add("CAMBIO DOM SUBIDO CON CORREO", FolderState.CambioDomicilioSubidoConCorreo);
        Add("CAMBIO DE DOMICILIO SUBIDO CON CORREO", FolderState.CambioDomicilioSubidoConCorreo);
        Add("CAMBIO DE DOM SUBIDO A CONASET", FolderState.CambioDomicilioSubidoAConaset);
        Add("CAMBIO DE DOMICILIO SUBIDO A CONASET", FolderState.CambioDomicilioSubidoAConaset);
        Add("CANJE", FolderState.CanjeLicenciaExtranjera);
        Add("CANJE LICENCIA EXTRANJERA", FolderState.CanjeLicenciaExtranjera);
        Add("SE ENCUENTRA EN OF.43", FolderState.SeEncuentraEnOficina43);
        Add("SE ENCUENTRA EN OFICINA 43", FolderState.SeEncuentraEnOficina43);
        Add("SE ENCUENTRA EN ARCHIVO", FolderState.SeEncuentraEnArchivos);
        Add("SUBIDA CON OFICIO", FolderState.SubidaConOficio);

        return aliases;
    }
}
