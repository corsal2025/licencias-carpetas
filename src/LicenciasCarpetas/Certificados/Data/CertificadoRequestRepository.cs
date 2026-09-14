using Microsoft.Data.Sqlite;
using LicenciasCarpetas.Certificados.Domain;

namespace LicenciasCarpetas.Certificados.Data;

public sealed class CertificadoRequestRepository(string connectionString) : ICertificadoRequestRepository
{
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CertificadoRequest (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NombreCompleto TEXT NOT NULL,
                Rut TEXT NOT NULL,
                Comuna TEXT NOT NULL,
                FechaIngreso TEXT NOT NULL,
                FechaPeticion TEXT NULL,
                FechaEmision TEXT NULL,
                Estado TEXT NOT NULL DEFAULT 'PENDIENTE',
                EstadoActual TEXT NOT NULL DEFAULT 'PENDIENTE',
                Folio TEXT NULL,
                Direccion TEXT NULL,
                Marked INTEGER NOT NULL DEFAULT 0,
                PendienteCarpeta INTEGER NOT NULL DEFAULT 0,
                Origin TEXT NOT NULL DEFAULT 'Solicitar',
                SourceId INTEGER NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CertificadoRequest_Rut ON CertificadoRequest(Rut);
        """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<CertificadoRequest> GetAll()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, NombreCompleto, Rut, Comuna, FechaIngreso, FechaPeticion, FechaEmision,
                   Estado, EstadoActual, Folio, Direccion, Marked, PendienteCarpeta, Origin,
                   SourceId, CreatedAt, UpdatedAt
            FROM CertificadoRequest
            ORDER BY Marked DESC, Id DESC
        """;

        using var reader = command.ExecuteReader();
        var list = new List<CertificadoRequest>();
        while (reader.Read())
        {
            list.Add(ReadRow(reader));
        }
        return list;
    }

    public CertificadoRequest? FindById(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, NombreCompleto, Rut, Comuna, FechaIngreso, FechaPeticion, FechaEmision,
                   Estado, EstadoActual, Folio, Direccion, Marked, PendienteCarpeta, Origin,
                   SourceId, CreatedAt, UpdatedAt
            FROM CertificadoRequest
            WHERE Id = $id
        """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    public CertificadoRequest? FindByRut(string rut)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, NombreCompleto, Rut, Comuna, FechaIngreso, FechaPeticion, FechaEmision,
                   Estado, EstadoActual, Folio, Direccion, Marked, PendienteCarpeta, Origin,
                   SourceId, CreatedAt, UpdatedAt
            FROM CertificadoRequest
            WHERE Rut = $rut
            LIMIT 1
        """;
        command.Parameters.AddWithValue("$rut", rut);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRow(reader) : null;
    }

