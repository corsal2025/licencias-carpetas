using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Persistence;

public interface IComunaContactRepository
{
    void EnsureSchema();
    void EnsureSeed(string? csvPath = null);
    void Upsert(ComunaContact contact);
    void Update(long id, string comuna, string email, string? notes = null);
    IReadOnlyList<ComunaContact> All(string? search = null);
    void Delete(long id);
}

public sealed class ComunaContactRepository(string connectionString) : IComunaContactRepository
{
    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ComunaContact (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Comuna TEXT NOT NULL,
                Email TEXT NOT NULL,
                Notes TEXT NULL,
                UNIQUE (Comuna, Email)
            );
            """;
        command.ExecuteNonQuery();
    }

    public void EnsureSeed(string? csvPath = null)
    {
        var path = csvPath;
        if (string.IsNullOrEmpty(path))
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "data", "comunas.csv"),
                Path.Combine(Directory.GetCurrentDirectory(), "data", "comunas.csv"),
                Path.Combine(AppContext.BaseDirectory, "comunas.csv")
            };
            path = candidates.FirstOrDefault(File.Exists);
        }

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();

            // Limpiar datos antiguos ficticios si existen
            using (var cleanCmd = connection.CreateCommand())
            {
                cleanCmd.Transaction = tx;
                cleanCmd.CommandText = "DELETE FROM ComunaContact WHERE Email LIKE 'licencias@mun%' OR Notes IS NULL OR Notes != 'Directorio oficial';";
                cleanCmd.ExecuteNonQuery();
            }

            var lines = File.ReadAllLines(path);
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var comuna = parts[0].Trim().Trim('"');
                    var email = parts[1].Trim().Trim('"');
                    if (comuna.Length > 0 && email.Contains('@'))
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            INSERT INTO ComunaContact (Comuna, Email, Notes)
                            VALUES ($comuna, $email, 'Directorio oficial')
                            ON CONFLICT(Comuna, Email) DO NOTHING;
                            """;
                        cmd.Parameters.AddWithValue("$comuna", comuna);
                        cmd.Parameters.AddWithValue("$email", email);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            tx.Commit();
        }
    }

    public void Upsert(ComunaContact contact)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ComunaContact (Comuna, Email, Notes)
            VALUES ($comuna, $email, $notes)
            ON CONFLICT(Comuna, Email) DO UPDATE SET
                Notes = excluded.Notes;
            """;
        command.Parameters.AddWithValue("$comuna", contact.Comuna.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$email", contact.Email.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$notes", (object?)contact.Notes ?? "Directorio oficial");
        command.ExecuteNonQuery();
    }

    public void Update(long id, string comuna, string email, string? notes = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ComunaContact
            SET Comuna = $comuna, Email = $email, Notes = $notes
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$comuna", comuna.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$email", email.Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$notes", (object?)notes ?? "Directorio oficial");
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ComunaContact> All(string? search = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(search))
        {
            command.CommandText = "SELECT Id, Comuna, Email, Notes FROM ComunaContact ORDER BY Comuna ASC, Email ASC";
        }
        else
        {
            command.CommandText = "SELECT Id, Comuna, Email, Notes FROM ComunaContact WHERE Comuna LIKE $q OR Email LIKE $q ORDER BY Comuna ASC, Email ASC";
            command.Parameters.AddWithValue("$q", $"%{search.Trim()}%");
        }

        var results = new List<ComunaContact>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ComunaContact
            {
                Id = reader.GetInt64(0),
                Comuna = reader.GetString(1),
                Email = reader.GetString(2),
                Notes = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return results;
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ComunaContact WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
