using LicenciasCarpetas.Certificados.Domain;

namespace LicenciasCarpetas.Certificados.Data;

public interface ICertificadoRequestRepository
{
    void EnsureSchema();
    IReadOnlyList<CertificadoRequest> GetAll();
    CertificadoRequest? FindById(long id);
    CertificadoRequest? FindByRut(string rut);
    long Insert(CertificadoRequest request);
    void Update(CertificadoRequest request);
    void SetMarked(long id, bool marked);
    void SetPendienteCarpeta(long id, bool pendienteCarpeta);
    void SetEstado(long id, string estado);
    void SetEmitido(long id, DateOnly fechaEmision, string? folio);
    void Delete(long id);
}
