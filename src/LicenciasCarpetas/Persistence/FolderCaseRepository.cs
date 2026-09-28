using System.Globalization;
using System.Text;
using LicenciasCarpetas.Domain;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Persistence;

public enum UpsertOutcome
{
    Inserted,
    Updated
}

public interface IFolderCaseRepository
{
    void EnsureSchema();
    UpsertOutcome Upsert(FolderCase folderCase);

    /// <summary>
    /// Importa un lote en una sola transacción. Fila por fila, cada Upsert abre y cierra su propia
    /// conexión: con las ~21.000 del libro eso son minuto y medio de puro ir y venir al archivo.
    /// </summary>
    IReadOnlyList<UpsertOutcome> UpsertMany(IReadOnlyList<FolderCase> folderCases);
    FolderCase? FindById(long id);
    IReadOnlyList<FolderCase> Query(CaseFilter filter, int skip, int take);
    IReadOnlyList<FolderCase> QueryAll(CaseFilter filter);
    int Count(CaseFilter filter);

    /// <summary>RUTs appearing on more than one case inside the current filter — the same person
    /// cited twice, which the workbook flags in violet.</summary>
    IReadOnlyList<string> DuplicateRuts(CaseFilter filter);
    int CountNeedingReview(IReadOnlyCollection<Office>? allowedOffices = null);
    IReadOnlyList<int> DistinctYears();
    /// <summary>Folders to pull from a sector, optionally narrowed to one citation day or month.</summary>
    IReadOnlyList<FolderCase> ForSector(FolderSector sector, bool onlyMarked, bool includePrinted = false,
        DateOnly? citationDay = null, int? year = null, int? month = null, IReadOnlyCollection<Office>? allowedOffices = null);

    /// <summary>Records that these cases went out on a printed sector list.</summary>
    void MarkSectorPrinted(IReadOnlyCollection<long> ids);

    /// <summary>Puts a case back on the pending list ("volver a pedir").</summary>
    void ClearSectorPrinted(long id);

    /// <summary>Wipes every row — reiniciar el proceso desde cero. Solo la tabla, no la base ni el
    /// esquema: los demás módulos (F8, Cambio de Domicilio) y las cuentas de usuario no se tocan.
    /// El único resguardo real es el respaldo que el llamador debe hacer antes de llamar esto.</summary>
    int DeleteAllPermanently();
    IReadOnlyList<(DateOnly Date, Office Office, int Scheduled, int Attended)> DailyAttendance(int year, int month, IReadOnlyCollection<Office>? allowedOffices = null);
    IReadOnlyList<(FolderState? State, int Count)> FolderStateBreakdown(int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices = null);
    IReadOnlyList<(FinalDecision? Decision, int Count)> FinalDecisionBreakdown(int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices = null);
    void UpdateEditableFields(long id, string? fullName, string? rut, DateOnly? citationDate,
        DateOnly? folderUploadedDate, DateOnly? lastFolderDate, string? lastFolderComuna,
        FolderState? folderState, FinalDecision? finalDecision, MoralIdoneity? moralIdoneity,
        string? attentionNote, bool needsReview, string? editedBy = null, string? cambioDomicilioComuna = null);
    void SetMarked(long id, bool marked);

    /// <summary>Whether the person showed up. Ticked from the cases screen and counted on the
    /// statistics screen; independent of the ATENCIÓN note.</summary>
    void SetAttended(long id, bool attended, string? editedBy = null);

    /// <summary>Los campos que el libro no trae: código F8, penúltima carpeta y clases de licencia.</summary>
    void UpdateCaseDetails(long id, string? codigoF8, DateOnly? penultimateFolderDate, string? licenceClasses = null, string? folioLicencia = null, string? editedBy = null);

    /// <summary>Nota libre sobre la persona, agregada aparte de guardar la fila — se abre bajo
    /// demanda desde el RUT, no ocupa una columna fija.</summary>
    void UpdateObservations(long id, string? observations, string? editedBy = null);

    /// <summary>Retrieves complete audit trail of modifications for a specific case.</summary>
    IReadOnlyList<CaseAuditEntry> GetAuditLog(long caseId);

    /// <summary>Retrieves all audit logs in a given date range for user statistics.</summary>
    IReadOnlyList<CaseAuditEntry> GetAuditLogsForPeriod(DateTimeOffset start, DateTimeOffset end, IReadOnlyCollection<Office>? allowedOffices = null);

    /// <summary>Cuántos casos hay por clase de licencia en el período. Un caso con varias clases
    /// suma en cada una: la pregunta es cuántas licencias se tramitan, no cuántas personas.</summary>
    IReadOnlyList<(LicenceClass Licence, int Count)> LicenceClassBreakdown(int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices = null);

    /// <summary>Moves the case to the bin. Recoverable with <see cref="Restore"/>.</summary>
    void Delete(long id);

    void Restore(long id);
    void DeletePermanently(long id);
    IReadOnlyList<FolderCase> Deleted(IReadOnlyCollection<Office>? allowedOffices = null);
    int CountDeleted(IReadOnlyCollection<Office>? allowedOffices = null);
    long Insert(FolderCase folderCase);
}

