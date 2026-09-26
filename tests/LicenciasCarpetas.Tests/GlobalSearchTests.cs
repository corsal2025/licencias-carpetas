using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8.Domain;
using LicenciasCarpetas.Persistence;
using Xunit;

namespace LicenciasCarpetas.Tests;

public class GlobalSearchTests
{
    [Fact]
    public void Finds_citizens_across_cases_and_f8_modules()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(new FolderCase
        {
            FullName = "CARLOS SANTANA GOMEZ",
            Rut = "12.345.678-9",
            CitationDate = new DateOnly(2026, 3, 15),
            Office = Office.AvenidaArgentina,
            FolderState = FolderState.SubidaAConaset
        });

        var f8Repo = new LicenciasCarpetas.F8.Data.UrgentRequestRepository(db.ConnectionString);
        f8Repo.EnsureSchema();
        f8Repo.Insert(new UrgentRequest
        {
            NombreCompleto = "CARLOS SANTANA GOMEZ",
            Rut = "123456789",
            FechaPeticion = new DateOnly(2026, 3, 10),
            Estado = "En proceso",
            Origin = "Manual"
        });

        var searchService = new GlobalSearchService(db.ConnectionString);

        // Search by RUT without dots
        var results = searchService.Search("123456789");
        Assert.True(results.Count >= 2);
        Assert.Contains(results, r => r.Module == "Gestión de Licencias" && r.Title == "CARLOS SANTANA GOMEZ");
        Assert.Contains(results, r => r.Module == "F8 Urgentes" && r.Title == "CARLOS SANTANA GOMEZ");

        // Search by partial name
        var nameResults = searchService.Search("Santana");
        Assert.True(nameResults.Count >= 2);
    }

    [Fact]
    public void Finds_address_change_and_certificate_requests()
    {
        using var db = new SqliteTestDatabase();
        new LicenciasCarpetas.CambioDomicilio.Data.CambioDomicilioRequestRepository(db.ConnectionString).EnsureSchema();
        new LicenciasCarpetas.CambioDomicilio.Data.OutboundAddressChangeRequestRepository(db.ConnectionString).EnsureSchema();
        var certificados = new LicenciasCarpetas.Certificados.Data.CertificadoRequestRepository(db.ConnectionString);
        certificados.EnsureSchema();
        certificados.Insert(new LicenciasCarpetas.Certificados.Domain.CertificadoRequest
        {
            NombreCompleto = "ANA ROJAS", Rut = "12.345.678-5", Comuna = "LIMACHE",
            FechaIngreso = new DateOnly(2026, 2, 1), CreatedAt = DateTimeOffset.UtcNow
        });
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO PersonRequest (FullName, Rut, Comuna, SourceMessageId, SourceSubject, SourceSender, NeedsReview, Status, ReceivedAt, CreatedAt)
                VALUES ('ANA ROJAS', '12.345.678-5', 'QUILPUÉ', 'm1', 's', 'x@y', 0, 'Pendiente', '2026-02-05', '2026-02-05');
                INSERT INTO OutboundAddressChangeRequest (FullName, Rut, DestinationComuna, Status, CreatedAt, CreatedByUserId)
                VALUES ('ANA ROJAS', '12345678-5', 'LIMACHE', 'Enviada', '2026-02-06', 1);
                """;
            command.ExecuteNonQuery();
        }

        var results = new GlobalSearchService(db.ConnectionString).Search("12345678");

        Assert.Contains(results, r => r.Module == "Cambio Domicilio (Recibida)");
        Assert.Contains(results, r => r.Module == "Cambio Domicilio (Solicitada)");
        Assert.Contains(results, r => r.Module == "Certificados");
    }

    [Fact]
    public void Returns_empty_list_for_empty_query()
    {
        using var db = new SqliteTestDatabase();
        var searchService = new GlobalSearchService(db.ConnectionString);

        var results = searchService.Search("   ");
        Assert.Empty(results);
    }
}
