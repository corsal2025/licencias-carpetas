namespace LicenciasCarpetas.Domain;

public enum FolderSector
{
    /// <summary>Última carpeta before July 2023 — the physical folder is stored in Archivo.</summary>
    Archivo,

    /// <summary>Última carpeta from July 2023 onwards — the physical folder is in Oficina 43.</summary>
    Oficina43
}

public enum CaseAgingAlert
{
    None,
    Normal,
    Warning,
    Overdue
}

public static class FolderSectorCatalog
{
    /// <summary>The office is called "Oficina 43" everywhere in the department — the enum name
    /// without its space is an implementation detail and must not reach the screen.</summary>
    public static string Display(FolderSector? sector) => sector switch
    {
        FolderSector.Archivo => "Archivo",
        FolderSector.Oficina43 => "Oficina 43",
        _ => "—"
    };
}

/// <summary>
/// One row of a monthly agenda sheet: a citizen cited on a given date at a given office, with the
/// state of their physical folder and the final decision of the process.
/// </summary>
public sealed class FolderCase
{
    public long Id { get; set; }

    /// <summary>"FECHA DE LA CITACIÓN" — the appointment date. Identifies the agenda month.</summary>
    public DateOnly? CitationDate { get; set; }

    /// <summary>"FECHA CUANDO SE SUBIO LA CARPETA" — when the folder was uploaded to Conaset.</summary>
    public DateOnly? FolderUploadedDate { get; set; }

    /// <summary>
    /// "FECHA ULTIMA CARPETA" when the cell holds a real date. In the workbook the same cell is
    /// reused to write a comuna name for cambio-de-domicilio cases; that text lands in
    /// <see cref="LastFolderComuna"/> instead, never here.
    /// </summary>
    public DateOnly? LastFolderDate { get; set; }

    /// <summary>Comuna written in the "FECHA ULTIMA CARPETA" cell: the folder lives in another
    /// municipality and has to be requested from it (cambio de domicilio).</summary>
    public string? LastFolderComuna { get; set; }

    /// <summary>Comuna the operator types on the Casos screen when Estado carpeta is "Cambio de
    /// domicilio solicitado", used to send the one-click outbound request. Unrelated to
    /// <see cref="LastFolderComuna"/>, which tracks where a previous folder physically lives.</summary>
    public string? CambioDomicilioComuna { get; set; }

    /// <summary>Date of the folder before the last one. Typed by hand; the workbook has no column
    /// for it, so an import never touches it. It never decides the sector — that is the última.</summary>
    public DateOnly? PenultimateFolderDate { get; set; }

    /// <summary>F8 case code, free text. Also typed by hand and absent from the workbook.</summary>
    public string? CodigoF8 { get; set; }

    /// <summary>Número de folio de la licencia, typed by hand. Absent from the workbook, so an
    /// import never touches it — same reasoning as CodigoF8.</summary>
    public string? FolioLicencia { get; set; }

    /// <summary>Clases de licencia que el contribuyente viene a obtener, separadas por coma
    /// ("B,C"). Selección múltiple: una misma citación puede cubrir varias clases.</summary>
    public string? LicenceClasses { get; set; }

