using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.Persistence;

/// <summary>A case of an office outside the user's scope was asked for by Id. Surfaces as 404, so
/// the response does not reveal that the case exists in another office.</summary>
public sealed class CaseOutOfScopeException(long caseId)
    : Exception($"El caso {caseId} no existe o no pertenece a sus sedes.");

/// <summary>
/// The case repository as seen by one signed-in user: the only one screens may inject. Marker
/// interface so the DI container can hand pages a per-request instance while importers and
/// background services keep the unrestricted singleton <see cref="IFolderCaseRepository"/>.
/// </summary>
public interface IScopedCaseRepository : IFolderCaseRepository
{
    OfficeScope Scope { get; }
}

/// <summary>
/// Per-request decorator that applies the user's <see cref="OfficeScope"/> to every read and
/// refuses every mutation by Id on a case outside it. One choke point instead of a check in each
/// handler, which is how "an endpoint someone forgot" leaks another office's cases.
/// </summary>
public sealed class ScopedCaseRepository(IFolderCaseRepository inner, OfficeScope scope) : IScopedCaseRepository
{
    public OfficeScope Scope => scope;

    private IReadOnlyCollection<Office>? Allowed => scope.AllowedOffices;

    private CaseFilter Scoped(CaseFilter filter) => filter with { AllowedOffices = Allowed };

    /// <summary>Throws unless the case exists (live or in the bin) and belongs to the scope.</summary>
    private void Guard(long id)
    {
        if (scope.IsUnrestricted)
        {
            return;
        }

        if (inner.FindById(id) is not { } found || !scope.Allows(found.Office))
        {
            throw new CaseOutOfScopeException(id);
        }
    }

    private void GuardUnrestricted()
    {
        if (!scope.IsUnrestricted)
        {
            throw new CaseOutOfScopeException(0);
        }
    }

    public void EnsureSchema() => inner.EnsureSchema();

    public UpsertOutcome Upsert(FolderCase folderCase)
    {
        if (!scope.Allows(folderCase.Office)) throw new CaseOutOfScopeException(folderCase.Id);
        return inner.Upsert(folderCase);
    }

    public IReadOnlyList<UpsertOutcome> UpsertMany(IReadOnlyList<FolderCase> folderCases)
    {
        if (folderCases.Any(c => !scope.Allows(c.Office))) throw new CaseOutOfScopeException(0);
        return inner.UpsertMany(folderCases);
    }

    public long Insert(FolderCase folderCase)
    {
        if (!scope.Allows(folderCase.Office)) throw new CaseOutOfScopeException(0);
        return inner.Insert(folderCase);
    }

    public FolderCase? FindById(long id) =>
        inner.FindById(id) is { } found && scope.Allows(found.Office) ? found : null;

    public IReadOnlyList<FolderCase> Query(CaseFilter filter, int skip, int take) => inner.Query(Scoped(filter), skip, take);

    public IReadOnlyList<FolderCase> QueryAll(CaseFilter filter) => inner.QueryAll(Scoped(filter));

    public int Count(CaseFilter filter) => inner.Count(Scoped(filter));

    public IReadOnlyList<string> DuplicateRuts(CaseFilter filter) => inner.DuplicateRuts(Scoped(filter));

    public int CountNeedingReview(IReadOnlyCollection<Office>? allowedOffices = null) => inner.CountNeedingReview(Allowed);

    public IReadOnlyList<int> DistinctYears() => inner.DistinctYears();

    public IReadOnlyList<FolderCase> ForSector(FolderSector sector, bool onlyMarked, bool includePrinted = false,
        DateOnly? citationDay = null, int? year = null, int? month = null, IReadOnlyCollection<Office>? allowedOffices = null)
        => inner.ForSector(sector, onlyMarked, includePrinted, citationDay, year, month, Allowed);

    public void MarkSectorPrinted(IReadOnlyCollection<long> ids) =>
        inner.MarkSectorPrinted(scope.IsUnrestricted ? ids : [.. ids.Where(id => FindById(id) is not null)]);

    public void ClearSectorPrinted(long id)
    {
        Guard(id);
        inner.ClearSectorPrinted(id);
    }

    public int DeleteAllPermanently()
    {
        GuardUnrestricted();
        return inner.DeleteAllPermanently();
    }

    public IReadOnlyList<(DateOnly Date, Office Office, int Scheduled, int Attended)> DailyAttendance(int year, int month, IReadOnlyCollection<Office>? allowedOffices = null)
        => inner.DailyAttendance(year, month, Allowed);

    public IReadOnlyList<(FolderState? State, int Count)> FolderStateBreakdown(int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices = null)
        => inner.FolderStateBreakdown(year, month, office, Allowed);

    public IReadOnlyList<(FinalDecision? Decision, int Count)> FinalDecisionBreakdown(int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices = null)
        => inner.FinalDecisionBreakdown(year, month, office, Allowed);

    public IReadOnlyList<(LicenceClass Licence, int Count)> LicenceClassBreakdown(int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices = null)
        => inner.LicenceClassBreakdown(year, month, office, Allowed);

    public void UpdateEditableFields(long id, string? fullName, string? rut, DateOnly? citationDate,
        DateOnly? folderUploadedDate, DateOnly? lastFolderDate, string? lastFolderComuna,
        FolderState? folderState, FinalDecision? finalDecision, MoralIdoneity? moralIdoneity,
        string? attentionNote, bool needsReview, string? editedBy = null, string? cambioDomicilioComuna = null)
    {
        Guard(id);
        inner.UpdateEditableFields(id, fullName, rut, citationDate, folderUploadedDate, lastFolderDate, lastFolderComuna,
            folderState, finalDecision, moralIdoneity, attentionNote, needsReview, editedBy, cambioDomicilioComuna);
    }

    public void SetMarked(long id, bool marked)
    {
        Guard(id);
        inner.SetMarked(id, marked);
    }

    public void SetAttended(long id, bool attended, string? editedBy = null)
    {
        Guard(id);
        inner.SetAttended(id, attended, editedBy);
    }

    public void UpdateCaseDetails(long id, string? codigoF8, DateOnly? penultimateFolderDate, string? licenceClasses = null, string? folioLicencia = null, string? editedBy = null)
    {
        Guard(id);
        inner.UpdateCaseDetails(id, codigoF8, penultimateFolderDate, licenceClasses, folioLicencia, editedBy);
    }

    public void UpdateObservations(long id, string? observations, string? editedBy = null)
    {
        Guard(id);
        inner.UpdateObservations(id, observations, editedBy);
    }

    public IReadOnlyList<CaseAuditEntry> GetAuditLog(long caseId)
    {
        Guard(caseId);
        return inner.GetAuditLog(caseId);
    }

    public IReadOnlyList<CaseAuditEntry> GetAuditLogsForPeriod(DateTimeOffset start, DateTimeOffset end, IReadOnlyCollection<Office>? allowedOffices = null)
        => inner.GetAuditLogsForPeriod(start, end, Allowed);

    public void Delete(long id)
    {
        Guard(id);
        inner.Delete(id);
    }

    public void Restore(long id)
    {
        Guard(id);
        inner.Restore(id);
    }

    public void DeletePermanently(long id)
    {
        Guard(id);
        inner.DeletePermanently(id);
    }

    public IReadOnlyList<FolderCase> Deleted(IReadOnlyCollection<Office>? allowedOffices = null) => inner.Deleted(Allowed);

    public int CountDeleted(IReadOnlyCollection<Office>? allowedOffices = null) => inner.CountDeleted(Allowed);
}
