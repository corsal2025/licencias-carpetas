using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Solicitar;

public sealed class FakeOutboundAddressChangeRequestRepository : IOutboundAddressChangeRequestRepository
{
    private readonly List<OutboundAddressChangeRequest> _requests = [];
    private readonly List<OutboundAddressChangeAttachment> _attachments = [];
    private long _nextRequestId = 1;
    private long _nextAttachmentId = 1;
    private readonly HashSet<long> _deletedSourceCases = [];

    public void EnsureSchema() { }

    public long Insert(OutboundAddressChangeRequest request)
    {
        request.Id = _nextRequestId++;
        request.Status = OutboundRequestStatus.Borrador;
        _requests.Add(request);
        return request.Id;
    }

    public void Update(OutboundAddressChangeRequest request)
    {
        var existing = FindById(request.Id);
        if (existing is null) return;
        existing.FullName = request.FullName;
        existing.Rut = request.Rut;
        existing.Phone = request.Phone;
        existing.Street = request.Street;
        existing.Number = request.Number;
        existing.Unit = request.Unit;
        existing.DestinationComuna = request.DestinationComuna;
        existing.WorkflowState = request.WorkflowState;
        existing.UploadedAt = request.UploadedAt;
    }

    public OutboundAddressChangeRequest? FindById(long id) => _requests.FirstOrDefault(r => r.Id == id);

    public IReadOnlyList<OutboundAddressChangeRequest> GetAll() =>
        _requests.OrderByDescending(r => r.Id).ToList();

    public IReadOnlyList<OutboundAddressChangeRequest> FindBySourceFolderCaseId(long folderCaseId) =>
        _requests.Where(r => r.SourceFolderCaseId == folderCaseId).OrderByDescending(r => r.Id).ToList();

    public bool MarkSent(long id, DateTimeOffset sentAt, long sentByUserId)
    {
        var request = FindById(id);
        if (request is not { Status: OutboundRequestStatus.Borrador }) return false;
        request.Status = OutboundRequestStatus.Enviada;
        request.SentAt = sentAt;
        request.SentByUserId = sentByUserId;
        return true;
    }

    public bool MarkUploaded(long id, DateTimeOffset uploadedAt)
    {
        var request = FindById(id);
        if (request is null) return false;
        request.UploadedAt = uploadedAt;
        return true;
    }

    public void Delete(long id) => _requests.RemoveAll(r => r.Id == id);

    public long AddAttachment(OutboundAddressChangeAttachment attachment)
    {
        attachment.Id = _nextAttachmentId++;
        _attachments.Add(attachment);
        return attachment.Id;
    }

    public IReadOnlyList<OutboundAddressChangeAttachment> GetAttachments(long requestId) =>
        _attachments.Where(a => a.RequestId == requestId).OrderBy(a => a.Id).ToList();

    public void DeleteAttachment(long attachmentId) => _attachments.RemoveAll(a => a.Id == attachmentId);

    public void RecordDeletedSourceFolderCase(long sourceFolderCaseId) => _deletedSourceCases.Add(sourceFolderCaseId);

    public bool IsSourceFolderCaseDeleted(long sourceFolderCaseId) => _deletedSourceCases.Contains(sourceFolderCaseId);

    public void ClearDeletedSourceFolderCase(long sourceFolderCaseId) => _deletedSourceCases.Remove(sourceFolderCaseId);
}
