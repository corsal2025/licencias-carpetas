namespace LicenciasCarpetas.Certificados.Domain;

public sealed class CertificadoRequest
{
    public long Id { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Rut { get; set; } = string.Empty;
    public string Comuna { get; set; } = string.Empty;
    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaPeticion { get; set; }
    public DateOnly? FechaEmision { get; set; }
    public string Estado { get; set; } = "PENDIENTE";
    public string EstadoActual { get; set; } = "PENDIENTE";
    public string? Folio { get; set; }
    public string? Direccion { get; set; }
    public bool Marked { get; set; }
    public bool PendienteCarpeta { get; set; }
    public string Origin { get; set; } = "Solicitar";
    public long? SourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
