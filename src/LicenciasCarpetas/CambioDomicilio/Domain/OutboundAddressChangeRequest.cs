using LicenciasCarpetas.Domain;

namespace LicenciasCarpetas.CambioDomicilio.Domain;

public enum OutboundRequestStatus
{
    Borrador,
    Enviada
}

/// <summary>A request Valparaíso sends to another comuna asking it to confirm/return a
/// contributor's folder after an address change — the mirror direction of PersonRequest, which
/// tracks requests other comunas send TO Valparaíso.</summary>
public sealed class OutboundAddressChangeRequest
{
    public long Id { get; set; }
    public required string FullName { get; set; }
    public required string Rut { get; set; }
    public string? Phone { get; set; }
    public string? Street { get; set; }
    public string? Number { get; set; }
    public string? Unit { get; set; }
    public required string DestinationComuna { get; set; }
    public OutboundRequestStatus Status { get; set; } = OutboundRequestStatus.Borrador;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
    public long? SentByUserId { get; set; }
    public long CreatedByUserId { get; set; }

    /// <summary>The FolderCase this request was created from via the one-click Casos "Solicitar"
    /// button, if any — lets that flow detect a request already exists for the case and refuse to
    /// create (and email) a duplicate. Null for requests started from Solicitar Cambios de
    /// Domicilio directly, which has no originating case to correlate.</summary>
    public long? SourceFolderCaseId { get; set; }

    /// <summary>Dónde va quedando la carpeta física en el flujo de Conaset — un subconjunto de
    /// FolderState.Selectable, no todos los valores (ver WorkflowStateCatalog en Index.cshtml.cs).
    /// Distinto de Status: Status dice si el CORREO de la solicitud se mandó o no; esto dice en qué
    /// paso está la carpeta una vez que la otra comuna responde. Cuando SourceFolderCaseId no es
    /// null, cambiar este valor también actualiza el FolderState del FolderCase de origen — así el
    /// operador no tiene que ir a Casos a repetir el mismo cambio dos veces.</summary>
    public FolderState? WorkflowState { get; set; }
}

/// <summary>One file attached to an OutboundAddressChangeRequest — stored on disk under
/// CarpetasOptions.UploadDirectory, only the metadata lives in SQLite.</summary>
public sealed class OutboundAddressChangeAttachment
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public required string FileName { get; set; }
    public required string StoredPath { get; set; }
    public required string ContentType { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}
