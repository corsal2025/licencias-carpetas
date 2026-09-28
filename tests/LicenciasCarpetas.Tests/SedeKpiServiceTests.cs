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
    public void Decision_times_come_from_FinalDecisionAt_in_business_days_and_report_the_sample()
    {
        // Citación domingo 2026-03-01 (no cuenta). Días hábiles hasta cada FinalDecisionAt
        // (convertido a fecha de Chile): martes 03 → 2, jueves 05 → 4, miércoles 11 → 8
        // (excluye sáb 07 y dom 08). Promedio (2+4+8)/3 = 4,7 (redondeado a 1 decimal); mediana 4.
        using var db = new SqliteTestDatabase();
        var a = Add(db, Office.Placilla, "2026-03-01", decision: FinalDecision.Otorgado);
        var b = Add(db, Office.Placilla, "2026-03-01", decision: FinalDecision.Denegado);
        var c = Add(db, Office.Placilla, "2026-03-01", decision: FinalDecision.Otorgado);
        var imported = Add(db, Office.Placilla, "2026-03-01", decision: FinalDecision.Otorgado); // importado: sin fecha confiable
        SetFinalDecisionAt(db, a, "2026-03-03T12:00:00+00:00");
        SetFinalDecisionAt(db, b, "2026-03-05T12:00:00+00:00");
        SetFinalDecisionAt(db, c, "2026-03-11T12:00:00+00:00");
        SetFinalDecisionAt(db, imported, null); // el Insert real ya la habría puesto: se limpia para simular la importación legacy sin fecha

        var placilla = Service(db).Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 3), OfficeScope.All, Today).For(Office.Placilla);

        Assert.Equal(3, placilla.DaysSample);
        Assert.Equal(4.7, placilla.AverageDays);
        Assert.Equal(4, placilla.MedianDays);
        Assert.Equal(1, placilla.DecidedWithoutDate);
        Assert.Equal(25.0, placilla.PercentDecidedWithoutDate);
        Assert.Equal(75.0, placilla.PercentWithDecisionDate);
    }

    [Fact]
    public void FinalDecisionAt_is_converted_to_the_Chile_timezone_before_counting_business_days()
    {
        // Chile está detrás de UTC (UTC-3 o UTC-4 según la regla vigente); 2026-03-11 02:30 UTC cae
        // igual del lado de Chile del martes 10 con cualquiera de los dos offsets — un decisionAt
        // que ya cruzó la medianoche en UTC debe seguir contando la fecha de Chile, no la de UTC.
        using var db = new SqliteTestDatabase();
        var id = Add(db, Office.Placilla, "2026-03-09", decision: FinalDecision.Otorgado); // lunes
        SetFinalDecisionAt(db, id, "2026-03-11T02:30:00+00:00"); // UTC miércoles madrugada = Chile martes noche

        var placilla = Service(db).Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 3), OfficeScope.All, Today).For(Office.Placilla);

        // Citación lunes 09, decisión (fecha Chile) martes 10 → 1 día hábil, no 2.
        Assert.Equal(1, placilla.AverageDays);
    }

    [Fact]
    public void Empty_office_scope_returns_zero_rows_everywhere()
    {
        using var db = new SqliteTestDatabase();
        Add(db, Office.AvenidaArgentina, "2026-03-02", decision: FinalDecision.Otorgado);
        Add(db, Office.Placilla, "2026-03-03", decision: FinalDecision.Denegado);

        var report = Service(db).Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 3), new OfficeScope([]), Today);

        Assert.Empty(report.Offices);
        Assert.Equal(0, report.Total.Total);
        Assert.Equal(0, report.Total.Otorgado);
        Assert.Equal(0, report.Total.DaysSample);
        Assert.Null(report.Total.AverageDays);
        Assert.Empty(report.Trend);
    }

    [Fact]
    public void Report_warns_when_a_year_in_the_period_has_no_holidays_configured()
    {
        using var db = new SqliteTestDatabase();
        var calculator = new BusinessDayCalculator(new KpiOptions { Holidays = ["2026-01-01"] });

        var reportWithConfiguredYear = new SedeKpiService(db.ConnectionString, calculator)
            .Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 1), OfficeScope.All, Today);
        var reportWithMissingYear = new SedeKpiService(db.ConnectionString, calculator)
            .Build(new KpiPeriod(2028, KpiPeriodKind.Mes, 1), OfficeScope.All, Today);

        Assert.True(reportWithConfiguredYear.HolidaysConfiguredForPeriod);
        Assert.False(reportWithMissingYear.HolidaysConfiguredForPeriod);
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

    private static readonly IBusinessDayCalculator NoHolidays = new BusinessDayCalculator(new KpiOptions());

    private static SedeKpiService Service(SqliteTestDatabase db) => new(db.ConnectionString, NoHolidays);

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

    /// <summary>Escribe <c>FinalDecisionAt</c> directamente, sin pasar por el repositorio — para
    /// fijar una fecha determinística en el test (el repositorio la fija en "ahora") o para simular
    /// una fila decidida sin fecha confiable (importación legacy, valor null).</summary>
    private static void SetFinalDecisionAt(SqliteTestDatabase db, long caseId, string? at)
    {
        using var connection = new SqliteConnection(db.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE FolderCase SET FinalDecisionAt = $at WHERE Id = $id";
        command.Parameters.AddWithValue("$at", (object?)at ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", caseId);
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
        var report = new SedeKpiService(db.ConnectionString, new BusinessDayCalculator(new KpiOptions()))
            .Build(new KpiPeriod(2026, KpiPeriodKind.Mes, 3), OfficeScope.All, new DateOnly(2026, 6, 30));

        var bytes = LicenciasCarpetas.Reporting.SedeKpiExcelExporter.Export(report);

        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Resumen", "Estados", "Tendencia"], workbook.Worksheets.Select(w => w.Name));
        var summary = workbook.Worksheet("Resumen");
        Assert.Equal("Placilla", summary.Cell(5, 1).GetString());
        Assert.Equal(1, summary.Cell(5, 3).GetValue<int>());
        Assert.Equal("Total", summary.Cell(7, 1).GetString());
    }
}
