using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Statistics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace LicenciasCarpetas.Tests;

/// <summary>"Marcar asistencia" en Estadísticas reemplazó el checkbox "Atendido" fila por fila en
/// la tabla de Casos — un día de agenda a la vez, en vez de buscar cada caso en una tabla de miles
/// de filas.</summary>
public class EstadisticasModelTests
{
    /// <summary>No hay request real en el test, así que TempData (usado para el mensaje de
    /// confirmación) no tiene de dónde salir por su cuenta — este provider en memoria evita el
    /// NullReferenceException sin necesitar sesión ni cookies reales.</summary>
    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private static EstadisticasModel Model(SqliteTestDatabase db) =>
        new(new StatisticsService(db.Cases, db.Counters), db.Counters, db.ScopedCases())
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new InMemoryTempDataProvider())
        };

    private static long Seed(SqliteTestDatabase db, DateOnly citationDate, bool attended = false) =>
        db.Cases.Insert(new FolderCase
        {
            FullName = "JUAN PEREZ",
            Rut = "13.025.150-1",
            CitationDate = citationDate,
            Office = Office.AvenidaArgentina,
            Attended = attended,
            SourceSheet = "ENERO AV. ARGENTINA",
            SourceRow = 3
        });

    [Fact]
    public void OnPostMarkAttendance_SetsAttendedOnlyForCheckedIds_ForThatDay()
    {
        using var db = new SqliteTestDatabase();
        var day = new DateOnly(2026, 1, 2);
        var attended = Seed(db, day, attended: false);
        var notAttended = Seed(db, day, attended: false);
        var otherDay = Seed(db, day.AddDays(1), attended: true);

        var result = Model(db).OnPostMarkAttendance("2026-01-02", [attended]);

        Assert.True(db.Cases.FindById(attended)!.Attended);
        Assert.False(db.Cases.FindById(notAttended)!.Attended);
        // A different day's case is untouched, checked or not — the handler only looks at that day's agenda.
        Assert.True(db.Cases.FindById(otherDay)!.Attended);
        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToPageResult>(result);
    }

    [Fact]
    public void OnPostMarkAttendance_UncheckingAnAlreadyAttendedCase_ClearsIt()
    {
        using var db = new SqliteTestDatabase();
        var day = new DateOnly(2026, 1, 2);
        var wasAttended = Seed(db, day, attended: true);

        Model(db).OnPostMarkAttendance("2026-01-02", []);

        Assert.False(db.Cases.FindById(wasAttended)!.Attended);
    }

    [Fact]
    public void OnPostMarkAttendance_InvalidDate_RedirectsWithoutTouchingAnyCase()
    {
        using var db = new SqliteTestDatabase();
        var id = Seed(db, new DateOnly(2026, 1, 2), attended: false);

        var result = Model(db).OnPostMarkAttendance("not-a-date", [id]);

        Assert.False(db.Cases.FindById(id)!.Attended);
        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToPageResult>(result);
    }

    [Fact]
    public void OnPostMarkAttendance_DayWithNoCases_DoesNotThrowAndReportsZero()
    {
        using var db = new SqliteTestDatabase();
        var model = Model(db);

        var result = model.OnPostMarkAttendance("2026-01-02", []);

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToPageResult>(result);
        Assert.Equal("Asistencia del 02-01-2026 guardada (0 de 0 atendido(s)).", model.TempData["Message"]);
    }

    /// <summary>An id from a stale/tampered form that isn't actually in that day's agenda must
    /// never inflate the confirmation count past the real total.</summary>
    [Fact]
    public void OnPostMarkAttendance_AttendedIdNotInThatDay_DoesNotInflateTheCountMessage()
    {
        using var db = new SqliteTestDatabase();
        var day = new DateOnly(2026, 1, 2);
        var inDay = Seed(db, day, attended: false);
        var otherDayId = Seed(db, day.AddDays(1), attended: false);
        var model = Model(db);

        model.OnPostMarkAttendance("2026-01-02", [inDay, otherDayId]);

        Assert.True(db.Cases.FindById(inDay)!.Attended);
        Assert.False(db.Cases.FindById(otherDayId)!.Attended); // untouched: not that day's agenda
        Assert.Equal("Asistencia del 02-01-2026 guardada (1 de 1 atendido(s)).", model.TempData["Message"]);
    }
}
