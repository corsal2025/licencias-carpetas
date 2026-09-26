using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Persistence;

/// <summary>
/// Sortable columns. A closed set on purpose: the ordering ends up inside the SQL text, so the
/// caller can never hand a column name straight to the query.
/// </summary>
public enum CaseSort
{
    CitationDate,
    Name,
    Rut,
    Office,
    FolderUploadedDate,
    LastFolderDate,
    FolderState,
    FinalDecision
}

/// <summary>Every filter the cases screen can apply, in one object so paging and counting agree.</summary>
public sealed record CaseFilter
{
    public Office? Office { get; init; }

    /// <summary>Offices the signed-in user may see (per-office roles). Null = every office; an empty
    /// collection = none. Combined with <see cref="Office"/>, never widened by it.</summary>
    public IReadOnlyCollection<Office>? AllowedOffices { get; init; }

    /// <summary>Citation month (1-12), paired with <see cref="Year"/> when both are set.</summary>
    public int? Month { get; init; }

    public int? Year { get; init; }

    /// <summary>Exact citation day — used by the "marcar asistencia" bulk action on the statistics
    /// screen, which needs one day's agenda regardless of office/state/etc. Wins over
    /// <see cref="Month"/>/<see cref="Year"/> when set (both wouldn't normally be combined).</summary>
    public DateOnly? CitationDay { get; init; }

    public FolderState? FolderState { get; init; }

    public FinalDecision? FinalDecision { get; init; }

    public FolderSector? Sector { get; init; }

    public bool OnlyNeedsReview { get; init; }

    /// <summary>Only rows whose última carpeta cell holds a comuna — the folder lives elsewhere.</summary>
    public bool OnlyOtherComuna { get; init; }

    /// <summary>Only pending cases whose citation date is older than 15 days.</summary>
    public bool OnlyOverdue { get; init; }

    /// <summary>Free text matched against full name and RUT.</summary>
    public string? Search { get; init; }

    /// <summary>Column the operator clicked. Citation date is the agenda's own order.</summary>
    public CaseSort Sort { get; init; } = CaseSort.CitationDate;

    /// <summary>Dates default to newest first, text to A-Z; this flips whichever applies.</summary>
    public bool Descending { get; init; }
}