/// <summary>Plain SQLite, no ORM — same approach as the sibling OutlookComunaRouter service.</summary>
public sealed class FolderCaseRepository(string connectionString) : IFolderCaseRepository
{
    /// <summary>Tope de filas del listado de sector. Existe para que "ver todo el sector" no intente
    /// imprimir miles de páginas; la pantalla avisa cuando la lista llegó al tope.</summary>
    public const int SectorListLimit = 2000;

    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS FolderCase (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CitationDate TEXT NULL,
                FolderUploadedDate TEXT NULL,
                LastFolderDate TEXT NULL,
                LastFolderComuna TEXT NULL,
                FirstName TEXT NULL,
                LastName TEXT NULL,
                FullName TEXT NULL,
                Rut TEXT NULL,
                Office INTEGER NOT NULL,
                AttentionNote TEXT NULL,
                Attended INTEGER NOT NULL DEFAULT 0,
                MoralIdoneity INTEGER NULL,
                FolderState INTEGER NULL,
                FolderStateRaw TEXT NULL,
                FinalDecision INTEGER NULL,
                FinalDecisionRaw TEXT NULL,
                SourceSheet TEXT NULL,
                SourceRow INTEGER NOT NULL DEFAULT 0,
                NeedsReview INTEGER NOT NULL DEFAULT 0,
                Marked INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_FolderCase_Rut ON FolderCase (Rut);
            CREATE INDEX IF NOT EXISTS IX_FolderCase_CitationDate ON FolderCase (CitationDate);
            CREATE INDEX IF NOT EXISTS IX_FolderCase_Source ON FolderCase (SourceSheet, SourceRow);
            CREATE INDEX IF NOT EXISTS IX_FolderCase_Natural ON FolderCase (Office, CitationDate, Rut);
            """;
        command.ExecuteNonQuery();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA busy_timeout = 5000;
            CREATE INDEX IF NOT EXISTS IX_FolderCase_NeedsReview ON FolderCase (NeedsReview, DeletedAt);
            CREATE INDEX IF NOT EXISTS IX_FolderCase_DeletedAt ON FolderCase (DeletedAt);
            CREATE INDEX IF NOT EXISTS IX_FolderCase_Office_Date ON FolderCase (Office, DeletedAt, CitationDate);
            """;
        pragma.ExecuteNonQuery();

        AddColumnIfMissing(connection, "DeletedAt");
        AddColumnIfMissing(connection, "FullNameSort");
        AddColumnIfMissing(connection, "SectorPrintedAt");
        AddColumnIfMissing(connection, "PenultimateFolderDate");
        AddColumnIfMissing(connection, "CodigoF8");
        AddColumnIfMissing(connection, "FolioLicencia");
        AddColumnIfMissing(connection, "UpdatedBy");
        AddColumnIfMissing(connection, "LicenceClasses");
        AddColumnIfMissing(connection, "Email");
        AddColumnIfMissing(connection, "CellPhone");
        AddColumnIfMissing(connection, "Observations");
        AddColumnIfMissing(connection, "CambioDomicilioComuna");
        AddColumnIfMissing(connection, "FinalDecisionAt");
        BackfillFullNameSort(connection);

        using var auditCommand = connection.CreateCommand();
        auditCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS CaseAuditLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FolderCaseId INTEGER NOT NULL,
                ChangedBy TEXT NOT NULL,
                ChangedAt TEXT NOT NULL,
                FieldName TEXT NOT NULL,
                OldValue TEXT NULL,
                NewValue TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_CaseAuditLog_FolderCaseId ON CaseAuditLog (FolderCaseId, ChangedAt DESC);
            """;
        auditCommand.ExecuteNonQuery();

        BackfillFinalDecisionAt(connection);
    }

    /// <summary>Additive migration for databases created before a column existed — SQLite has no
    /// "ADD COLUMN IF NOT EXISTS", so PRAGMA table_info is checked first. Every column added this
    /// way is a nullable TEXT.</summary>
    private static void AddColumnIfMissing(SqliteConnection connection, string column)
    {
        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA table_info(FolderCase)";
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
        // The column name is one of the literals listed above, never caller text.
        alterCommand.CommandText = $"ALTER TABLE FolderCase ADD COLUMN {column} TEXT NULL";
        alterCommand.ExecuteNonQuery();
    }


    /// <summary>
    /// Fills the sort key for rows stored before it existed. Stripping accents is not something
    /// SQLite can do, so the rows are read and rewritten here — in one transaction, because the
    /// 2026 workbook alone is over 20.000 of them.
    /// </summary>
    private static void BackfillFullNameSort(SqliteConnection connection)
    {
        var pending = new List<(long Id, string FullName)>();

        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT Id, FullName FROM FolderCase WHERE FullNameSort IS NULL AND FullName IS NOT NULL";
            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                pending.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE FolderCase SET FullNameSort = $sort WHERE Id = $id";
        var sortParameter = updateCommand.Parameters.Add("$sort", SqliteType.Text);
        var idParameter = updateCommand.Parameters.Add("$id", SqliteType.Integer);

        foreach (var (id, fullName) in pending)
        {
            sortParameter.Value = TextNormalizer.Normalize(fullName);
            idParameter.Value = id;
            updateCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Fills <c>FinalDecisionAt</c> for cases decided before the column existed, from the last
    /// "Decisión final" audit entry that switched to Otorgado/Denegado. Cases with no such audit
    /// entry (imported straight from the workbook, never edited here) are left null — there is no
    /// exact moment to backfill, and the KPI screen already reports those as "sin fecha".
    /// </summary>
    private static void BackfillFinalDecisionAt(SqliteConnection connection)
    {
        var pending = new List<(long Id, string ChangedAt)>();

        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = """
                SELECT f.Id,
                       (SELECT a.ChangedAt FROM CaseAuditLog a
                        WHERE a.FolderCaseId = f.Id AND a.FieldName = 'Decisión final'
                          AND a.NewValue IN ('Otorgado', 'Denegado')
                        ORDER BY a.ChangedAt DESC, a.Id DESC LIMIT 1)
                FROM FolderCase f
                WHERE f.FinalDecisionAt IS NULL AND f.FinalDecision IN (0, 1)
                """;
            using var reader = selectCommand.ExecuteReader();
            while (reader.Read())
            {
                // ChangedAt is written as DateTimeOffset.UtcNow.ToString("O") (RecordAuditLog), but
                // the backfill re-parses and re-formats it rather than trusting that verbatim: any
                // row written by a future/older format, or hand-edited, is skipped instead of
                // storing a value FinalDecisionAt's own reader could later fail to parse.
                if (!reader.IsDBNull(1)
                    && DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out var changedAt))
                {
                    pending.Add((reader.GetInt64(0), changedAt.ToString("O")));
                }
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = "UPDATE FolderCase SET FinalDecisionAt = $at WHERE Id = $id";
        var atParameter = updateCommand.Parameters.Add("$at", SqliteType.Text);
        var idParameter = updateCommand.Parameters.Add("$id", SqliteType.Integer);

        foreach (var (id, changedAt) in pending)
        {
            atParameter.Value = changedAt;
            idParameter.Value = id;
            updateCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Re-importing the same workbook must not duplicate anyone. A row is the same case when the
    /// person, citation date and office match; when the RUT or the date is unreadable, the workbook
    /// cell it came from (sheet + row) is used as identity instead.
    /// </summary>
    public UpsertOutcome Upsert(FolderCase folderCase)
    {
        var existingId = FindExistingId(folderCase);
        if (existingId is null)
        {
            Insert(folderCase);
            return UpsertOutcome.Inserted;
        }

        using (var connection = Open())
        {
            Update(existingId.Value, folderCase, connection);
        }
        return UpsertOutcome.Updated;
    }

    /// <summary>
    /// Una conexión y una transacción para todo el lote. SQLite confirma cada escritura suelta en
    /// disco; agrupadas, la importación completa del libro baja de minutos a segundos. Si algo
    /// falla a mitad de camino, no queda media hoja importada.
    /// </summary>
    public IReadOnlyList<UpsertOutcome> UpsertMany(IReadOnlyList<FolderCase> folderCases)
    {
        var outcomes = new List<UpsertOutcome>(folderCases.Count);
        if (folderCases.Count == 0)
        {
            return outcomes;
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        foreach (var folderCase in folderCases)
        {
            var existingId = FindExistingId(folderCase, connection);
            if (existingId is null)
            {
                Insert(folderCase, connection);
                outcomes.Add(UpsertOutcome.Inserted);
            }
            else
            {
                Update(existingId.Value, folderCase, connection);
                outcomes.Add(UpsertOutcome.Updated);
            }
        }

        transaction.Commit();
        return outcomes;
    }

    private long? FindExistingId(FolderCase folderCase)
    {
        using var connection = Open();
        return FindExistingId(folderCase, connection);
    }

    private static long? FindExistingId(FolderCase folderCase, SqliteConnection connection)
    {
        if (folderCase.Rut is not null && folderCase.CitationDate is not null)
        {
            using var byNaturalKey = connection.CreateCommand();
            byNaturalKey.CommandText = """
                SELECT Id FROM FolderCase
                WHERE Office = $office AND CitationDate = $citationDate AND Rut = $rut
                LIMIT 1
                """;
            byNaturalKey.Parameters.AddWithValue("$office", (int)folderCase.Office);
            byNaturalKey.Parameters.AddWithValue("$citationDate", Text(folderCase.CitationDate));
            byNaturalKey.Parameters.AddWithValue("$rut", folderCase.Rut);
            if (byNaturalKey.ExecuteScalar() is long id)
            {
                return id;
            }
        }

        if (folderCase.SourceSheet is null)
        {
            return null;
        }

        using var bySource = connection.CreateCommand();
        bySource.CommandText = "SELECT Id FROM FolderCase WHERE SourceSheet = $sheet AND SourceRow = $row LIMIT 1";
        bySource.Parameters.AddWithValue("$sheet", folderCase.SourceSheet);
        bySource.Parameters.AddWithValue("$row", folderCase.SourceRow);
        return bySource.ExecuteScalar() as long?;
    }

    public long Insert(FolderCase folderCase)
    {
        using var connection = Open();
        return Insert(folderCase, connection);
    }

    private static long Insert(FolderCase folderCase, SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FolderCase (
                CitationDate, FolderUploadedDate, LastFolderDate, LastFolderComuna,
                FirstName, LastName, FullName, FullNameSort, Rut, Office, AttentionNote, Attended,
                MoralIdoneity, FolderState, FolderStateRaw, FinalDecision, FinalDecisionRaw,
                SourceSheet, SourceRow, NeedsReview, Marked, CreatedAt, UpdatedAt,
                PenultimateFolderDate, CodigoF8, LicenceClasses, Email, CellPhone, FolioLicencia,
                CambioDomicilioComuna, FinalDecisionAt)
            VALUES (
                $citationDate, $uploadedDate, $lastFolderDate, $lastFolderComuna,
                $firstName, $lastName, $fullName, $fullNameSort, $rut, $office, $attention, $attended,
                $idoneity, $state, $stateRaw, $decision, $decisionRaw,
                $sheet, $row, $needsReview, $marked, $createdAt, $updatedAt,
                $penultimate, $codigoF8, $licences, $email, $cellPhone, $folio,
                $cambioDomicilioComuna, $decisionAt);
            SELECT last_insert_rowid();
            """;
        BindWritableFields(command, folderCase);
        command.Parameters.AddWithValue("$firstName", Nullable(folderCase.FirstName));
        command.Parameters.AddWithValue("$lastName", Nullable(folderCase.LastName));
        command.Parameters.AddWithValue("$sheet", Nullable(folderCase.SourceSheet));
        command.Parameters.AddWithValue("$row", folderCase.SourceRow);
        command.Parameters.AddWithValue("$marked", folderCase.Marked ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", folderCase.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", folderCase.UpdatedAt.ToString("O"));
        // Only on insert: an import must never overwrite these, since the master workbook has no
        // column for any of them (same reasoning as Marked).
        command.Parameters.AddWithValue("$penultimate", Nullable(Text(folderCase.PenultimateFolderDate)));
        command.Parameters.AddWithValue("$codigoF8", Nullable(folderCase.CodigoF8));
        command.Parameters.AddWithValue("$licences", Nullable(folderCase.LicenceClasses));
        command.Parameters.AddWithValue("$email", Nullable(folderCase.Email));
        command.Parameters.AddWithValue("$cellPhone", Nullable(folderCase.CellPhone));
        command.Parameters.AddWithValue("$folio", Nullable(folderCase.FolioLicencia));
        command.Parameters.AddWithValue("$cambioDomicilioComuna", Nullable(folderCase.CambioDomicilioComuna));
        // A brand-new row has no "previous" decision: null -> decided counts as a change, same rule
        // as any other transition into Otorgado/Denegado.
        command.Parameters.AddWithValue("$decisionAt", Nullable(ComputeFinalDecisionAtText(null, null, folderCase.FinalDecision, DateTimeOffset.UtcNow)));

        return (long)command.ExecuteScalar()!;
    }

