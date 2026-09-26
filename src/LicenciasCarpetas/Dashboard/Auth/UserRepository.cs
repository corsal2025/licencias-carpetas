using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Dashboard.Auth;

public interface IUserRepository
{
    void EnsureSchema();
    DashboardUser? FindByUsername(string username);
    DashboardUser? FindById(long id);
    void Insert(DashboardUser user);
    void UpdatePassword(long id, string hash, string salt, int iterations);

    /// <summary>Recovery: sets a new password and clears any lockout, so the operator can get in
    /// straight away instead of waiting out the fifteen minutes that locked them out.</summary>
    void ResetPassword(long id, string hash, string salt, int iterations);

    /// <summary>Usernames, for the operator who forgot which account they created.</summary>
    IReadOnlyList<string> AllUsernames();

    /// <summary>Full rows, for the Usuarios screen — necesita rol y módulos, no solo el nombre.</summary>
    IReadOnlyList<DashboardUser> AllUsers();
    void Delete(string username);
    void RecordFailedLogin(long id, int attempts, DateTimeOffset? lockedUntil);
    void ResetFailedLogins(long id);
    int Count();

    /// <summary>Rol y acceso a módulos externos — separado del alta y del cambio de clave porque
    /// se edita después, no solo al crear la cuenta.</summary>
    void UpdateRole(long id, UserRole role, bool canAccessCambioDomicilio, bool canAccessF8Urgentes);

    /// <summary>Firma personal que se agrega a los correos salientes de Cambio de Domicilio.</summary>
    void UpdateEmailFooter(long id, string? footer);
}

