using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

public class FolderCaseRepositoryTests
{
    private static FolderCase Case(
        string fullName = "JUAN PEREZ",
        string? rut = "13.025.150-1",
        int day = 2,
        Office office = Office.AvenidaArgentina,
        string sheet = "ENERO AV. ARGENTINA",
        int row = 3,
        FolderState? state = FolderState.SubidaAConaset,
        DateOnly? lastFolder = null,
        bool attended = true)
        => new()
        {
            FullName = fullName,
            Rut = rut,
            CitationDate = new DateOnly(2026, 1, day),
            Office = office,
            FolderState = state,
            LastFolderDate = lastFolder,
            Attended = attended,
            SourceSheet = sheet,
            SourceRow = row
        };

    [Fact]
    public void Inserts_and_reads_back_a_case()
    {
        using var db = new SqliteTestDatabase();

        var id = db.Cases.Insert(Case());
        var stored = db.Cases.FindById(id);

        Assert.NotNull(stored);
        Assert.Equal("JUAN PEREZ", stored.FullName);
        Assert.Equal(new DateOnly(2026, 1, 2), stored.CitationDate);
        Assert.Equal(FolderState.SubidaAConaset, stored.FolderState);
    }

    [Fact]
    public void Re_importing_the_same_person_updates_instead_of_duplicating()
    {
        using var db = new SqliteTestDatabase();

        Assert.Equal(UpsertOutcome.Inserted, db.Cases.Upsert(Case()));
        // Same person, same citation, same office — but the operator moved the row and changed the state.
        Assert.Equal(UpsertOutcome.Updated, db.Cases.Upsert(Case(row: 57, state: FolderState.SubidaConF8)));

        var all = db.Cases.Query(new CaseFilter(), 0, 50);
        Assert.Single(all);
        Assert.Equal(FolderState.SubidaConF8, all[0].FolderState);
    }

    [Fact]
    public void A_row_without_a_valid_rut_is_matched_by_its_workbook_cell()
    {
        using var db = new SqliteTestDatabase();

        Assert.Equal(UpsertOutcome.Inserted, db.Cases.Upsert(Case(rut: null, row: 12)));
        Assert.Equal(UpsertOutcome.Updated, db.Cases.Upsert(Case(rut: null, row: 12, fullName: "JUAN PEREZ SOTO")));

        var all = db.Cases.Query(new CaseFilter(), 0, 50);
        Assert.Single(all);
        Assert.Equal("JUAN PEREZ SOTO", all[0].FullName);
    }

