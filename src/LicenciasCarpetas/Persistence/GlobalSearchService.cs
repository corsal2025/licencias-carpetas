using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Persistence;

public interface IGlobalSearchService
{
    /// <summary>Cases are limited to <paramref name="scope"/> (per-office roles); F8, Cambio de
    /// Domicilio and Certificados have no office and are not filtered.</summary>
    IReadOnlyList<GlobalSearchResult> Search(string query, int limit = 20, OfficeScope? scope = null);
}

public sealed class GlobalSearchService(string connectionString) : IGlobalSearchService
{
    public IReadOnlyList<GlobalSearchResult> Search(string query, int limit = 20, OfficeScope? scope = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var trimmed = query.Trim();
        var rutClean = trimmed.Replace(".", string.Empty).Replace("-", string.Empty);
        var results = new List<GlobalSearchResult>();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // 1. Search FolderCase (Casos)
        try
        {
            using var cmd = connection.CreateCommand();
            var allowed = scope?.AllowedOffices;
            var officeClause = allowed is null
                ? string.Empty
                : allowed.Count == 0 ? "AND 1 = 0" : $"AND Office IN ({string.Join(", ", allowed.Select(o => (int)o))})";
            cmd.CommandText = $"""
                SELECT Id, FullName, Rut, CitationDate, FolderState, FolderStateRaw
                FROM FolderCase
                WHERE DeletedAt IS NULL {officeClause}
                  AND (FullName LIKE $search COLLATE NOCASE OR replace(replace(Rut, '.', ''), '-', '') LIKE $rutSearch)
                ORDER BY CitationDate DESC, Id DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$search", $"%{trimmed}%");
            cmd.Parameters.AddWithValue("$rutSearch", $"%{rutClean}%");
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var name = reader.IsDBNull(1) ? "Sin Nombre" : reader.GetString(1);
                var rut = reader.IsDBNull(2) ? null : reader.GetString(2);
                var citation = reader.IsDBNull(3) ? null : reader.GetString(3);
                var stateInt = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var stateRaw = reader.IsDBNull(5) ? null : reader.GetString(5);

                var stateDisplay = stateInt is { } s && Enum.IsDefined(typeof(FolderState), s)
                    ? FolderStateCatalog.Display((FolderState)s)
                    : stateRaw ?? "Pendiente";

                results.Add(new GlobalSearchResult
                {
                    Module = "Gestión de Licencias",
                    Title = name,
                    Rut = rut,
                    Detail = citation is not null ? $"Citación: {citation}" : null,
                    Status = stateDisplay,
                    Url = $"/Index?search={Uri.EscapeDataString(rut ?? name)}"
                });
            }
        }
        catch
        {
            // Table might not exist in early setup
        }

        // 2. Search UrgentRequest (F8 Urgentes)
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, NombreCompleto, Rut, FechaPeticion, Estado
                FROM UrgentRequest
                WHERE (NombreCompleto LIKE $search COLLATE NOCASE OR replace(replace(Rut, '.', ''), '-', '') LIKE $rutSearch)
                ORDER BY Id DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$search", $"%{trimmed}%");
            cmd.Parameters.AddWithValue("$rutSearch", $"%{rutClean}%");
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var name = reader.IsDBNull(1) ? "Sin Nombre" : reader.GetString(1);
                var rut = reader.IsDBNull(2) ? null : reader.GetString(2);
                var fecha = reader.IsDBNull(3) ? null : reader.GetString(3);
                var estado = reader.IsDBNull(4) ? null : reader.GetString(4);

                results.Add(new GlobalSearchResult
                {
                    Module = "F8 Urgentes",
                    Title = name,
                    Rut = rut,
                    Detail = fecha is not null ? $"Petición: {fecha}" : null,
                    Status = estado ?? "En proceso",
                    Url = $"/F8/Index?search={Uri.EscapeDataString(rut ?? name)}"
                });
            }
        }
        catch
        {
            // Module might not have table created yet
        }

        // 3. Search PersonRequest (Cambio de Domicilio - Recibidas)
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, FullName, Rut, Comuna, Status
                FROM PersonRequest
                WHERE (FullName LIKE $search COLLATE NOCASE OR replace(replace(Rut, '.', ''), '-', '') LIKE $rutSearch)
                ORDER BY Id DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$search", $"%{trimmed}%");
            cmd.Parameters.AddWithValue("$rutSearch", $"%{rutClean}%");
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var name = reader.IsDBNull(1) ? "Sin Nombre" : reader.GetString(1);
                var rut = reader.IsDBNull(2) ? null : reader.GetString(2);
                var comuna = reader.IsDBNull(3) ? null : reader.GetString(3);
                var status = reader.IsDBNull(4) ? null : reader.GetString(4);

                results.Add(new GlobalSearchResult
                {
                    Module = "Cambio Domicilio (Recibida)",
                    Title = name,
                    Rut = rut,
                    Detail = comuna is not null ? $"Comuna: {comuna}" : null,
                    Status = status,
                    Url = $"/CambioDomicilio/Index?search={Uri.EscapeDataString(rut ?? name)}"
                });
            }
        }
        catch
        {
            // Table might not exist
        }

        // 4. Search OutboundAddressChangeRequest (Cambio de Domicilio - Solicitadas)
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, FullName, Rut, DestinationComuna, coalesce(WorkflowState, Status)
                FROM OutboundAddressChangeRequest
                WHERE (FullName LIKE $search COLLATE NOCASE OR replace(replace(Rut, '.', ''), '-', '') LIKE $rutSearch)
                ORDER BY Id DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$search", $"%{trimmed}%");
            cmd.Parameters.AddWithValue("$rutSearch", $"%{rutClean}%");
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var name = reader.IsDBNull(1) ? "Sin Nombre" : reader.GetString(1);
                var rut = reader.IsDBNull(2) ? null : reader.GetString(2);
                var comuna = reader.IsDBNull(3) ? null : reader.GetString(3);
                var status = reader.IsDBNull(4) ? null : reader.GetString(4);

                results.Add(new GlobalSearchResult
                {
                    Module = "Cambio Domicilio (Solicitada)",
                    Title = name,
                    Rut = rut,
                    Detail = comuna is not null ? $"Para: {comuna}" : null,
                    Status = status,
                    Url = $"/CambioDomicilio/Solicitar/Index?search={Uri.EscapeDataString(rut ?? name)}"
                });
            }
        }
        catch
        {
            // Table might not exist
        }

        // 5. Search CertificadoRequest (Carpetas para Certificados)
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT Id, NombreCompleto, Rut, Comuna, EstadoActual
                FROM CertificadoRequest
                WHERE (NombreCompleto LIKE $search COLLATE NOCASE OR replace(replace(Rut, '.', ''), '-', '') LIKE $rutSearch)
                ORDER BY Id DESC
                LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$search", $"%{trimmed}%");
            cmd.Parameters.AddWithValue("$rutSearch", $"%{rutClean}%");
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.IsDBNull(1) ? "Sin Nombre" : reader.GetString(1);
                var rut = reader.IsDBNull(2) ? null : reader.GetString(2);
                var comuna = reader.IsDBNull(3) ? null : reader.GetString(3);
                var status = reader.IsDBNull(4) ? null : reader.GetString(4);

                results.Add(new GlobalSearchResult
                {
                    Module = "Certificados",
                    Title = name,
                    Rut = rut,
                    Detail = comuna is not null ? $"Comuna: {comuna}" : null,
                    Status = status,
                    Url = $"/Certificados/Index?search={Uri.EscapeDataString(rut ?? name)}"
                });
            }
        }
        catch
        {
            // Table might not exist
        }

        return results.Take(limit).ToList();
    }
}

