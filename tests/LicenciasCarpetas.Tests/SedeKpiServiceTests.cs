using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Statistics;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Tests;

/// <summary>Change "sgl-kpi-dashboard-comparativo": per-office KPIs for a period.</summary>
public class SedeKpiServiceTests
{
    private static readonly DateOnly Today = new(2026, 6, 30);

    [Theory]
    [InlineData(KpiPeriodKind.Mes, 2, "2026-02-01", "2026-02-28")]
    [InlineData(KpiPeriodKind.Semestre, 2, "2026-07-01", "2026-12-31")]
    [InlineData(KpiPeriodKind.Anio, 0, "2026-01-01", "2026-12-31")]
    public void Periods_resolve_to_inclusive_date_ranges(KpiPeriodKind kind, int index, string from, string to)
    {
        var period = new KpiPeriod(2026, kind, index);

        Assert.Equal(DateOnly.Parse(from), period.From);
        Assert.Equal(DateOnly.Parse(to), period.To);
    }

    [Fact]
    public void Groups_decisions_per_office_and_only_counts_the_period()
    {
        using var db = new SqliteTestDatabase();
        Add(db, Office.AvenidaArgentina, "2026-03-02", decision: FinalDecision.Otorgado);
        Add(db, Office.AvenidaArgentina, "2026-03-03", decision: FinalDecision.Denegado);
        Add(db, Office.AvenidaArgentina, "2026-03-04", decision: FinalDecision.EsperaExamen);
        Add(db, Office.AvenidaArgentina, "2026-03-05");
        Add(db, Office.Placilla, "2026-03-05", decision: FinalDecision.Otorgado);
        Add(db, Office.Placilla, "2026-04-01", decision: FinalDecision.Otorgado); // fuera del mes
        var deleted = Add(db, Office.Placilla, "2026-03-06", decision: FinalDecision.Otorgado);
        db.Cases.Delete(deleted);

        var report = Service(db).Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 3), OfficeScope.All, Today);

        var argentina = report.For(Office.AvenidaArgentina);
        Assert.Equal((4, 1, 1, 1, 1, 2), (argentina.Total, argentina.Otorgado, argentina.Denegado, argentina.EnCurso, argentina.SinDecision, argentina.Pendiente));
        Assert.Equal(25.0, argentina.PercentWithoutDecision);
        Assert.Equal(1, report.For(Office.Placilla).Total);
        Assert.Equal(0, report.For(Office.MercadoPuerto).Total);
        Assert.Equal(5, report.Total.Total);
    }

    [Fact]
    public void Decision_times_come_from_the_audit_log_and_report_the_sample()
    {
        using var db = new SqliteTestDatabase();
        var a = Add(db, Office.Placilla, "2026-03-01", decision: FinalDecision.Otorgado);
        var b = Add(db, Office.Placilla, "2026-03-01", decision: FinalDecision.Denegado);
        var c = Add(db, Office.Placilla, "2026-03-01", decision: FinalDecision.Otorgado);
        Add(db, Office.Placilla, "2026-03-01", decision: FinalDecision.Otorgado); // importado: sin auditoría
        Audit(db, a, "Otorgado", "2026-03-03T12:00:00+00:00");
        Audit(db, b, "Denegado", "2026-03-05T12:00:00+00:00");
        Audit(db, c, "EsperaExamen", "2026-03-02T12:00:00+00:00");
        Audit(db, c, "Otorgado", "2026-03-11T12:00:00+00:00");

        var placilla = Service(db).Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 3), OfficeScope.All, Today).For(Office.Placilla);

        Assert.Equal(3, placilla.DaysSample);
        Assert.Equal(5.3, placilla.AverageDays); // (2 + 4 + 10) / 3, a un decimal
        Assert.Equal(4, placilla.MedianDays);
        Assert.Equal(1, placilla.DecidedWithoutDate);
        Assert.Equal(25.0, placilla.PercentDecidedWithoutDate);
    }

    [Fact]
    public void Backlog_buckets_pending_cases_by_age()
    {
        using var db = new SqliteTestDatabase();
        Add(db, Office.MercadoPuerto, "2026-06-28", state: FolderState.SeEncuentraEnOficina43);                 // 2 días
        Add(db, Office.MercadoPuerto, "2026-06-20", state: FolderState.SeEncuentraEnOficina43);                 // 10 días
        Add(db, Office.MercadoPuerto, "2026-06-01", state: FolderState.SeEncuentraEnOficina43);                 // 29 días
        Add(db, Office.MercadoPuerto, "2026-06-01", state: FolderState.SubidaAConaset);              // subida: fuera
        Add(db, Office.MercadoPuerto, "2026-06-01", state: FolderState.SeEncuentraEnOficina43, decision: FinalDecision.Otorgado); // decidido: fuera

        var puerto = Service(db).Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 6), OfficeScope.All, Today).For(Office.MercadoPuerto);

        Assert.Equal((1, 1, 1), (puerto.BacklogNormal, puerto.BacklogWarning, puerto.BacklogOverdue));
    }

    [Fact]
    public void Scope_limits_offices_and_totals()
    {
        using var db = new SqliteTestDatabase();
        Add(db, Office.AvenidaArgentina, "2026-03-02");
        Add(db, Office.Placilla, "2026-03-02");

        var report = Service(db).Build(new KpiPeriod(2026, KpiPeriodKind.Anio, 0), new OfficeScope([Office.Placilla]), Today);

        Assert.Equal([Office.Placilla], report.Offices.Select(o => o.Office!.Value));
        Assert.Equal(1, report.Total.Total);
    }

    [Fact]
    public void Trend_has_one_point_per_month_and_office()
    {
        using var db = new SqliteTestDatabase();
        Add(db, Office.Placilla, "2026-01-10", decision: FinalDecision.Otorgado);
        Add(db, Office.Placilla, "2026-03-10");

        var report = Service(db).Build(new KpiPeriod(2026, KpiPeriodKind.Semestre, 1), OfficeScope.All, Today);

        Assert.Equal(6 * 3, report.Trend.Count);
        var january = report.Trend.Single(t => t.Office == Office.Placilla && t.Month == 1);
        Assert.Equal((1, 1), (january.Total, january.Otorgado));
    }

    [Fact]
    public void Folder_state_breakdown_uses_catalog_names()
    {
        using var db = new SqliteTestDatabase();
        Add(db, Office.Placilla, "2026-03-10", state: FolderState.SubidaAConaset);
        Add(db, Office.Placilla, "2026-03-11", state: FolderState.SubidaAConaset);
        Add(db, Office.Placilla, "2026-03-12");

        var placilla = Service(db).Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 3), OfficeScope.All, Today).For(Office.Placilla);

        Assert.Contains(placilla.States, s => s.State == FolderStateCatalog.Display(FolderState.SubidaAConaset) && s.Count == 2);
        Assert.Contains(placilla.States, s => s.State == "Sin estado" && s.Count == 1);
    }

    private static SedeKpiService Service(SqliteTestDatabase db) => new(db.ConnectionString);

    private static long Add(SqliteTestDatabase db, Office office, string citation, FolderState? state = null, FinalDecision? decision = null)
        => db.Cases.Insert(new FolderCase
        {
            FullName = "PERSONA",
            Rut = "11.111.111-1",
            Office = office,
            CitationDate = DateOnly.Parse(citation),
            FolderState = state,
            FinalDecision = decision
        });

    private static void Audit(SqliteTestDatabase db, long caseId, string newValue, string at)
    {
        using var connection = new SqliteConnection(db.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CaseAuditLog (FolderCaseId, ChangedBy, ChangedAt, FieldName, OldValue, NewValue)
            VALUES ($id, 'ana', $at, 'Decisión final', NULL, $value)
            """;
        command.Parameters.AddWithValue("$id", caseId);
        command.Parameters.AddWithValue("$at", at);
        command.Parameters.AddWithValue("$value", newValue);
        command.ExecuteNonQuery();
    }
}

public class SedeKpiExcelExporterTests
{
    [Fact]
    public void Workbook_matches_the_report()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(new FolderCase { FullName = "X", Office = Office.Placilla, CitationDate = new DateOnly(2026, 3, 1), FinalDecision = FinalDecision.Otorgado });
        var report = new SedeKpiService(db.ConnectionString).Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 3), OfficeScope.All, new DateOnly(2026, 6, 30));

        var bytes = LicenciasCarpetas.Reporting.SedeKpiExcelExporter.Export(report);

        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Resumen", "Estados", "Tendencia"], workbook.Worksheets.Select(w => w.Name));
        var summary = workbook.Worksheet("Resumen");
        Assert.Equal("Placilla", summary.Cell(5, 1).GetString());
        Assert.Equal(1, summary.Cell(5, 3).GetValue<int>());
        Assert.Equal("Total", summary.Cell(7, 1).GetString());
    }
}
