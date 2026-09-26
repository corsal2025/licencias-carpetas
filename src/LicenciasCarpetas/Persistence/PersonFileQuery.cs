using System.Globalization;
using System.Text;
using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Persistence;

/// <summary>Which offices a person file may show. <see cref="AllowedOffices"/> null = every office.
/// Plug-in point for per-office roles: the caller passes the user's allowed offices.</summary>
public sealed record PersonFileScope(IReadOnlyCollection<Office>? AllowedOffices)
{
    public static PersonFileScope All { get; } = new((IReadOnlyCollection<Office>?)null);

    public bool Allows(Office office) => AllowedOffices is null || AllowedOffices.Contains(office);
}

public sealed record PersonFileCase(
    long Id,
    Office Office,
    DateOnly? CitationDate,
    string? FolderState,
    string? FinalDecision,
    string? MoralIdoneity,
    string? LastFolder,
    string? CodigoF8,
    bool Attended);

public sealed record PersonFileAuditEntry(
    long FolderCaseId,
    Office Office,
    string ChangedBy,
    DateTimeOffset ChangedAt,
    string FieldName,
    string? OldValue,
    string? NewValue);

/// <summary>A request from another module (F8, Cambio de Domicilio, Certificados), flattened to
/// what the person file shows.</summary>
public sealed record PersonFileRequest(long Id, string? Date, string? Detail, string? Status);

public sealed class PersonFile
{
    public string Rut { get; init; } = string.Empty;
    public string? FullName { get; init; }
    public IReadOnlyList<PersonFileCase> Cases { get; init; } = [];
    public IReadOnlyList<PersonFileAuditEntry> Audit { get; init; } = [];
    public IReadOnlyList<PersonFileRequest> F8Requests { get; init; } = [];
    public IReadOnlyList<PersonFileRequest> AddressChangesReceived { get; init; } = [];
    public IReadOnlyList<PersonFileRequest> AddressChangesRequested { get; init; } = [];
    public IReadOnlyList<PersonFileRequest> Certificates { get; init; } = [];

    public bool HasAnyRecord => Cases.Count + F8Requests.Count + AddressChangesReceived.Count
        + AddressChangesRequested.Count + Certificates.Count > 0;
}

public interface IPersonFileQuery
{
    PersonFile Load(string rut, PersonFileScope scope);
}

/// <summary>
/// Read-only, cross-module lookup of everything stored for one RUT. RUTs are stored in several
/// shapes (dotted, bare, zero-padded by <see cref="RutValidator"/>, lowercase k), so both sides are
/// compared in canonical form: digits + check digit, no leading zeros, uppercase. That defeats the
/// Rut indexes — acceptable at the current volume; an expression index can be added later.
/// </summary>
public sealed class PersonFileQuery(string connectionString) : IPersonFileQuery
{
    private static string CanonicalSql(string column) =>
        $"upper(ltrim(replace(replace(replace({column}, '.', ''), '-', ''), ' ', ''), '0'))";

