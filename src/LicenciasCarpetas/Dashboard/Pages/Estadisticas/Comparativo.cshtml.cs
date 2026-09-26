using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Reporting;
using LicenciasCarpetas.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages.Estadisticas;

/// <summary>Comparativo por sede: la página solo presenta; todo el cálculo vive en <see cref="SedeKpiService"/>.</summary>
[Authorize(Roles = "Administrador,Jefatura,Coordinador")]
public class ComparativoModel(SedeKpiService kpis, OfficeScope userScope) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Year { get; set; } = DateTime.Today.Year;

    [BindProperty(SupportsGet = true)]
    public KpiPeriodKind Kind { get; set; } = KpiPeriodKind.Mes;

    /// <summary>Mes (1–12) o semestre (1–2), según <see cref="Kind"/>.</summary>
    [BindProperty(SupportsGet = true)]
    public int Index { get; set; } = DateTime.Today.Month;

    [BindProperty(SupportsGet = true)]
    public Office? Sede { get; set; }

    public SedeKpiReport Report { get; private set; } = null!;

    /// <summary>Sedes que el usuario puede elegir en el filtro.</summary>
    public IReadOnlyList<Office> Offices => userScope.Offices;

    public void OnGet() => Report = Build();

    public IActionResult OnGetExport()
    {
        var report = Build();
        var fileName = $"kpi-sedes-{report.Period.From:yyyyMM}-{report.Period.To:yyyyMM}.xlsx";
        return File(SedeKpiExcelExporter.Export(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private SedeKpiReport Build()
    {
        Index = Kind switch
        {
            KpiPeriodKind.Mes => Math.Clamp(Index, 1, 12),
            KpiPeriodKind.Semestre => Math.Clamp(Index, 1, 2),
            _ => 0
        };
        var scope = userScope.Narrow(Sede);
        return kpis.Build(new KpiPeriod(Year, Kind, Index), scope, DateOnly.FromDateTime(DateTime.Today));
    }
}
