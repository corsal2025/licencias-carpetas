using System.Globalization;
using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Dashboard.Auth;

public sealed record PermissionAuditEntry(
    DateTimeOffset ChangedAt, string ChangedBy, long TargetUserId, string TargetUsername,
    string Field, string? Before, string? After);

public interface IUserOfficeRepository
{
    /// <summary>Creates UserOffice/PermissionAudit. The first time the table is created every
    /// existing account gets the three offices, so nobody loses access on upgrade.</summary>
    void EnsureSchema();

    IReadOnlyList<Office> For(long userId);

    /// <summary>Replaces a user's offices, audits the change and rotates the user's security stamp
    /// so open sessions pick it up on their next request. One transaction.</summary>
    void Replace(long userId, IReadOnlyCollection<Office> offices, string changedBy);

    /// <summary>Audits a role/module change and rotates the stamp.</summary>
    void RecordRoleChange(long userId, string changedBy, string? before, string? after);

    void DeleteForUser(long userId);

    IReadOnlyList<PermissionAuditEntry> Audit(int limit = 200);
}

public sealed class UserOfficeRepository(string connectionString) : IUserOfficeRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        bool existed;
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'UserOffice'";
            existed = check.ExecuteScalar() is not null;
        }

        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS UserOffice (
                    UserId INTEGER NOT NULL,
                    Office INTEGER NOT NULL,
                    PRIMARY KEY (UserId, Office)
                );
                CREATE TABLE IF NOT EXISTS PermissionAudit (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ChangedAt TEXT NOT NULL,
                    ChangedBy TEXT NOT NULL,
                    TargetUserId INTEGER NOT NULL,
                    TargetUsername TEXT NOT NULL,
                    Field TEXT NOT NULL,
                    Before TEXT NULL,
                    After TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        // Solo en la creación: un reinicio posterior no debe devolver sedes quitadas a propósito.
        if (!existed)
        {
            using var migrate = connection.CreateCommand();
            migrate.Transaction = transaction;
            migrate.CommandText = $"""
                INSERT OR IGNORE INTO UserOffice (UserId, Office)
                SELECT u.Id, o.v FROM DashboardUser u
                CROSS JOIN ({string.Join(" UNION ALL ", Enum.GetValues<Office>().Select(o => $"SELECT {(int)o} AS v"))}) o
                """;
            migrate.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<Office> For(long userId)
    {
        using var connection = Open();
        return ReadOffices(connection, null, userId);
    }

    public void Replace(long userId, IReadOnlyCollection<Office> offices, string changedBy)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var before = ReadOffices(connection, transaction, userId);
        var after = offices.Distinct().Order().ToList();

        Execute(connection, transaction, "DELETE FROM UserOffice WHERE UserId = $user", ("$user", userId));
        foreach (var office in after)
        {
            Execute(connection, transaction, "INSERT INTO UserOffice (UserId, Office) VALUES ($user, $office)",
                ("$user", userId), ("$office", (int)office));
        }

        if (!before.SequenceEqual(after))
        {
            Audit(connection, transaction, userId, changedBy, "Sedes", Describe(before), Describe(after));
            RotateStamp(connection, transaction, userId);
        }

        transaction.Commit();
    }

    public void RecordRoleChange(long userId, string changedBy, string? before, string? after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        Audit(connection, transaction, userId, changedBy, "Rol y módulos", before, after);
        RotateStamp(connection, transaction, userId);
        transaction.Commit();
    }

    public void DeleteForUser(long userId)
    {
        using var connection = Open();
        Execute(connection, null, "DELETE FROM UserOffice WHERE UserId = $user", ("$user", userId));
    }

    public IReadOnlyList<PermissionAuditEntry> Audit(int limit = 200)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ChangedAt, ChangedBy, TargetUserId, TargetUsername, Field, Before, After
            FROM PermissionAudit ORDER BY Id DESC LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);
        var entries = new List<PermissionAuditEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new PermissionAuditEntry(
                DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                reader.GetString(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return entries;
    }

    public static string Describe(IEnumerable<Office> offices)
    {
        var list = offices.Order().Select(OfficeCatalog.Display).ToList();
        return list.Count == 0 ? "(ninguna)" : string.Join(", ", list);
    }

    private static List<Office> ReadOffices(SqliteConnection connection, SqliteTransaction? transaction, long userId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Office FROM UserOffice WHERE UserId = $user ORDER BY Office";
        command.Parameters.AddWithValue("$user", userId);
        var offices = new List<Office>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var value = reader.GetInt32(0);
            if (Enum.IsDefined(typeof(Office), value))
            {
                offices.Add((Office)value);
            }
        }

        return offices;
    }

    private static void Audit(SqliteConnection connection, SqliteTransaction transaction, long userId,
        string changedBy, string field, string? before, string? after)
    {
        Execute(connection, transaction, """
            INSERT INTO PermissionAudit (ChangedAt, ChangedBy, TargetUserId, TargetUsername, Field, Before, After)
            VALUES ($at, $by, $user, coalesce((SELECT Username FROM DashboardUser WHERE Id = $user), ''), $field, $before, $after)
            """,
            ("$at", DateTimeOffset.UtcNow.ToString("O")), ("$by", changedBy), ("$user", userId),
            ("$field", field), ("$before", before), ("$after", after));
    }

    private static void RotateStamp(SqliteConnection connection, SqliteTransaction transaction, long userId) =>
        Execute(connection, transaction, "UPDATE DashboardUser SET SecurityStamp = $stamp WHERE Id = $user",
            ("$stamp", Guid.NewGuid().ToString("N")), ("$user", userId));

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