    private static void Update(long id, FolderCase folderCase, SqliteConnection connection)
    {
        var (previousDecision, previousDecisionAt) = SelectCurrentDecision(id, connection);

        using var command = connection.CreateCommand();
        // Marked is the operator's own bookkeeping and is deliberately left untouched by an import.
        command.CommandText = """
            UPDATE FolderCase SET
                CitationDate = $citationDate,
                FolderUploadedDate = $uploadedDate,
                LastFolderDate = $lastFolderDate,
                LastFolderComuna = $lastFolderComuna,
                FirstName = $firstName,
                LastName = $lastName,
                FullName = $fullName,
                FullNameSort = $fullNameSort,
                Rut = $rut,
                Office = $office,
                AttentionNote = $attention,
                Attended = $attended,
                MoralIdoneity = $idoneity,
                FolderState = $state,
                FolderStateRaw = $stateRaw,
                FinalDecision = $decision,
                FinalDecisionRaw = $decisionRaw,
                FinalDecisionAt = $decisionAt,
                SourceSheet = $sheet,
                SourceRow = $row,
                NeedsReview = $needsReview,
                UpdatedAt = $updatedAt,
                Email = COALESCE($email, Email),
                CellPhone = COALESCE($cellPhone, CellPhone)
            WHERE Id = $id
            """;
        BindWritableFields(command, folderCase);
        command.Parameters.AddWithValue("$firstName", Nullable(folderCase.FirstName));
        command.Parameters.AddWithValue("$lastName", Nullable(folderCase.LastName));
        command.Parameters.AddWithValue("$sheet", Nullable(folderCase.SourceSheet));
        command.Parameters.AddWithValue("$row", folderCase.SourceRow);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        // COALESCE keeps whatever the master workbook import (no email/phone columns) already had —
        // only a citas import, which does carry these, overwrites them.
        command.Parameters.AddWithValue("$email", Nullable(folderCase.Email));
        command.Parameters.AddWithValue("$cellPhone", Nullable(folderCase.CellPhone));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$decisionAt", Nullable(ComputeFinalDecisionAtText(previousDecision, previousDecisionAt, folderCase.FinalDecision, DateTimeOffset.UtcNow)));
        command.ExecuteNonQuery();
    }

    /// <summary>Current <c>FinalDecision</c>/<c>FinalDecisionAt</c> for a row, read before an
    /// import overwrites it — the only way to tell whether the decision actually changed.</summary>
    private static (FinalDecision? Decision, string? At) SelectCurrentDecision(long id, SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FinalDecision, FinalDecisionAt FROM FolderCase WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return (null, null);
        }