    public static string Canonical(string rut)
    {
        var builder = new StringBuilder();
        foreach (var c in rut)
        {
            if (char.IsDigit(c) || c is 'k' or 'K')
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString().TrimStart('0');
    }

    public PersonFile Load(string rut, PersonFileScope scope)
    {
        var canonical = Canonical(rut);
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var (cases, fullName) = LoadCases(connection, canonical, scope);

        return new PersonFile
        {
            Rut = RutValidator.NormalizeAndValidate(rut) ?? rut,
            FullName = fullName,
            Cases = cases,
            Audit = LoadAudit(connection, cases),
            F8Requests = LoadRequests(connection, canonical, $"""
                SELECT Id, FechaPeticion, CodigoF8, coalesce(EstadoActual, Estado), NombreCompleto
                FROM UrgentRequest WHERE {CanonicalSql("Rut")} = $rut ORDER BY Id DESC
                """),
            AddressChangesReceived = LoadRequests(connection, canonical, $"""
                SELECT Id, substr(ReceivedAt, 1, 10), Comuna, Status, FullName
                FROM PersonRequest WHERE {CanonicalSql("Rut")} = $rut ORDER BY Id DESC
                """),
            AddressChangesRequested = LoadRequests(connection, canonical, $"""
                SELECT Id, substr(CreatedAt, 1, 10), DestinationComuna, coalesce(WorkflowState, Status), FullName
                FROM OutboundAddressChangeRequest WHERE {CanonicalSql("Rut")} = $rut ORDER BY Id DESC
                """),
            Certificates = LoadRequests(connection, canonical, $"""
                SELECT Id, FechaIngreso, Comuna, EstadoActual, NombreCompleto
                FROM CertificadoRequest WHERE {CanonicalSql("Rut")} = $rut ORDER BY Id DESC
                """)
        };
    }

    private static (IReadOnlyList<PersonFileCase> Cases, string? FullName) LoadCases(
        SqliteConnection connection, string canonical, PersonFileScope scope)
    {
        var cases = new List<PersonFileCase>();
        string? fullName = null;
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, Office, CitationDate, FolderState, FolderStateRaw, FinalDecision, FinalDecisionRaw,
                   MoralIdoneity, LastFolderDate, LastFolderComuna, CodigoF8, Attended, FullName
            FROM FolderCase
            WHERE DeletedAt IS NULL AND {CanonicalSql("Rut")} = $rut
            ORDER BY CitationDate DESC, Id DESC
            """;
        command.Parameters.AddWithValue("$rut", canonical);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var office = (Office)reader.GetInt32(1);
            if (!scope.Allows(office))
            {
                continue;
            }

            fullName ??= reader.IsDBNull(12) ? null : reader.GetString(12);
            cases.Add(new PersonFileCase(
                reader.GetInt64(0),
                office,
                reader.IsDBNull(2) ? null : DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                EnumDisplay<FolderState>(reader, 3, FolderStateCatalog.Display) ?? Text(reader, 4),
                EnumDisplay<FinalDecision>(reader, 5, FinalDecisionCatalog.Display) ?? Text(reader, 6),
                EnumDisplay<MoralIdoneity>(reader, 7, MoralIdoneityCatalog.Display),
                Text(reader, 8) ?? Text(reader, 9),
                Text(reader, 10),
                !reader.IsDBNull(11) && reader.GetInt64(11) != 0));
        }

        return (cases, fullName);
    }

    private static IReadOnlyList<PersonFileAuditEntry> LoadAudit(SqliteConnection connection, IReadOnlyList<PersonFileCase> cases)
    {
        if (cases.Count == 0)
        {
            return [];
        }

        var offices = cases.ToDictionary(c => c.Id, c => c.Office);
        var entries = new List<PersonFileAuditEntry>();
        using var command = connection.CreateCommand();
        var parameters = new List<string>();
        foreach (var (id, index) in offices.Keys.Select((id, index) => (id, index)))
        {
            parameters.Add($"$id{index}");
            command.Parameters.AddWithValue($"$id{index}", id);
        }

        command.CommandText = $"""
            SELECT FolderCaseId, ChangedBy, ChangedAt, FieldName, OldValue, NewValue
            FROM CaseAuditLog
            WHERE FolderCaseId IN ({string.Join(", ", parameters)})
            ORDER BY ChangedAt DESC, Id DESC
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var caseId = reader.GetInt64(0);
            entries.Add(new PersonFileAuditEntry(
                caseId,
                offices[caseId],
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                reader.GetString(3),
                Text(reader, 4),
                Text(reader, 5)));
        }

        return entries;
    }

    /// <summary>Runs a query shaped (Id, date, detail, status, name). A module whose table does not
    /// exist yet (never initialized on this install) reads as no requests.</summary>
    private static IReadOnlyList<PersonFileRequest> LoadRequests(SqliteConnection connection, string canonical, string sql)
    {
        var requests = new List<PersonFileRequest>();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$rut", canonical);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                requests.Add(new PersonFileRequest(reader.GetInt64(0), Text(reader, 1), Text(reader, 2), Text(reader, 3)));
            }
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return requests;
    }

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static string? EnumDisplay<T>(SqliteDataReader reader, int ordinal, Func<T, string> display) where T : struct, Enum
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetInt32(ordinal);
        return Enum.IsDefined(typeof(T), value) ? display((T)(object)value) : null;
    }
}
