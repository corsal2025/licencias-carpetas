using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Routing;

/// <summary>In-memory fake covering only what AddressChangeRoutingService/CambioDomicilioSyncService
/// actually call — a full SQLite-backed exercise already happens in
/// CambioDomicilioRequestRepositoryTests, this one is about orchestration, not persistence.</summary>
public sealed class FakeCambioDomicilioRequestRepository : ICambioDomicilioRequestRepository
{
    private readonly List<PersonRequest> _requests = [];
    private readonly HashSet<string> _deletedSourceMessages = [];
    private long _nextId = 1;

    public void EnsureSchema() { }

    public bool ExistsBySourceMessageId(string sourceMessageId) =>
        _requests.Any(r => r.SourceMessageId == sourceMessageId);

    public PersonRequest? FindByRutAndComuna(string rut, string comuna) =>
        _requests.LastOrDefault(r => r.Rut == rut && r.Comuna == comuna);

    public PersonRequest? FindByFullNameAndComuna(string fullName, string comuna) =>
        _requests.LastOrDefault(r => string.Equals(r.FullName, fullName, StringComparison.OrdinalIgnoreCase) && r.Comuna == comuna);

    public PersonRequest? FindPendingBySourceMessageId(string sourceMessageId) =>
        _requests.FirstOrDefault(r => r.SourceMessageId == sourceMessageId && r.Status == RequestStatus.Pending);

    public PersonRequest? FindById(long id) => _requests.FirstOrDefault(r => r.Id == id);

    public long Insert(PersonRequest request)
    {
        request.Id = _nextId++;
        _requests.Add(request);
        return request.Id;
    }

    public void MarkUploaded(long id, DateTimeOffset uploadedAt)
    {
        var request = FindById(id);
        if (request is { Status: RequestStatus.Pending })
        {
            request.Status = RequestStatus.Uploaded;
            request.UploadedAt = uploadedAt;
        }
    }

    public void SetFechaUltimaCarpeta(long id, DateOnly fecha)
    {
        var request = FindById(id);
        if (request is null) return;
        request.FechaUltimaCarpeta = fecha;
        request.SinCarpeta = false;
    }

    public void ClearFechaUltimaCarpeta(long id)
    {
        var request = FindById(id);
        if (request is null) return;
        request.FechaUltimaCarpeta = null;
        request.SinCarpeta = false;
    }

    public void SetSinCarpeta(long id)
    {
        var request = FindById(id);
        if (request is null) return;
        request.FechaUltimaCarpeta = null;
        request.SinCarpeta = true;
    }

    public void SetPersonData(long id, string fullName, string normalizedRut)
    {
        var request = FindById(id);
        if (request is null) return;
        request.FullName = fullName;
        request.Rut = normalizedRut;
        request.NeedsReview = false;
    }

    public void SetMarked(long id, bool marked)
    {
        var request = FindById(id);
        if (request is null) return;
        request.Marked = marked;
        request.MarkedAt = marked ? DateTimeOffset.UtcNow : null;
    }

    public void SetFolderNotFound(long id, bool folderNotFound)
    {
        var request = FindById(id);
        if (request is not null) request.FolderNotFound = folderNotFound;
    }

    public void SetPendienteCarpeta(long id, bool pendienteCarpeta)
    {
        var request = FindById(id);
        if (request is not null) request.PendienteCarpeta = pendienteCarpeta;
    }

    public void SetCodigoF8(long id, string? codigoF8)
    {
        var request = FindById(id);
        if (request is not null) request.CodigoF8 = codigoF8;
    }

    public void SetDestination(long id, CaseDestination destination, DateTimeOffset transferredAt)
    {
        var request = FindById(id);
        if (request is null) return;
        request.Destination = destination;
        request.TransferredAt = transferredAt;
    }

    public void ClearDestination(long id)
    {
        var request = FindById(id);
        if (request is null) return;
        request.Destination = CaseDestination.None;
        request.TransferredAt = null;
    }

    public void SetCertificadoNotified(long id, DateTimeOffset notifiedAt)
    {
        var request = FindById(id);
        if (request is not null) request.CertificadoNotifiedAt = notifiedAt;
    }

    public void UpdateStatusToConfirmed(long id, DateTimeOffset confirmedAt, long confirmedByUserId)
    {
        var request = FindById(id);
        if (request is null) return;
        request.Status = RequestStatus.Confirmed;
        request.ConfirmedAt = confirmedAt;
        request.ConfirmedByUserId = confirmedByUserId;
    }

    public int RevertUploadedBySourceMessageId(string sourceMessageId)
    {
        var affected = _requests.Where(r => r.SourceMessageId == sourceMessageId && r.Status == RequestStatus.Uploaded).ToList();
        foreach (var request in affected)
        {
            request.Status = RequestStatus.Pending;
            request.UploadedAt = null;
        }
        return affected.Count;
    }

    public void RevertConfirmedToPending(long id)
    {
        var request = FindById(id);
        if (request is not { Status: RequestStatus.Confirmed }) return;
        request.Status = RequestStatus.Pending;
        request.UploadedAt = null;
        request.ConfirmedAt = null;
        request.ConfirmedByUserId = null;
    }

    public void SetSectorPdfGenerated(long id, DateTimeOffset generatedAt)
    {
        var request = FindById(id);
        if (request is not null) request.SectorPdfGeneratedAt = generatedAt;
    }

    public void SetPenultimasCarpetasPdfGenerated(long id, DateTimeOffset generatedAt)
    {
        var request = FindById(id);
        if (request is not null) request.PenultimasCarpetasPdfGeneratedAt = generatedAt;
    }

    public void Delete(long id) => _requests.RemoveAll(r => r.Id == id);

    public void RecordDeletedSourceMessage(string sourceMessageId) => _deletedSourceMessages.Add(sourceMessageId);

    public bool IsSourceMessageDeleted(string sourceMessageId) => _deletedSourceMessages.Contains(sourceMessageId);

    public void ClearDeletedSourceMessage(string sourceMessageId) => _deletedSourceMessages.Remove(sourceMessageId);

    public IReadOnlyList<PersonRequest> GetAll() => _requests;
}