    /// <summary>Nota libre sobre la persona (RUT), para cuando el caso necesita una aclaración que
    /// no cabe en ninguna otra columna. Típicamente vacía — no es parte del Excel.</summary>
    public string? Observations { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Correo y celular, leídos del export de citas cuando ese sistema los trae. El libro
    /// maestro no tiene estas columnas, así que una importación de él nunca las toca.</summary>
    public string? Email { get; set; }
    public string? CellPhone { get; set; }

    /// <summary>"NOMBRE COMPLETO" — what every screen and printed document shows.</summary>
    public string? FullName { get; set; }

    /// <summary>Canonical dotted RUT with a valid check digit, or the raw text when it could not be validated.</summary>
    public string? Rut { get; set; }

    public Office Office { get; set; }

    /// <summary>"ATENCIÓN" column as written by the operator — kept verbatim, it is free text.</summary>
    public string? AttentionNote { get; set; }

    /// <summary>Whether the citizen was actually attended, derived from a non-empty ATENCIÓN cell on import.</summary>
    public bool Attended { get; set; }

    public MoralIdoneity? MoralIdoneity { get; set; }

    public FolderState? FolderState { get; set; }

    /// <summary>Original ESTADO DE LA CARPETA text when it matched no catalog value — nothing is discarded on import.</summary>
    public string? FolderStateRaw { get; set; }

    public FinalDecision? FinalDecision { get; set; }

    /// <summary>Original DECISIÓN FINAL text when it matched no catalog value.</summary>
    public string? FinalDecisionRaw { get; set; }

    /// <summary>Momento (UTC) en que <see cref="FinalDecision"/> pasó a Otorgado o Denegado. Se
    /// limpia a null si la decisión cambia a cualquier otro valor; no se toca si la decisión no
    /// cambia. Es la fuente de la métrica "días citación → decisión" del KPI por sede.</summary>
    public DateTimeOffset? FinalDecisionAt { get; set; }

    /// <summary>Sheet the row came from ("MAYO AV. ARGENTINA"), so any imported row can be traced back.</summary>
    public string? SourceSheet { get; set; }

    /// <summary>1-based row number inside <see cref="SourceSheet"/>.</summary>
    public int SourceRow { get; set; }

    /// <summary>Set on import when the row is incomplete or unreadable: invalid RUT, no name, or a
    /// state/decision outside the catalog. Drives the "Requiere revisión" filter.</summary>
    public bool NeedsReview { get; set; }

    /// <summary>Operator-only bookkeeping checkbox — no effect on any other rule.</summary>
    public bool Marked { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Usuario que editó el caso por última vez desde el dashboard. Null si nadie lo ha
    /// tocado: una importación del libro no cuenta como autoría de nadie.</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>When the operator sent the case to the bin. Null for a live case. A case in the bin
    /// is invisible everywhere except /Papelera, and a re-import updates it without reviving it.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>When this case was last included in a printed sector list. Once set, it drops off
    /// the next list so the same physical folder is not requested twice; "volver a pedir" clears it.</summary>
    public DateTimeOffset? SectorPrintedAt { get; set; }

    /// <summary>Where the physical folder is filed, derived from the última-carpeta date. Null while
    /// there is no date (including cambio-de-domicilio rows, whose folder is in another comuna).</summary>
    public FolderSector? Sector => LastFolderDate is { } fecha
        ? fecha < new DateOnly(2023, 7, 1) ? FolderSector.Archivo : FolderSector.Oficina43
        : null;

    /// <summary>True when the folder was already uploaded to Conaset, whichever the upload route was.</summary>
    public bool IsUploaded => FolderState is Domain.FolderState.SubidaAConaset
        or Domain.FolderState.SubidaConF8
        or Domain.FolderState.SubidaConOficio
        or Domain.FolderState.CambioDomicilioSubidoAConaset
        or Domain.FolderState.CambioDomicilioSubidoConCorreo;

    /// <summary>Display text for the folder state, catalog value or raw leftover.</summary>
    public string FolderStateText => FolderState is { } state
        ? FolderStateCatalog.Display(state)
        : FolderStateRaw ?? string.Empty;

    public string FinalDecisionText => FinalDecision is { } decision
        ? FinalDecisionCatalog.Display(decision)
        : FinalDecisionRaw ?? string.Empty;

    /// <summary>
    /// Days elapsed since citation date for active tracking.
    /// </summary>
    public int? DaysSinceCitation => CitationDate is { } date
        ? Math.Max(0, (DateOnly.FromDateTime(DateTime.Today).DayNumber - date.DayNumber))
        : null;

    /// <summary>
    /// SLA aging alert: Green (&lt; 7d), Yellow (7-14d), Red (&gt;= 15d) for pending cases.
    /// </summary>
    public CaseAgingAlert AgingAlert
    {
        get
        {
            return AgingFor(FolderState, CitationDate, DateOnly.FromDateTime(DateTime.Today));
        }
    }

    /// <summary>The SLA rule behind <see cref="AgingAlert"/>, with "today" passed in so reports
    /// (and their tests) can evaluate it for any date.</summary>
    public static CaseAgingAlert AgingFor(FolderState? state, DateOnly? citationDate, DateOnly today)
    {
        var probe = new FolderCase { FolderState = state };
        if (probe.IsUploaded || state is Domain.FolderState.NoExisteCarpeta || state is Domain.FolderState.PrimeraLicencia)
        {
            return CaseAgingAlert.None;
        }

        if (citationDate is not { } date) return CaseAgingAlert.None;
        var days = Math.Max(0, today.DayNumber - date.DayNumber);
        if (days >= 15) return CaseAgingAlert.Overdue;
        if (days >= 7) return CaseAgingAlert.Warning;
        return CaseAgingAlert.Normal;
    }
}