    [Fact]
    public void Different_people_on_the_same_day_are_separate_cases()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, fullName: "MARGARITA PARRAGUEZ"));

        Assert.Equal(2, db.Cases.Count(new CaseFilter()));
    }

    [Fact]
    public void Filters_by_office_month_and_state()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, office: Office.Placilla, sheet: "ENERO PLACILLA"));
        db.Cases.Upsert(Case(rut: "5.667.048-3", row: 5, state: FolderState.PrimeraLicencia));

        Assert.Equal(2, db.Cases.Count(new CaseFilter { Office = Office.AvenidaArgentina }));
        Assert.Equal(3, db.Cases.Count(new CaseFilter { Year = 2026, Month = 1 }));
        Assert.Equal(0, db.Cases.Count(new CaseFilter { Year = 2026, Month = 2 }));
        Assert.Equal(1, db.Cases.Count(new CaseFilter { FolderState = FolderState.PrimeraLicencia }));
    }

    /// <summary>CitationDay powers Estadísticas' "marcar asistencia" bulk action — needs one exact
    /// day's agenda, independent of Year/Month (which only narrow to a whole month).</summary>
    [Fact]
    public void Filters_by_exact_citation_day()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3, day: 2));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, day: 2));
        db.Cases.Upsert(Case(rut: "5.667.048-3", row: 5, day: 5));

        var day2 = db.Cases.QueryAll(new CaseFilter { CitationDay = new DateOnly(2026, 1, 2) });
        var day5 = db.Cases.QueryAll(new CaseFilter { CitationDay = new DateOnly(2026, 1, 5) });
        var day9 = db.Cases.QueryAll(new CaseFilter { CitationDay = new DateOnly(2026, 1, 9) });

        Assert.Equal(2, day2.Count);
        Assert.Single(day5);
        Assert.Empty(day9);
    }

    [Fact]
    public void Searches_a_rut_typed_without_dots()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Upsert(Case(rut: "13.025.150-1"));

        Assert.Equal(1, db.Cases.Count(new CaseFilter { Search = "130251501" }));
        Assert.Equal(1, db.Cases.Count(new CaseFilter { Search = "perez" }));
        Assert.Equal(0, db.Cases.Count(new CaseFilter { Search = "gonzalez" }));
    }

    [Fact]
    public void Splits_cases_between_archivo_and_oficina_43()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3, lastFolder: new DateOnly(2023, 6, 30)));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, lastFolder: new DateOnly(2023, 7, 1)));

        Assert.Single(db.Cases.ForSector(FolderSector.Archivo, onlyMarked: false));
        Assert.Single(db.Cases.ForSector(FolderSector.Oficina43, onlyMarked: false));
    }

    [Fact]
    public void Only_marked_cases_reach_the_printable_sector_list_when_asked()
    {
        using var db = new SqliteTestDatabase();

        var id = db.Cases.Insert(Case(lastFolder: new DateOnly(2020, 1, 1)));
        db.Cases.Insert(Case(rut: "16.487.222-K", row: 4, lastFolder: new DateOnly(2020, 1, 1)));
        db.Cases.SetMarked(id, true);

        Assert.Single(db.Cases.ForSector(FolderSector.Archivo, onlyMarked: true));
        Assert.Equal(2, db.Cases.ForSector(FolderSector.Archivo, onlyMarked: false).Count);
    }

    [Fact]
    public void Counts_attendance_per_day_and_office()
    {
        using var db = new SqliteTestDatabase();

        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3, attended: true));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4, attended: false));
        db.Cases.Upsert(Case(rut: "5.667.048-3", row: 5, office: Office.Placilla, sheet: "ENERO PLACILLA", attended: true));

        var attendance = db.Cases.DailyAttendance(2026, 1);

        var avArgentina = attendance.Single(entry => entry.Office == Office.AvenidaArgentina);
        Assert.Equal(2, avArgentina.Scheduled);
        Assert.Equal(1, avArgentina.Attended);

        var placilla = attendance.Single(entry => entry.Office == Office.Placilla);
        Assert.Equal(1, placilla.Scheduled);
        Assert.Equal(1, placilla.Attended);
    }

    [Fact]
    public void CambioDomicilioComuna_round_trips_through_insert_and_UpdateEditableFields()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case());

        var inserted = db.Cases.Insert(new FolderCase
        {
            FullName = "MARIA LOPEZ",
            Rut = "9.271.271-K",
            Office = Office.AvenidaArgentina,
            CambioDomicilioComuna = "Quillota"
        });
        Assert.Equal("Quillota", db.Cases.FindById(inserted)!.CambioDomicilioComuna);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, FolderState.CambioDomicilioSolicitado, null, null,
            null, needsReview: false, cambioDomicilioComuna: "Valparaíso");

        Assert.Equal("Valparaíso", db.Cases.FindById(id)!.CambioDomicilioComuna);
    }

    [Fact]
    public void Editing_a_case_clears_the_raw_leftovers_and_the_review_flag()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ",
            Rut = "13.025.150-9",
            Office = Office.AvenidaArgentina,
            FolderStateRaw = "ALGO RARO",
            NeedsReview = true
        });

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ SOTO", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, FolderState.SubidaAConaset, FinalDecision.Otorgado, MoralIdoneity.Alertada,
            "SI, EN AV. ARGENTINA", needsReview: false);

        var stored = db.Cases.FindById(id)!;
        Assert.Equal("JUAN PEREZ SOTO", stored.FullName);
        Assert.Equal("13.025.150-1", stored.Rut);
        Assert.Null(stored.FolderStateRaw);
        Assert.Equal(FolderState.SubidaAConaset, stored.FolderState);
        Assert.Equal("SI, EN AV. ARGENTINA", stored.AttentionNote);
        // La asistencia ya no se deduce del texto de ATENCIÓN: tiene su propia casilla.
        Assert.False(stored.Attended);
        Assert.False(stored.NeedsReview);
    }

    [Fact]
    public void Pages_results_without_losing_or_repeating_rows()
    {
        using var db = new SqliteTestDatabase();
        for (var i = 0; i < 25; i++)
        {
            db.Cases.Insert(Case(fullName: $"PERSONA {i:D2}", rut: null, row: i));
        }

        var firstPage = db.Cases.Query(new CaseFilter(), 0, 10);
        var secondPage = db.Cases.Query(new CaseFilter(), 10, 10);

        Assert.Equal(10, firstPage.Count);
        Assert.Equal(10, secondPage.Count);
        Assert.Empty(firstPage.Select(c => c.Id).Intersect(secondPage.Select(c => c.Id)));
        Assert.Equal(25, db.Cases.Count(new CaseFilter()));
    }

    [Fact]
    public void DeleteAllPermanently_WipesEveryRow_AndReturnsHowMany()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3));
        db.Cases.Upsert(Case(rut: "16.487.222-K", row: 4));
        db.Cases.Upsert(Case(rut: "5.667.048-3", row: 5));

        var deleted = db.Cases.DeleteAllPermanently();

        Assert.Equal(3, deleted);
        Assert.Equal(0, db.Cases.Count(new CaseFilter()));
        Assert.Empty(db.Cases.Query(new CaseFilter(), 0, 100));
    }

    // --- FinalDecisionAt --------------------------------------------------

    [Fact]
    public void EnsureSchema_adds_FinalDecisionAt_column_idempotently()
    {
        using var db = new SqliteTestDatabase();

        // Called once already by the SqliteTestDatabase constructor — calling it again must not throw.
        db.Cases.EnsureSchema();
        db.Cases.EnsureSchema();

        var id = db.Cases.Insert(Case());
        Assert.Null(db.Cases.FindById(id)!.FinalDecisionAt);
    }

    [Fact]
    public void EnsureSchema_backfills_FinalDecisionAt_from_the_last_decision_audit_entry()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case(state: null));

        // Simula una fila decidida antes de que existiera la columna: FinalDecision quedó en
        // Otorgado por una vía que no pasó por el nuevo cálculo (aquí, escritura directa) y su
        // única huella del momento de la decisión es la bitácora de auditoría.
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString))
        {
            connection.Open();
            using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE FolderCase SET FinalDecision = 0, FinalDecisionAt = NULL WHERE Id = $id";
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }

            using var audit = connection.CreateCommand();
            audit.CommandText = """
                INSERT INTO CaseAuditLog (FolderCaseId, ChangedBy, ChangedAt, FieldName, OldValue, NewValue)
                VALUES ($id, 'tester', '2026-05-04T10:00:00+00:00', 'Decisión final', NULL, 'Otorgado')
                """;
            audit.Parameters.AddWithValue("$id", id);
            audit.ExecuteNonQuery();
        }

        db.Cases.EnsureSchema();

        var stored = db.Cases.FindById(id)!;
        Assert.NotNull(stored.FinalDecisionAt);
        Assert.Equal(DateTimeOffset.Parse("2026-05-04T10:00:00+00:00"), stored.FinalDecisionAt);
    }

    [Fact]
    public void EnsureSchema_leaves_FinalDecisionAt_null_when_there_is_no_audit_entry_to_backfill_from()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case(state: null));

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString))
        {
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE FolderCase SET FinalDecision = 1, FinalDecisionAt = NULL WHERE Id = $id";
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }

        db.Cases.EnsureSchema();

        Assert.Null(db.Cases.FindById(id)!.FinalDecisionAt);
    }

    [Fact]
    public void EnsureSchema_backfills_FinalDecisionAt_for_a_Denegado_case_with_audit_entry()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case(state: null));

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString))
        {
            connection.Open();
            using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE FolderCase SET FinalDecision = 1, FinalDecisionAt = NULL WHERE Id = $id";
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }

            using var audit = connection.CreateCommand();
            audit.CommandText = """
                INSERT INTO CaseAuditLog (FolderCaseId, ChangedBy, ChangedAt, FieldName, OldValue, NewValue)
                VALUES ($id, 'tester', '2026-06-10T09:30:00+00:00', 'Decisión final', 'ParaDenegar', 'Denegado')
                """;
            audit.Parameters.AddWithValue("$id", id);
            audit.ExecuteNonQuery();
        }

        db.Cases.EnsureSchema();

        var stored = db.Cases.FindById(id)!;
        Assert.Equal(FinalDecision.Denegado, stored.FinalDecision);
        Assert.Equal(DateTimeOffset.Parse("2026-06-10T09:30:00+00:00"), stored.FinalDecisionAt);
    }

    [Fact]
    public void EnsureSchema_skips_backfill_when_the_audit_entry_timestamp_is_unparsable()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case(state: null));

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString))
        {
            connection.Open();
            using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE FolderCase SET FinalDecision = 0, FinalDecisionAt = NULL WHERE Id = $id";
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }

            using var audit = connection.CreateCommand();
            audit.CommandText = """
                INSERT INTO CaseAuditLog (FolderCaseId, ChangedBy, ChangedAt, FieldName, OldValue, NewValue)
                VALUES ($id, 'tester', 'no-es-una-fecha', 'Decisión final', NULL, 'Otorgado')
                """;
            audit.Parameters.AddWithValue("$id", id);
            audit.ExecuteNonQuery();
        }

        db.Cases.EnsureSchema();

        // Un ChangedAt ilegible no debe tumbar el backfill ni escribir basura: la fila queda sin
        // fecha, igual que un caso importado del libro sin auditoría.
        Assert.Null(db.Cases.FindById(id)!.FinalDecisionAt);
    }

    [Fact]
    public void FindById_reports_no_date_instead_of_throwing_when_FinalDecisionAt_is_garbage()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case(state: null));

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.ConnectionString))
        {
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE FolderCase SET FinalDecision = 0, FinalDecisionAt = 'no-es-una-fecha' WHERE Id = $id";
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }

        // Una fila con un valor corrupto en FinalDecisionAt no debe romper el listado: se lee como
        // "sin fecha" en vez de lanzar.
        var stored = db.Cases.FindById(id);
        Assert.NotNull(stored);
        Assert.Null(stored!.FinalDecisionAt);
        Assert.DoesNotContain(db.Cases.Query(new CaseFilter(), 0, 10), c => c.Id == id && c.FinalDecisionAt is not null);
    }

    [Fact]
    public void Insert_sets_FinalDecisionAt_when_the_case_arrives_already_decided()
    {
        using var db = new SqliteTestDatabase();

        var granted = db.Cases.Insert(new FolderCase { FullName = "A", Office = Office.AvenidaArgentina, FinalDecision = FinalDecision.Otorgado });
        var pending = db.Cases.Insert(new FolderCase { FullName = "B", Office = Office.AvenidaArgentina, FinalDecision = FinalDecision.EsperaExamen });
        var undecided = db.Cases.Insert(new FolderCase { FullName = "C", Office = Office.AvenidaArgentina });

        Assert.NotNull(db.Cases.FindById(granted)!.FinalDecisionAt);
        Assert.Null(db.Cases.FindById(pending)!.FinalDecisionAt);
        Assert.Null(db.Cases.FindById(undecided)!.FinalDecisionAt);
    }

    [Fact]
    public void UpdateEditableFields_sets_FinalDecisionAt_when_decision_becomes_granted_or_denied()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case(state: null));
        Assert.Null(db.Cases.FindById(id)!.FinalDecisionAt);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, FinalDecision.Otorgado, null, null, needsReview: false);

        var stored = db.Cases.FindById(id)!;
        Assert.NotNull(stored.FinalDecisionAt);
        Assert.True(stored.FinalDecisionAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void UpdateEditableFields_clears_FinalDecisionAt_when_decision_moves_away_from_granted_or_denied()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case(state: null));
        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, FinalDecision.Denegado, null, null, needsReview: false);
        Assert.NotNull(db.Cases.FindById(id)!.FinalDecisionAt);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, FinalDecision.ClasePendiente, null, null, needsReview: false);

        Assert.Null(db.Cases.FindById(id)!.FinalDecisionAt);
    }

    [Fact]
    public void UpdateEditableFields_keeps_FinalDecisionAt_untouched_when_decision_does_not_change()
    {
        using var db = new SqliteTestDatabase();
        var id = db.Cases.Insert(Case(state: null));
        db.Cases.UpdateEditableFields(id, "JUAN PEREZ", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, FinalDecision.Otorgado, null, null, needsReview: false);
        var firstTimestamp = db.Cases.FindById(id)!.FinalDecisionAt;
        Assert.NotNull(firstTimestamp);

        db.Cases.UpdateEditableFields(id, "JUAN PEREZ SOTO", "13.025.150-1", new DateOnly(2026, 1, 2),
            null, null, null, null, FinalDecision.Otorgado, null, "nota", needsReview: false);

        Assert.Equal(firstTimestamp, db.Cases.FindById(id)!.FinalDecisionAt);
    }

    [Fact]
    public void Upsert_sets_FinalDecisionAt_on_reimport_when_decision_changes_to_granted_or_denied()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Upsert(Case(rut: "13.025.150-1", row: 3, state: FolderState.PrimeraLicencia));
        Assert.Null(db.Cases.Query(new CaseFilter(), 0, 10)[0].FinalDecisionAt);

        var reimported = Case(rut: "13.025.150-1", row: 3, state: FolderState.SubidaAConaset);
        reimported.FinalDecision = FinalDecision.Otorgado;
        db.Cases.Upsert(reimported);

        var stored = db.Cases.Query(new CaseFilter(), 0, 10)[0];
        Assert.NotNull(stored.FinalDecisionAt);
    }

    [Fact]
    public void Upsert_clears_FinalDecisionAt_on_reimport_when_decision_moves_away_from_granted_or_denied()
    {
        using var db = new SqliteTestDatabase();
        var first = Case(rut: "13.025.150-1", row: 3);
        first.FinalDecision = FinalDecision.Denegado;
        db.Cases.Upsert(first);
        Assert.NotNull(db.Cases.Query(new CaseFilter(), 0, 10)[0].FinalDecisionAt);

        var reimported = Case(rut: "13.025.150-1", row: 3);
        reimported.FinalDecision = FinalDecision.ParaDenegar;
        db.Cases.Upsert(reimported);

        Assert.Null(db.Cases.Query(new CaseFilter(), 0, 10)[0].FinalDecisionAt);
    }

    [Fact]
    public void Upsert_keeps_FinalDecisionAt_untouched_on_reimport_when_decision_does_not_change()
    {
        using var db = new SqliteTestDatabase();
        var first = Case(rut: "13.025.150-1", row: 3);
        first.FinalDecision = FinalDecision.Otorgado;
        db.Cases.Upsert(first);
        var firstTimestamp = db.Cases.Query(new CaseFilter(), 0, 10)[0].FinalDecisionAt;
        Assert.NotNull(firstTimestamp);

        var reimported = Case(rut: "13.025.150-1", row: 57, state: FolderState.SubidaConF8);
        reimported.FinalDecision = FinalDecision.Otorgado;
        db.Cases.Upsert(reimported);

        Assert.Equal(firstTimestamp, db.Cases.Query(new CaseFilter(), 0, 10)[0].FinalDecisionAt);
    }

    [Fact]
    public void ParaDenegar_counts_as_pendiente_en_curso_not_as_a_final_decision()
    {
        // Regresión: ParaDenegar debe seguir cayendo en "pendiente/en curso" y nunca en
        // Otorgado/Denegado — este test blinda esa regla si la enumeración cambia a futuro.
        FinalDecision value = FinalDecision.ParaDenegar;
        Assert.False(value is FinalDecision.Otorgado or FinalDecision.Denegado);

        using var db = new SqliteTestDatabase();
        var pending = Case(rut: "13.025.150-1", row: 3);
        pending.FinalDecision = FinalDecision.ParaDenegar;
        db.Cases.Upsert(pending);

        var stored = db.Cases.Query(new CaseFilter(), 0, 10)[0];
        Assert.Equal(FinalDecision.ParaDenegar, stored.FinalDecision);
        Assert.Null(stored.FinalDecisionAt);
    }
}
