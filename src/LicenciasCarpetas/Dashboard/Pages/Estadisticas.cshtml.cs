using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize(Roles = "Administrador,Jefatura,Coordinador")]
public class EstadisticasModel(
    StatisticsService statistics,
    IDailyCounterRepository counters,
    IScopedCaseRepository cases) : PageModel
{
    public MonthlyStatistics? Statistics { get; private set; }
    public MonthlyUserStatistics? UserStatistics { get; private set; }
    public IReadOnlyList<int> Years { get; private set; } = [];
    public IReadOnlyList<FolderCase> AttendanceDayCases { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public int Year { get; set; } = DateTime.Today.Year;

    [BindProperty(SupportsGet = true)]
    public int Month { get; set; } = DateTime.Today.Month;

    /// <summary>Día elegido para marcar asistencia en bloque — "Atendido" se registra acá, no fila
    /// por fila en la tabla de Casos (que ya no trae esa columna).</summary>
    [BindProperty(SupportsGet = true)]
    public string? AttendanceDate { get; set; }

    public string? Message { get; set; }

    /// <summary>Sedes que este usuario puede ver y elegir (roles por sede).</summary>
    public IReadOnlyList<Office> AllowedOffices => cases.Scope.Offices;

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPostMarkAttendance(string attendanceDate, long[] attendedIds)
    {
        if (!DateOnly.TryParseExact(attendanceDate, "yyyy-MM-dd", out var date))
        {
            TempData["Message"] = $"Fecha inválida: {attendanceDate}";
            return RedirectToPage(new { year = Year, month = Month });
        }

        var attendedSet = attendedIds.ToHashSet();
        var dayCases = cases.QueryAll(new CaseFilter { CitationDay = date });
        var attendedCount = 0;
        foreach (var dayCase in dayCases)
        {
            var attended = attendedSet.Contains(dayCase.Id);
            cases.SetAttended(dayCase.Id, attended, editedBy: User?.Identity?.Name);
            if (attended) attendedCount++;
        }

        // attendedSet.Count, no: un id de attendedIds ajeno al día (formulario obsoleto o
        // manipulado) nunca se usa para escribir, pero contarlo igual daría un mensaje como
        // "5 de 4 atendido(s)" — attendedCount solo cuenta los que de verdad son de este día.
        TempData["Message"] = $"Asistencia del {date:dd-MM-yyyy} guardada ({attendedCount} de {dayCases.Count} atendido(s)).";
        return RedirectToPage(new { year = Year, month = Month, attendanceDate });
    }

    /// <summary>Escaneadas / subidas are the only numbers nobody can derive from the agenda, so they
    /// stay hand-entered exactly like in the workbook.</summary>
    public IActionResult OnPostSetCounters(string fecha, int? escaneadas, int? subidas)
    {
        if (!DateOnly.TryParseExact(fecha, "yyyy-MM-dd", out var date))
        {
            TempData["Message"] = $"Fecha inválida: {fecha}";
            return RedirectToPage(new { year = Year, month = Month });
        }

        counters.Upsert(new DailyCounter { Date = date, Scanned = escaneadas, Uploaded = subidas });
        TempData["Message"] = $"Contadores del {date:dd-MM-yyyy} guardados.";
        return RedirectToPage(new { year = Year, month = Month });
    }

    private void Load()
    {
        Years = cases.DistinctYears();
        if (Years.Count > 0 && !Years.Contains(Year))
        {
            Year = Years[0];
        }

        Statistics = statistics.ForMonth(Year, Month, cases.Scope);
        UserStatistics = statistics.UserStatsForMonth(Year, Month, cases.Scope);
        Message = TempData["Message"] as string;

        if (DateOnly.TryParseExact(AttendanceDate, "yyyy-MM-dd", out var date))
        {
            AttendanceDayCases = cases.QueryAll(new CaseFilter { CitationDay = date })
                .OrderBy(c => c.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public static string Percent(double? rate) => rate is { } value ? $"{value * 100:0.#}%" : "—";
}
