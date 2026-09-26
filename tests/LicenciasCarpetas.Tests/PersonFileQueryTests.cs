using LicenciasCarpetas.Certificados.Data;
using LicenciasCarpetas.Certificados.Domain;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.F8.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Tests;

/// <summary>Change "sgl-ficha-persona": one read-only view of everything recorded for a RUT.</summary>
public class PersonFileQueryTests
{
    [Theory]
    [InlineData("9876543-3")]
    [InlineData("9.876.543-3")]
    [InlineData("09.876.543-3")]
    [InlineData("098765433")]
    public void Finds_the_case_whatever_the_rut_format(string stored)
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(Case(stored, Office.AvenidaArgentina, new DateOnly(2026, 3, 1)));

        var file = new PersonFileQuery(db.ConnectionString).Load("09.876.543-3", PersonFileScope.All);

        Assert.Single(file.Cases);
    }

    [Fact]
    public void A_k_check_digit_matches_regardless_of_case()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(Case("10.000.013-k", Office.Placilla, new DateOnly(2026, 3, 1)));

        var file = new PersonFileQuery(db.ConnectionString).Load("10.000.013-K", PersonFileScope.All);

        Assert.Single(file.Cases);
    }

    [Fact]
    public void Lists_cases_of_every_office_newest_first_and_skips_the_trash()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(Case("12.345.678-5", Office.AvenidaArgentina, new DateOnly(2026, 1, 10)));
        db.Cases.Insert(Case("12.345.678-5", Office.Placilla, new DateOnly(2026, 5, 10)));
        var trashed = db.Cases.Insert(Case("12.345.678-5", Office.MercadoPuerto, new DateOnly(2026, 6, 10)));
        db.Cases.Insert(Case("11.111.111-1", Office.Placilla, new DateOnly(2026, 5, 10)));
        db.Cases.Delete(trashed);

        var file = new PersonFileQuery(db.ConnectionString).Load("12.345.678-5", PersonFileScope.All);

        Assert.Equal([Office.Placilla, Office.AvenidaArgentina], file.Cases.Select(c => c.Office));
        Assert.Equal("JUAN PEREZ", file.FullName);
    }

    [Fact]
    public void A_restricted_scope_only_shows_its_offices()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(Case("12.345.678-5", Office.AvenidaArgentina, new DateOnly(2026, 1, 10)));
        db.Cases.Insert(Case("12.345.678-5", Office.Placilla, new DateOnly(2026, 5, 10)));

        var file = new PersonFileQuery(db.ConnectionString)
            .Load("12.345.678-5", new PersonFileScope([Office.Placilla]));

        Assert.Equal(Office.Placilla, Assert.Single(file.Cases).Office);
    }

    [Fact]
    public void Includes_the_audit_trail_of_the_persons_cases()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case("12.345.678-5", Office.AvenidaArgentina, new DateOnly(2026, 1, 10)));
        var other = db.Cases.Insert(Case("11.111.111-1", Office.AvenidaArgentina, new DateOnly(2026, 1, 10)));
        Execute(db.ConnectionString, $"""
            INSERT INTO CaseAuditLog (FolderCaseId, ChangedBy, ChangedAt, FieldName, OldValue, NewValue) VALUES
            ({id}, 'ana', '2026-01-11T10:00:00+00:00', 'Estado', NULL, 'EN OF.43'),
            ({id}, 'luis', '2026-01-12T10:00:00+00:00', 'Decisión', NULL, 'OTORGADO'),
            ({other}, 'ana', '2026-01-12T10:00:00+00:00', 'Estado', NULL, 'X');
            """);

        var file = new PersonFileQuery(db.ConnectionString).Load("12.345.678-5", PersonFileScope.All);

        Assert.Equal(["luis", "ana"], file.Audit.Select(a => a.ChangedBy));
        Assert.Equal(Office.AvenidaArgentina, file.Audit[0].Office);
    }

    [Fact]
    public void Includes_f8_address_change_and_certificate_requests()
    {
        using var db = new SqliteTestDatabase();
        var f8 = new UrgentRequestRepository(db.ConnectionString);
        f8.EnsureSchema();
        f8.Insert(new UrgentRequest { NombreCompleto = "JUAN PEREZ", Rut = "12345678-5", Estado = "EN PROCESO", Origin = "Manual", FechaPeticion = new DateOnly(2026, 2, 1) });
        var certificados = new CertificadoRequestRepository(db.ConnectionString);
        certificados.EnsureSchema();
        certificados.Insert(new CertificadoRequest { NombreCompleto = "JUAN PEREZ", Rut = "12.345.678-5", Comuna = "VIÑA DEL MAR", FechaIngreso = new DateOnly(2026, 2, 3), CreatedAt = DateTimeOffset.UtcNow });
        new LicenciasCarpetas.CambioDomicilio.Data.CambioDomicilioRequestRepository(db.ConnectionString).EnsureSchema();
        new LicenciasCarpetas.CambioDomicilio.Data.OutboundAddressChangeRequestRepository(db.ConnectionString).EnsureSchema();
        Execute(db.ConnectionString, """
            INSERT INTO PersonRequest (FullName, Rut, Comuna, SourceMessageId, SourceSubject, SourceSender, NeedsReview, Status, ReceivedAt, CreatedAt)
            VALUES ('JUAN PEREZ', '12.345.678-5', 'QUILPUÉ', 'm1', 's', 'x@y', 0, 'Pendiente', '2026-02-05T00:00:00+00:00', '2026-02-05T00:00:00+00:00');
            INSERT INTO OutboundAddressChangeRequest (FullName, Rut, DestinationComuna, Status, CreatedAt, CreatedByUserId)
            VALUES ('JUAN PEREZ', '12345678-5', 'LIMACHE', 'Enviada', '2026-02-06T00:00:00+00:00', 1);
            """);

        var file = new PersonFileQuery(db.ConnectionString).Load("12.345.678-5", PersonFileScope.All);

        Assert.Equal("EN PROCESO", Assert.Single(file.F8Requests).Status);
        Assert.Equal("VIÑA DEL MAR", Assert.Single(file.Certificates).Detail);
        Assert.Equal("QUILPUÉ", Assert.Single(file.AddressChangesReceived).Detail);
        Assert.Equal("LIMACHE", Assert.Single(file.AddressChangesRequested).Detail);
        Assert.True(file.HasAnyRecord);
    }

    [Fact]
    public void Missing_module_tables_read_as_empty()
    {
        using var db = new SqliteTestDatabase();

        var file = new PersonFileQuery(db.ConnectionString).Load("12.345.678-5", PersonFileScope.All);

        Assert.Empty(file.F8Requests);
        Assert.Empty(file.Certificates);
        Assert.False(file.HasAnyRecord);
    }

    [Theory]
    [InlineData("09.876.543-3", "98765433")]
    [InlineData("10.000.013-k", "10000013K")]
    public void Canonical_form_strips_format_and_leading_zeros(string input, string expected)
        => Assert.Equal(expected, PersonFileQuery.Canonical(input));

    private static FolderCase Case(string rut, Office office, DateOnly citation) => new()
    {
        FullName = "JUAN PEREZ",
        Rut = rut,
        Office = office,
        CitationDate = citation,
        FolderState = FolderState.SubidaAConaset
    };

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