    public long Insert(CertificadoRequest request)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CertificadoRequest (
                NombreCompleto, Rut, Comuna, FechaIngreso, FechaPeticion, FechaEmision,
                Estado, EstadoActual, Folio, Direccion, Marked, PendienteCarpeta, Origin,
                SourceId, CreatedAt, UpdatedAt
            ) VALUES (
                $nombreCompleto, $rut, $comuna, $fechaIngreso, $fechaPeticion, $fechaEmision,
                $estado, $estadoActual, $folio, $direccion, $marked, $pendienteCarpeta, $origin,
                $sourceId, $createdAt, $updatedAt
            );
            SELECT last_insert_rowid();
        """;
        BindParams(command, request);
        var id = (long)command.ExecuteScalar()!;
        request.Id = id;
        return id;
    }

    public void Update(CertificadoRequest request)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CertificadoRequest
            SET NombreCompleto = $nombreCompleto, Rut = $rut, Comuna = $comuna,
                FechaIngreso = $fechaIngreso, FechaPeticion = $fechaPeticion, FechaEmision = $fechaEmision,
                Estado = $estado, EstadoActual = $estadoActual, Folio = $folio, Direccion = $direccion,
                Marked = $marked, PendienteCarpeta = $pendienteCarpeta, Origin = $origin,
                SourceId = $sourceId, CreatedAt = $createdAt, UpdatedAt = $updatedAt
            WHERE Id = $id
        """;
        BindParams(command, request);
        command.Parameters.AddWithValue("$id", request.Id);
        command.ExecuteNonQuery();
    }

    public void SetMarked(long id, bool marked)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CertificadoRequest SET Marked = $marked, UpdatedAt = $now WHERE Id = $id";
        command.Parameters.AddWithValue("$marked", marked ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetPendienteCarpeta(long id, bool pendienteCarpeta)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CertificadoRequest SET PendienteCarpeta = $pendienteCarpeta, UpdatedAt = $now WHERE Id = $id";
        command.Parameters.AddWithValue("$pendienteCarpeta", pendienteCarpeta ? 1 : 0);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetEstado(long id, string estado)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CertificadoRequest SET Estado = $estado, UpdatedAt = $now WHERE Id = $id";
        command.Parameters.AddWithValue("$estado", estado);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetEmitido(long id, DateOnly fechaEmision, string? folio)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CertificadoRequest
            SET FechaEmision = $fechaEmision, Folio = $folio,
                Estado = 'CERTIFICADO EMITIDO', EstadoActual = 'EMITIDO',
                UpdatedAt = $now
            WHERE Id = $id
        """;
        command.Parameters.AddWithValue("$fechaEmision", fechaEmision.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$folio", (object?)folio ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CertificadoRequest WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void BindParams(SqliteCommand command, CertificadoRequest r)
    {
        command.Parameters.AddWithValue("$nombreCompleto", r.NombreCompleto);
        command.Parameters.AddWithValue("$rut", r.Rut);
        command.Parameters.AddWithValue("$comuna", r.Comuna);
        command.Parameters.AddWithValue("$fechaIngreso", r.FechaIngreso.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$fechaPeticion", r.FechaPeticion.HasValue ? r.FechaPeticion.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        command.Parameters.AddWithValue("$fechaEmision", r.FechaEmision.HasValue ? r.FechaEmision.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        command.Parameters.AddWithValue("$estado", r.Estado);
        command.Parameters.AddWithValue("$estadoActual", r.EstadoActual);
        command.Parameters.AddWithValue("$folio", (object?)r.Folio ?? DBNull.Value);
        command.Parameters.AddWithValue("$direccion", (object?)r.Direccion ?? DBNull.Value);
        command.Parameters.AddWithValue("$marked", r.Marked ? 1 : 0);
        command.Parameters.AddWithValue("$pendienteCarpeta", r.PendienteCarpeta ? 1 : 0);
        command.Parameters.AddWithValue("$origin", r.Origin);
        command.Parameters.AddWithValue("$sourceId", (object?)r.SourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", r.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", r.UpdatedAt.HasValue ? r.UpdatedAt.Value.ToString("O") : DBNull.Value);
    }

    private static CertificadoRequest ReadRow(SqliteDataReader reader)
    {
        return new CertificadoRequest
        {
            Id = reader.GetInt64(0),
            NombreCompleto = reader.GetString(1),
            Rut = reader.GetString(2),
            Comuna = reader.GetString(3),
            FechaIngreso = DateOnly.Parse(reader.GetString(4)),
            FechaPeticion = reader.IsDBNull(5) ? null : DateOnly.Parse(reader.GetString(5)),
            FechaEmision = reader.IsDBNull(6) ? null : DateOnly.Parse(reader.GetString(6)),
            Estado = reader.GetString(7),
            EstadoActual = reader.GetString(8),
            Folio = reader.IsDBNull(9) ? null : reader.GetString(9),
            Direccion = reader.IsDBNull(10) ? null : reader.GetString(10),
            Marked = reader.GetInt32(11) == 1,
            PendienteCarpeta = reader.GetInt32(12) == 1,
            Origin = reader.GetString(13),
            SourceId = reader.IsDBNull(14) ? null : reader.GetInt64(14),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(15)),
            UpdatedAt = reader.IsDBNull(16) ? null : DateTimeOffset.Parse(reader.GetString(16))
        };
    }
}