        var decision = reader.IsDBNull(0) ? (FinalDecision?)null : (FinalDecision)reader.GetInt32(0);
        var at = reader.IsDBNull(1) ? null : reader.GetString(1);
        return (decision, at);
    }

    /// <summary>
    /// The single rule behind <c>FinalDecisionAt</c>: it moves to "now" the moment the decision
    /// becomes Otorgado/Denegado, clears when it moves away from either, and is left untouched when
    /// the decision does not change at all — regardless of which of the three write paths triggered
    /// it (edit screen, single upsert, batch import).
    /// </summary>
    private static string? ComputeFinalDecisionAtText(FinalDecision? previous, string? previousAtText, FinalDecision? next, DateTimeOffset now)
    {
        if (previous == next)
        {
            return previousAtText;
        }

        var nextIsDecided = next is FinalDecision.Otorgado or FinalDecision.Denegado;
        return nextIsDecided ? now.ToString("O") : null;
    }

    private static void BindWritableFields(SqliteCommand command, FolderCase folderCase)
    {
        command.Parameters.AddWithValue("$citationDate", Nullable(Text(folderCase.CitationDate)));
        command.Parameters.AddWithValue("$uploadedDate", Nullable(Text(folderCase.FolderUploadedDate)));
        command.Parameters.AddWithValue("$lastFolderDate", Nullable(Text(folderCase.LastFolderDate)));
        command.Parameters.AddWithValue("$lastFolderComuna", Nullable(folderCase.LastFolderComuna));
        command.Parameters.AddWithValue("$fullName", Nullable(folderCase.FullName));
        command.Parameters.AddWithValue("$fullNameSort", Nullable(SortKey(folderCase.FullName)));
        command.Parameters.AddWithValue("$rut", Nullable(folderCase.Rut));
        command.Parameters.AddWithValue("$office", (int)folderCase.Office);
        command.Parameters.AddWithValue("$attention", Nullable(folderCase.AttentionNote));
        command.Parameters.AddWithValue("$attended", folderCase.Attended ? 1 : 0);
        command.Parameters.AddWithValue("$idoneity", folderCase.MoralIdoneity is { } idoneity ? (int)idoneity : DBNull.Value);
        command.Parameters.AddWithValue("$state", folderCase.FolderState is { } state ? (int)state : DBNull.Value);
        command.Parameters.AddWithValue("$stateRaw", Nullable(folderCase.FolderStateRaw));
        command.Parameters.AddWithValue("$decision", folderCase.FinalDecision is { } decision ? (int)decision : DBNull.Value);
        command.Parameters.AddWithValue("$decisionRaw", Nullable(folderCase.FinalDecisionRaw));
        command.Parameters.AddWithValue("$needsReview", folderCase.NeedsReview ? 1 : 0);
    }

    public FolderCase? FindById(long id)
    {
        using var connection = Open();
        return FindByIdInternal(connection, null, id);
    }

    private static FolderCase? FindByIdInternal(SqliteConnection connection, SqliteTransaction? transaction, long id)
    {
        using var command = connection.CreateCommand();
        if (transaction is not null) command.Transaction = transaction;
        command.CommandText = "SELECT * FROM FolderCase WHERE Id = $id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<FolderCase> Query(CaseFilter filter, int skip, int take)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = BuildWhere(filter, command);
        command.CommandText = $"""
            SELECT * FROM FolderCase
            {where}
            ORDER BY {OrderBy(filter)}
            LIMIT $take OFFSET $skip
            """;
        command.Parameters.AddWithValue("$take", take);
        command.Parameters.AddWithValue("$skip", skip);

        var results = new List<FolderCase>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    /// <summary>Every case matching the filter, unpaged — used by the Excel export, where handing
    /// back a silently truncated file would be worse than a slow one.</summary>
    public IReadOnlyList<FolderCase> QueryAll(CaseFilter filter)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = BuildWhere(filter, command);
        command.CommandText = $"""
            SELECT * FROM FolderCase
            {where}
            ORDER BY {OrderBy(filter)}
            """;

        var results = new List<FolderCase>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public int Count(CaseFilter filter)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = BuildWhere(filter, command);
        command.CommandText = $"SELECT COUNT(*) FROM FolderCase {where}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IReadOnlyList<string> DuplicateRuts(CaseFilter filter)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var where = BuildWhere(filter, command);
        command.CommandText = $"""
            SELECT Rut FROM FolderCase
            {where} AND Rut IS NOT NULL AND Rut <> ''
            GROUP BY Rut
            HAVING COUNT(*) > 1
            """;

        var duplicates = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            duplicates.Add(reader.GetString(0));
        }
        return duplicates;
    }

    public int CountNeedingReview(IReadOnlyCollection<Office>? allowedOffices = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM FolderCase WHERE NeedsReview = 1 AND DeletedAt IS NULL {OfficeClause(allowedOffices, command)}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int CountDeleted(IReadOnlyCollection<Office>? allowedOffices = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM FolderCase WHERE DeletedAt IS NOT NULL {OfficeClause(allowedOffices, command)}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Everything currently in the bin, most recently deleted first.</summary>
    public IReadOnlyList<FolderCase> Deleted(IReadOnlyCollection<Office>? allowedOffices = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT * FROM FolderCase
            WHERE DeletedAt IS NOT NULL {OfficeClause(allowedOffices, command)}
            ORDER BY DeletedAt DESC
            LIMIT 500
            """;

        var results = new List<FolderCase>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public IReadOnlyList<int> DistinctYears()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT substr(CitationDate, 1, 4) AS Year
            FROM FolderCase WHERE CitationDate IS NOT NULL AND DeletedAt IS NULL
            ORDER BY Year DESC
            """;
        var years = new List<int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (int.TryParse(reader.GetString(0), out var year))
            {
                years.Add(year);
            }
        }
        return years;
    }

    /// <summary>Cases whose physical folder has to be pulled from a given sector, for the printable
    /// list. Folders already requested are left out unless explicitly asked for.</summary>
    public IReadOnlyList<FolderCase> ForSector(FolderSector sector, bool onlyMarked, bool includePrinted = false,
        DateOnly? citationDay = null, int? year = null, int? month = null, IReadOnlyCollection<Office>? allowedOffices = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var cutoff = new DateOnly(2023, 7, 1).ToString("yyyy-MM-dd");
        var sectorClause = sector == FolderSector.Archivo
            ? "LastFolderDate < $cutoff"
            : "LastFolderDate >= $cutoff";
        var markedClause = onlyMarked ? "AND Marked = 1" : string.Empty;
        var printedClause = includePrinted ? string.Empty : "AND SectorPrintedAt IS NULL";

        // Un día es más específico que un mes: si vienen los dos, manda el día.
        var periodClause = string.Empty;
        if (citationDay is { } day)
        {
            periodClause = "AND CitationDate = $day";
            command.Parameters.AddWithValue("$day", day.ToString("yyyy-MM-dd"));
        }
        else if (year is { } yearValue)
        {
            periodClause = "AND substr(CitationDate, 1, 4) = $year";
            command.Parameters.AddWithValue("$year", yearValue.ToString("D4"));

            if (month is { } monthValue)
            {
                periodClause += " AND substr(CitationDate, 6, 2) = $month";
                command.Parameters.AddWithValue("$month", monthValue.ToString("D2"));
            }
        }

        command.CommandText = $"""
            SELECT * FROM FolderCase
            WHERE DeletedAt IS NULL AND LastFolderDate IS NOT NULL
              AND {sectorClause} {markedClause} {printedClause} {periodClause} {OfficeClause(allowedOffices, command)}
            ORDER BY CitationDate DESC, FullNameSort COLLATE NOCASE ASC
            LIMIT {SectorListLimit}
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff);

        var results = new List<FolderCase>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Map(reader));
        }
        return results;
    }

    /// <summary>Scheduled vs attended people per day and office — the agenda half of the statistics screen.</summary>
    public IReadOnlyList<(DateOnly Date, Office Office, int Scheduled, int Attended)> DailyAttendance(int year, int month, IReadOnlyCollection<Office>? allowedOffices = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT CitationDate, Office, COUNT(*) AS Scheduled, SUM(Attended) AS Attended
            FROM FolderCase
            WHERE DeletedAt IS NULL AND CitationDate IS NOT NULL
              AND substr(CitationDate, 1, 4) = $year
              AND substr(CitationDate, 6, 2) = $month {OfficeClause(allowedOffices, command)}
            GROUP BY CitationDate, Office
            ORDER BY CitationDate ASC, Office ASC
            """;
        command.Parameters.AddWithValue("$year", year.ToString("D4"));
        command.Parameters.AddWithValue("$month", month.ToString("D2"));

        var results = new List<(DateOnly, Office, int, int)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                DateOnly.Parse(reader.GetString(0)),
                (Office)reader.GetInt32(1),
                Convert.ToInt32(reader.GetValue(2)),
                reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3))));
        }
        return results;
    }

    public IReadOnlyList<(FolderState? State, int Count)> FolderStateBreakdown(int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices = null)
        => Breakdown("FolderState", year, month, office, allowedOffices)
            .Select(entry => (entry.Value is { } value ? (FolderState)value : (FolderState?)null, entry.Count))
            .ToList();

    public IReadOnlyList<(FinalDecision? Decision, int Count)> FinalDecisionBreakdown(int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices = null)
        => Breakdown("FinalDecision", year, month, office, allowedOffices)
            .Select(entry => (entry.Value is { } value ? (FinalDecision)value : (FinalDecision?)null, entry.Count))
            .ToList();

    /// <summary>
    /// Las clases viven en un texto ("B,C"), así que el conteo se arma acá y no en SQL: un caso con
    /// dos clases suma en las dos. La pregunta que responde es cuántas licencias se tramitan.
    /// </summary>
    public IReadOnlyList<(LicenceClass Licence, int Count)> LicenceClassBreakdown(int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var monthClause = month is null ? string.Empty : "AND substr(CitationDate, 6, 2) = $month";
        var officeClause = office is null ? string.Empty : "AND Office = $office";
        command.CommandText = $"""
            SELECT LicenceClasses FROM FolderCase
            WHERE DeletedAt IS NULL AND LicenceClasses IS NOT NULL AND CitationDate IS NOT NULL
              AND substr(CitationDate, 1, 4) = $year {monthClause} {officeClause} {OfficeClause(allowedOffices, command)}
            """;
        command.Parameters.AddWithValue("$year", year.ToString("D4"));
        if (month is { } monthValue)
        {
            command.Parameters.AddWithValue("$month", monthValue.ToString("D2"));
        }
        if (office is { } officeValue)
        {
            command.Parameters.AddWithValue("$office", (int)officeValue);
        }

        var counts = new Dictionary<LicenceClass, int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            foreach (var licence in LicenceClassCatalog.Parse(reader.GetString(0)))
            {
                counts[licence] = counts.GetValueOrDefault(licence) + 1;
            }
        }

        return [.. counts.OrderBy(entry => entry.Key).Select(entry => (entry.Key, entry.Value))];
    }

    /// <summary>Counts per catalog value. The column name is never caller-supplied text — only the two
    /// literals below reach it — so it cannot carry SQL injection.</summary>
    private List<(int? Value, int Count)> Breakdown(string column, int year, int? month, Office? office, IReadOnlyCollection<Office>? allowedOffices)
    {
        if (column is not ("FolderState" or "FinalDecision"))
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Unsupported breakdown column.");
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        var monthClause = month is null ? string.Empty : "AND substr(CitationDate, 6, 2) = $month";
        var officeClause = office is null ? string.Empty : "AND Office = $office";
        command.CommandText = $"""
            SELECT {column} AS Value, COUNT(*) AS Total
            FROM FolderCase
            WHERE DeletedAt IS NULL AND CitationDate IS NOT NULL
              AND substr(CitationDate, 1, 4) = $year {monthClause} {officeClause} {OfficeClause(allowedOffices, command)}
            GROUP BY {column}
            ORDER BY Total DESC
            """;
        command.Parameters.AddWithValue("$year", year.ToString("D4"));
        if (month is { } monthValue)
        {
            command.Parameters.AddWithValue("$month", monthValue.ToString("D2"));
        }
        if (office is { } officeValue)
        {
            command.Parameters.AddWithValue("$office", (int)officeValue);
        }

        var results = new List<(int?, int)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                Convert.ToInt32(reader.GetValue(1))));
        }
        return results;
    }

    public void UpdateEditableFields(long id, string? fullName, string? rut, DateOnly? citationDate,
        DateOnly? folderUploadedDate, DateOnly? lastFolderDate, string? lastFolderComuna,
        FolderState? folderState, FinalDecision? finalDecision, MoralIdoneity? moralIdoneity,
        string? attentionNote, bool needsReview, string? editedBy = null, string? cambioDomicilioComuna = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        // Read regardless of editedBy: FinalDecisionAt's "did it change" rule does not depend on
        // whether the edit is attributed to anyone.
        var (previousDecision, previousDecisionAt) = SelectCurrentDecision(id, connection);

        if (!string.IsNullOrWhiteSpace(editedBy))
        {
            var current = FindByIdInternal(connection, transaction, id);
            if (current is not null)
            {
                RecordAuditLog(connection, transaction, id, editedBy, "Nombre", current.FullName, fullName);
                RecordAuditLog(connection, transaction, id, editedBy, "RUT", current.Rut, rut);
                RecordAuditLog(connection, transaction, id, editedBy, "Fecha citación", current.CitationDate?.ToString("yyyy-MM-dd"), citationDate?.ToString("yyyy-MM-dd"));
                RecordAuditLog(connection, transaction, id, editedBy, "Fecha subida", current.FolderUploadedDate?.ToString("yyyy-MM-dd"), folderUploadedDate?.ToString("yyyy-MM-dd"));
                RecordAuditLog(connection, transaction, id, editedBy, "Fecha última carpeta", current.LastFolderDate?.ToString("yyyy-MM-dd"), lastFolderDate?.ToString("yyyy-MM-dd"));
                RecordAuditLog(connection, transaction, id, editedBy, "Comuna última carpeta", current.LastFolderComuna, lastFolderComuna);
                RecordAuditLog(connection, transaction, id, editedBy, "Estado carpeta", current.FolderState?.ToString(), folderState?.ToString());
                RecordAuditLog(connection, transaction, id, editedBy, "Decisión final", current.FinalDecision?.ToString(), finalDecision?.ToString());
                RecordAuditLog(connection, transaction, id, editedBy, "Idoneidad moral", current.MoralIdoneity?.ToString(), moralIdoneity?.ToString());
                RecordAuditLog(connection, transaction, id, editedBy, "Atención", current.AttentionNote, attentionNote);
                RecordAuditLog(connection, transaction, id, editedBy, "Comuna cambio domicilio", current.CambioDomicilioComuna, cambioDomicilioComuna);
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE FolderCase SET
                FullName = $fullName,
                FullNameSort = $fullNameSort,
                Rut = $rut,
                CitationDate = $citationDate,
                FolderUploadedDate = $uploadedDate,
                LastFolderDate = $lastFolderDate,
                LastFolderComuna = $lastFolderComuna,
                FolderState = $state,
                FolderStateRaw = NULL,
                FinalDecision = $decision,
                FinalDecisionRaw = NULL,
                FinalDecisionAt = $decisionAt,
                MoralIdoneity = $idoneity,
                AttentionNote = $attention,
                NeedsReview = $needsReview,
                UpdatedAt = $updatedAt,
                UpdatedBy = COALESCE($editedBy, UpdatedBy),
                CambioDomicilioComuna = $cambioDomicilioComuna
            WHERE Id = $id
            """;
        // Attended queda fuera a propósito: lo maneja su propia casilla (SetAttended). Derivarlo
        // aquí del texto de ATENCIÓN hacía que guardar una fila con ese campo vacío marcara a la
        // persona como ausente sin que nadie lo pidiera.
        command.Parameters.AddWithValue("$fullName", Nullable(fullName));
        command.Parameters.AddWithValue("$fullNameSort", Nullable(SortKey(fullName)));
        command.Parameters.AddWithValue("$rut", Nullable(rut));
        command.Parameters.AddWithValue("$citationDate", Nullable(Text(citationDate)));
        command.Parameters.AddWithValue("$uploadedDate", Nullable(Text(folderUploadedDate)));
        command.Parameters.AddWithValue("$lastFolderDate", Nullable(Text(lastFolderDate)));
        command.Parameters.AddWithValue("$lastFolderComuna", Nullable(lastFolderComuna));
        command.Parameters.AddWithValue("$state", folderState is { } state ? (int)state : DBNull.Value);
        command.Parameters.AddWithValue("$decision", finalDecision is { } decision ? (int)decision : DBNull.Value);
        command.Parameters.AddWithValue("$decisionAt", Nullable(ComputeFinalDecisionAtText(previousDecision, previousDecisionAt, finalDecision, DateTimeOffset.UtcNow)));
        command.Parameters.AddWithValue("$idoneity", moralIdoneity is { } idoneity ? (int)idoneity : DBNull.Value);
        command.Parameters.AddWithValue("$attention", Nullable(attentionNote));
        command.Parameters.AddWithValue("$needsReview", needsReview ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$editedBy", Nullable(editedBy));
        command.Parameters.AddWithValue("$cambioDomicilioComuna", Nullable(cambioDomicilioComuna));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public void UpdateCaseDetails(long id, string? codigoF8, DateOnly? penultimateFolderDate,
        string? licenceClasses = null, string? folioLicencia = null, string? editedBy = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        if (!string.IsNullOrWhiteSpace(editedBy))
        {
            var current = FindByIdInternal(connection, transaction, id);
            if (current is not null)
            {
                RecordAuditLog(connection, transaction, id, editedBy, "Código F8", current.CodigoF8, codigoF8);
                RecordAuditLog(connection, transaction, id, editedBy, "Penúltima carpeta", current.PenultimateFolderDate?.ToString("yyyy-MM-dd"), penultimateFolderDate?.ToString("yyyy-MM-dd"));
                RecordAuditLog(connection, transaction, id, editedBy, "Clases licencia", current.LicenceClasses, licenceClasses);
                RecordAuditLog(connection, transaction, id, editedBy, "Folio licencia", current.FolioLicencia, folioLicencia);
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE FolderCase
            SET CodigoF8 = $codigo, PenultimateFolderDate = $penultimate,
                LicenceClasses = $licences, FolioLicencia = $folio, UpdatedAt = $updatedAt,
                UpdatedBy = COALESCE($editedBy, UpdatedBy)
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$licences", Nullable(licenceClasses));
        command.Parameters.AddWithValue("$codigo", Nullable(string.IsNullOrWhiteSpace(codigoF8) ? null : codigoF8.Trim()));
        command.Parameters.AddWithValue("$penultimate", Nullable(Text(penultimateFolderDate)));
        command.Parameters.AddWithValue("$folio", Nullable(string.IsNullOrWhiteSpace(folioLicencia) ? null : folioLicencia.Trim()));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$editedBy", Nullable(editedBy));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public void UpdateObservations(long id, string? observations, string? editedBy = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        var trimmed = string.IsNullOrWhiteSpace(observations) ? null : observations.Trim();
        if (!string.IsNullOrWhiteSpace(editedBy))
        {
            var current = FindByIdInternal(connection, transaction, id);
            if (current is not null)
            {
                RecordAuditLog(connection, transaction, id, editedBy, "Observaciones", current.Observations, trimmed);
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE FolderCase
            SET Observations = $observations, UpdatedAt = $updatedAt,
                UpdatedBy = COALESCE($editedBy, UpdatedBy)
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$observations", Nullable(trimmed));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$editedBy", Nullable(editedBy));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public IReadOnlyList<CaseAuditEntry> GetAuditLog(long caseId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, FolderCaseId, ChangedBy, ChangedAt, FieldName, OldValue, NewValue
            FROM CaseAuditLog
            WHERE FolderCaseId = $caseId
            ORDER BY ChangedAt DESC, Id DESC
            """;
        command.Parameters.AddWithValue("$caseId", caseId);

        var entries = new List<CaseAuditEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new CaseAuditEntry
            {
                Id = reader.GetInt64(0),
                FolderCaseId = reader.GetInt64(1),
                ChangedBy = reader.GetString(2),
                ChangedAt = DateTimeOffset.Parse(reader.GetString(3)),
                FieldName = reader.GetString(4),
                OldValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                NewValue = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        return entries;
    }

    public IReadOnlyList<CaseAuditEntry> GetAuditLogsForPeriod(DateTimeOffset start, DateTimeOffset end, IReadOnlyCollection<Office>? allowedOffices = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        var officeClause = allowedOffices is null
            ? string.Empty
            : $"AND FolderCaseId IN (SELECT Id FROM FolderCase WHERE 1 = 1 {OfficeClause(allowedOffices, command)})";
        command.CommandText = $"""
            SELECT Id, FolderCaseId, ChangedBy, ChangedAt, FieldName, OldValue, NewValue
            FROM CaseAuditLog
            WHERE ChangedAt >= $start AND ChangedAt < $end {officeClause}
            ORDER BY ChangedAt ASC, Id ASC
            """;
        command.Parameters.AddWithValue("$start", start.ToString("O"));
        command.Parameters.AddWithValue("$end", end.ToString("O"));

        var entries = new List<CaseAuditEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new CaseAuditEntry
            {
                Id = reader.GetInt64(0),
                FolderCaseId = reader.GetInt64(1),
                ChangedBy = reader.GetString(2),
                ChangedAt = DateTimeOffset.Parse(reader.GetString(3)),
                FieldName = reader.GetString(4),
                OldValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                NewValue = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        return entries;
    }

    private static void RecordAuditLog(SqliteConnection connection, SqliteTransaction? transaction,
        long folderCaseId, string changedBy, string fieldName, string? oldValue, string? newValue)
    {
        var oldNormalized = string.IsNullOrWhiteSpace(oldValue) ? null : oldValue.Trim();
        var newNormalized = string.IsNullOrWhiteSpace(newValue) ? null : newValue.Trim();

        if (string.Equals(oldNormalized, newNormalized, StringComparison.Ordinal))
        {
            return;
        }

        using var command = connection.CreateCommand();
        if (transaction is not null) command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CaseAuditLog (FolderCaseId, ChangedBy, ChangedAt, FieldName, OldValue, NewValue)
            VALUES ($caseId, $changedBy, $changedAt, $fieldName, $oldValue, $newValue)
            """;
        command.Parameters.AddWithValue("$caseId", folderCaseId);
        command.Parameters.AddWithValue("$changedBy", changedBy);
        command.Parameters.AddWithValue("$changedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$fieldName", fieldName);
        command.Parameters.AddWithValue("$oldValue", (object?)oldNormalized ?? DBNull.Value);
        command.Parameters.AddWithValue("$newValue", (object?)newNormalized ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void MarkSectorPrinted(IReadOnlyCollection<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET SectorPrintedAt = $printedAt WHERE Id = $id";
        var printedAt = command.Parameters.Add("$printedAt", SqliteType.Text);
        var idParameter = command.Parameters.Add("$id", SqliteType.Integer);
        printedAt.Value = DateTimeOffset.UtcNow.ToString("O");

        foreach (var id in ids)
        {
            idParameter.Value = id;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void ClearSectorPrinted(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET SectorPrintedAt = NULL WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void SetAttended(long id, bool attended, string? editedBy = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        if (!string.IsNullOrWhiteSpace(editedBy))
        {
            var current = FindByIdInternal(connection, transaction, id);
            if (current is not null && current.Attended != attended)
            {
                RecordAuditLog(connection, transaction, id, editedBy, "Asistencia", current.Attended ? "Presente" : "Ausente", attended ? "Presente" : "Ausente");
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE FolderCase
            SET Attended = $attended, UpdatedAt = $updatedAt,
                UpdatedBy = COALESCE($editedBy, UpdatedBy)
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$attended", attended ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$editedBy", Nullable(editedBy));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public void SetMarked(long id, bool marked)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET Marked = $marked, UpdatedAt = $updatedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$marked", marked ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET DeletedAt = $deletedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$deletedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void Restore(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET DeletedAt = NULL WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Wipes the row. Only reachable from the bin, where the case is already out of sight.</summary>
    public void DeletePermanently(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FolderCase WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public int DeleteAllPermanently()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FolderCase";
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// Builds the ORDER BY from the closed <see cref="CaseSort"/> set — no caller text ever reaches
    /// the SQL. Id breaks ties so paging can't show a row twice or skip one, and every column falls
    /// back to the name so equal dates read alphabetically.
    /// </summary>
    private static string OrderBy(CaseFilter filter)
    {
        // Dates read newest-first by default (that is how the agenda is consulted); text reads A-Z.
        var (column, descendingByDefault) = filter.Sort switch
        {
            // FullNameSort, not FullName: SQLite compares bytes, so ÁLVARO and MUÑOZ would land
            // after Z on the raw text.
            CaseSort.Name => ("FullNameSort COLLATE NOCASE", false),
            CaseSort.Rut => ("Rut", false),
            CaseSort.Office => ("Office", false),
            CaseSort.FolderUploadedDate => ("FolderUploadedDate", true),
            CaseSort.LastFolderDate => ("LastFolderDate", false),
            CaseSort.FolderState => ("FolderState", false),
            CaseSort.FinalDecision => ("FinalDecision", false),
            _ => ("CitationDate", true)
        };

        var direction = filter.Descending != descendingByDefault ? "DESC" : "ASC";

        // En la vista por defecto, lo ya subido a Conaset es trabajo terminado: baja al final de la
        // lista y entre ellos van en orden de subida. Si el operador pide una columna concreta,
        // manda esa columna y no se altera nada.
        if (filter.Sort == CaseSort.CitationDate)
        {
            var conaset = (int)Domain.FolderState.SubidaAConaset;
            return $"""
                CASE WHEN FolderState = {conaset} THEN 1 ELSE 0 END ASC,
                CASE WHEN FolderState = {conaset} THEN FolderUploadedDate END ASC,
                {column} {direction}, FullNameSort COLLATE NOCASE ASC, Id ASC
                """;
        }

        return $"{column} {direction}, FullNameSort COLLATE NOCASE ASC, Id ASC";
    }

    /// <summary>Accent-free, upper-cased copy of a name, for ordering only — never shown.</summary>
    private static string? SortKey(string? fullName)
        => fullName is null ? null : TextNormalizer.Normalize(fullName);

    private static string BuildWhere(CaseFilter filter, SqliteCommand command)
    {
        // Cases in the bin never show up in a listing, a count or an export — only in /Papelera.
        var clauses = new List<string> { "DeletedAt IS NULL" };

        if (filter.Office is { } office)
        {
            clauses.Add("Office = $office");
            command.Parameters.AddWithValue("$office", (int)office);
        }

        if (OfficeClause(filter.AllowedOffices, command) is { Length: > 0 } allowedClause)
        {
            clauses.Add(allowedClause["AND ".Length..]);
        }

        if (filter.Year is { } year)
        {
            clauses.Add("substr(CitationDate, 1, 4) = $year");
            command.Parameters.AddWithValue("$year", year.ToString("D4"));
        }

        if (filter.Month is { } month)
        {
            clauses.Add("substr(CitationDate, 6, 2) = $month");
            command.Parameters.AddWithValue("$month", month.ToString("D2"));
        }

        if (filter.CitationDay is { } citationDay)
        {
            clauses.Add("CitationDate = $citationDay");
            command.Parameters.AddWithValue("$citationDay", citationDay.ToString("yyyy-MM-dd"));
        }

        if (filter.FolderState is { } state)
        {
            clauses.Add("FolderState = $state");
            command.Parameters.AddWithValue("$state", (int)state);
        }

        if (filter.FinalDecision is { } decision)
        {
            clauses.Add("FinalDecision = $decision");
            command.Parameters.AddWithValue("$decision", (int)decision);
        }

        if (filter.Sector is { } sector)
        {
            var cutoff = new DateOnly(2023, 7, 1).ToString("yyyy-MM-dd");
            clauses.Add(sector == FolderSector.Archivo
                ? "(LastFolderDate IS NOT NULL AND LastFolderDate < $cutoff)"
                : "(LastFolderDate IS NOT NULL AND LastFolderDate >= $cutoff)");
            command.Parameters.AddWithValue("$cutoff", cutoff);
        }

        if (filter.OnlyNeedsReview)
        {
            clauses.Add("NeedsReview = 1");
        }

        if (filter.OnlyOtherComuna)
        {
            clauses.Add("LastFolderComuna IS NOT NULL");
        }

        if (filter.OnlyOverdue)
        {
            var fifteenDaysAgo = DateOnly.FromDateTime(DateTime.Today.AddDays(-15)).ToString("yyyy-MM-dd");
            clauses.Add("""
                (CitationDate <= $overdueCutoff
                 AND FolderUploadedDate IS NULL
                 AND (FolderState IS NULL OR FolderState NOT IN (0, 1, 2, 3, 4, 5, 11)))
                """);
            command.Parameters.AddWithValue("$overdueCutoff", fifteenDaysAgo);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // RUTs are searched with and without dots so "135516122" finds "13.551.612-2".
            clauses.Add("(FullName LIKE $search COLLATE NOCASE OR replace(replace(Rut, '.', ''), '-', '') LIKE $rutSearch)");
            command.Parameters.AddWithValue("$search", $"%{filter.Search.Trim()}%");
            command.Parameters.AddWithValue("$rutSearch",
                $"%{filter.Search.Trim().Replace(".", string.Empty).Replace("-", string.Empty)}%");
        }

        var builder = new StringBuilder("WHERE ");
        builder.AppendJoin(" AND ", clauses);
        return builder.ToString();
    }

    /// <summary>"AND Office IN (...)" for a per-office scope: empty for no restriction, and a
    /// clause that matches nothing for an empty scope (a user with no offices sees no cases).
    /// Only enum ordinals reach the SQL, as parameters.</summary>
    private static string OfficeClause(IReadOnlyCollection<Office>? allowedOffices, SqliteCommand command)
    {
        if (allowedOffices is null)
        {
            return string.Empty;
        }

        if (allowedOffices.Count == 0)
        {
            return "AND 1 = 0";
        }

        var names = new List<string>();
        foreach (var (office, index) in allowedOffices.Distinct().Select((office, index) => (office, index)))
        {
            names.Add($"$allowedOffice{index}");
            command.Parameters.AddWithValue($"$allowedOffice{index}", (int)office);
        }

        return $"AND Office IN ({string.Join(", ", names)})";
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string? Text(DateOnly? date) => date?.ToString("yyyy-MM-dd");

    private static object Nullable(string? value) => (object?)value ?? DBNull.Value;

    private static FolderCase Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        CitationDate = ReadDate(reader, "CitationDate"),
        FolderUploadedDate = ReadDate(reader, "FolderUploadedDate"),
        LastFolderDate = ReadDate(reader, "LastFolderDate"),
        LastFolderComuna = ReadText(reader, "LastFolderComuna"),
        FirstName = ReadText(reader, "FirstName"),
        LastName = ReadText(reader, "LastName"),
        FullName = ReadText(reader, "FullName"),
        Rut = ReadText(reader, "Rut"),
        Office = (Office)reader.GetInt32(reader.GetOrdinal("Office")),
        AttentionNote = ReadText(reader, "AttentionNote"),
        Attended = reader.GetInt32(reader.GetOrdinal("Attended")) == 1,
        MoralIdoneity = ReadEnum<MoralIdoneity>(reader, "MoralIdoneity"),
        FolderState = ReadEnum<FolderState>(reader, "FolderState"),
        FolderStateRaw = ReadText(reader, "FolderStateRaw"),
        FinalDecision = ReadEnum<FinalDecision>(reader, "FinalDecision"),
        FinalDecisionRaw = ReadText(reader, "FinalDecisionRaw"),
        FinalDecisionAt = ReadTimestamp(reader, "FinalDecisionAt"),
        SourceSheet = ReadText(reader, "SourceSheet"),
        SourceRow = reader.GetInt32(reader.GetOrdinal("SourceRow")),
        NeedsReview = reader.GetInt32(reader.GetOrdinal("NeedsReview")) == 1,
        Marked = reader.GetInt32(reader.GetOrdinal("Marked")) == 1,
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("UpdatedAt"))),
        DeletedAt = ReadText(reader, "DeletedAt") is { } deletedAt ? DateTimeOffset.Parse(deletedAt) : null,
        SectorPrintedAt = ReadText(reader, "SectorPrintedAt") is { } printedAt ? DateTimeOffset.Parse(printedAt) : null,
        PenultimateFolderDate = ReadDate(reader, "PenultimateFolderDate"),
        CodigoF8 = ReadText(reader, "CodigoF8"),
        FolioLicencia = ReadText(reader, "FolioLicencia"),
        UpdatedBy = ReadText(reader, "UpdatedBy"),
        LicenceClasses = ReadText(reader, "LicenceClasses"),
        Email = ReadText(reader, "Email"),
        CellPhone = ReadText(reader, "CellPhone"),
        Observations = ReadText(reader, "Observations"),
        CambioDomicilioComuna = ReadText(reader, "CambioDomicilioComuna")
    };

    private static string? ReadText(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateOnly? ReadDate(SqliteDataReader reader, string column)
    {
        var text = ReadText(reader, column);
        return text is null ? null : DateOnly.Parse(text);
    }

    private static TEnum? ReadEnum<TEnum>(SqliteDataReader reader, string column) where TEnum : struct, Enum
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (TEnum)(object)reader.GetInt32(ordinal);
    }

    /// <summary>Parses a stored ISO-8601 timestamp defensively: a malformed value (hand-edited row,
    /// data from before a format change) must never break a list — it is reported as "no date"
    /// instead of throwing.</summary>
    private static DateTimeOffset? ReadTimestamp(SqliteDataReader reader, string column)
    {
        var text = ReadText(reader, column);
        if (text is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