public sealed class UserRepository(string connectionString) : IUserRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS DashboardUser (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                Iterations INTEGER NOT NULL,
                FailedLoginAttempts INTEGER NOT NULL DEFAULT 0,
                LockedUntil TEXT NULL,
                CreatedAt TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        // Rol por defecto Administrador: una base creada antes de que existieran los roles no debe
        // dejar a nadie afuera de una pantalla que ya usaba.
        AddColumnIfMissing(connection, "Role", "TEXT NOT NULL DEFAULT 'Administrador'");
        AddColumnIfMissing(connection, "CanAccessCambioDomicilio", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "CanAccessF8Urgentes", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "EmailFooter", "TEXT NULL");
        AddColumnIfMissing(connection, "SecurityStamp", "TEXT NULL");

        using var stamp = connection.CreateCommand();
        stamp.CommandText = "UPDATE DashboardUser SET SecurityStamp = lower(hex(randomblob(16))) WHERE SecurityStamp IS NULL";
        stamp.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string column, string definition)
    {
        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA table_info(DashboardUser)";
            using var reader = pragmaCommand.ExecuteReader();
            var nameOrdinal = reader.GetOrdinal("name");
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(nameOrdinal), column, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE DashboardUser ADD COLUMN {column} {definition}";
        alterCommand.ExecuteNonQuery();
    }

    public DashboardUser? FindByUsername(string username)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        // COLLATE NOCASE and a trim: accounts are stored lower-cased, but the operator types the
        // name however they type it, and SQLite compares text case-sensitively by default.
        command.CommandText = "SELECT * FROM DashboardUser WHERE Username = $username COLLATE NOCASE LIMIT 1";
        command.Parameters.AddWithValue("$username", username.Trim());
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public DashboardUser? FindById(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM DashboardUser WHERE Id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void Insert(DashboardUser user)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DashboardUser (Username, PasswordHash, PasswordSalt, Iterations, FailedLoginAttempts,
                LockedUntil, CreatedAt, Role, CanAccessCambioDomicilio, CanAccessF8Urgentes, SecurityStamp)
            VALUES ($username, $hash, $salt, $iterations, 0, NULL, $createdAt, $role, $cambio, $f8, lower(hex(randomblob(16))))
            """;
        command.Parameters.AddWithValue("$username", user.Username);
        command.Parameters.AddWithValue("$hash", user.PasswordHash);
        command.Parameters.AddWithValue("$salt", user.PasswordSalt);
        command.Parameters.AddWithValue("$iterations", user.Iterations);
        command.Parameters.AddWithValue("$createdAt", user.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$role", user.Role.ToString());
        command.Parameters.AddWithValue("$cambio", user.CanAccessCambioDomicilio ? 1 : 0);
        command.Parameters.AddWithValue("$f8", user.CanAccessF8Urgentes ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void UpdateRole(long id, UserRole role, bool canAccessCambioDomicilio, bool canAccessF8Urgentes)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DashboardUser
            SET Role = $role, CanAccessCambioDomicilio = $cambio, CanAccessF8Urgentes = $f8
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$role", role.ToString());
        command.Parameters.AddWithValue("$cambio", canAccessCambioDomicilio ? 1 : 0);
        command.Parameters.AddWithValue("$f8", canAccessF8Urgentes ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void UpdatePassword(long id, string hash, string salt, int iterations)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DashboardUser SET PasswordHash = $hash, PasswordSalt = $salt, Iterations = $iterations WHERE Id = $id";
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$iterations", iterations);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void ResetPassword(long id, string hash, string salt, int iterations)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE DashboardUser
            SET PasswordHash = $hash, PasswordSalt = $salt, Iterations = $iterations,
                FailedLoginAttempts = 0, LockedUntil = NULL
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$iterations", iterations);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Delete(string username)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'UserOffice'";
            if (check.ExecuteScalar() is not null)
            {
                // SQLite no aplica FKs sin PRAGMA foreign_keys: las sedes del usuario se borran a mano.
                using var offices = connection.CreateCommand();
                offices.Transaction = transaction;
                offices.CommandText = "DELETE FROM UserOffice WHERE UserId IN (SELECT Id FROM DashboardUser WHERE Username = $username)";
                offices.Parameters.AddWithValue("$username", username);
                offices.ExecuteNonQuery();
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM DashboardUser WHERE Username = $username";
        command.Parameters.AddWithValue("$username", username);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void RecordFailedLogin(long id, int attempts, DateTimeOffset? lockedUntil)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DashboardUser SET FailedLoginAttempts = $attempts, LockedUntil = $lockedUntil WHERE Id = $id";
        command.Parameters.AddWithValue("$attempts", attempts);
        command.Parameters.AddWithValue("$lockedUntil", (object?)lockedUntil?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void ResetFailedLogins(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DashboardUser SET FailedLoginAttempts = 0, LockedUntil = NULL WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> AllUsernames()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Username FROM DashboardUser ORDER BY Username ASC";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }

    public IReadOnlyList<DashboardUser> AllUsers()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM DashboardUser ORDER BY Username ASC";

        var result = new List<DashboardUser>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(Map(reader));
        }
        return result;
    }

    public int Count()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM DashboardUser";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static DashboardUser Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        Username = reader.GetString(reader.GetOrdinal("Username")),
        PasswordHash = reader.GetString(reader.GetOrdinal("PasswordHash")),
        PasswordSalt = reader.GetString(reader.GetOrdinal("PasswordSalt")),
        Iterations = reader.GetInt32(reader.GetOrdinal("Iterations")),
        FailedLoginAttempts = reader.GetInt32(reader.GetOrdinal("FailedLoginAttempts")),
        LockedUntil = reader.IsDBNull(reader.GetOrdinal("LockedUntil"))
            ? null
            : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("LockedUntil"))),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        Role = Enum.TryParse<UserRole>(reader.GetString(reader.GetOrdinal("Role")), out var role)
            ? role
            : UserRole.Administrador,
        CanAccessCambioDomicilio = reader.GetInt32(reader.GetOrdinal("CanAccessCambioDomicilio")) == 1,
        CanAccessF8Urgentes = reader.GetInt32(reader.GetOrdinal("CanAccessF8Urgentes")) == 1,
        EmailFooter = reader.IsDBNull(reader.GetOrdinal("EmailFooter")) ? null : reader.GetString(reader.GetOrdinal("EmailFooter")),
        SecurityStamp = reader.IsDBNull(reader.GetOrdinal("SecurityStamp")) ? null : reader.GetString(reader.GetOrdinal("SecurityStamp"))
    };

    public void UpdateEmailFooter(long id, string? footer)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DashboardUser SET EmailFooter = $footer WHERE Id = $id";
        command.Parameters.AddWithValue("$footer", (object?)footer ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }
}
