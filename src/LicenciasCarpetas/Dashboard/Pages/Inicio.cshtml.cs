using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Statistics;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize]
public class InicioModel(
    IScopedCaseRepository cases,
    ICambioDomicilioRequestRepository cambioDomicilioRequests,
    CambioDomicilioStatisticsService cambioDomicilioStatistics,
    IUrgentRequestRepository urgentRequests,
    StatisticsService statisticsService,
    LicenciasCarpetas.CambioDomicilio.Ews.EwsHealthCheck? ewsHealth = null) : PageModel
{
    private const string F8EstadoActualSubida = "SUBIDA A CONASET";

    public int CasosCount { get; private set; }
    public int OverdueCasesCount { get; private set; }
    public int OtherComunaCount { get; private set; }
    public int NeedsReviewCount { get; private set; }

    public int CurrentYear { get; private set; }
    public int CurrentMonth { get; private set; }
    public MonthlyStatistics? MonthStats { get; private set; }

    public bool PuedeCambioDomicilio { get; private set; }
    public int? CambioDomicilioPendientes { get; private set; }

    /// <summary>Último estado conocido del correo institucional (solo con acceso a Cambio de Domicilio).</summary>
    public LicenciasCarpetas.CambioDomicilio.Ews.EwsStatus? EwsStatus { get; private set; }

    public async Task<Microsoft.AspNetCore.Mvc.IActionResult> OnPostCheckEwsAsync(CancellationToken cancellationToken)
    {
        if (ewsHealth is not null && User.HasClaim("mod:cambio-domicilio", "true"))
        {
            await ewsHealth.CheckAsync(DateTimeOffset.Now, cancellationToken);
        }

        return RedirectToPage();
    }

    public bool PuedeF8 { get; private set; }
    public int? F8Count { get; private set; }

    public void OnGet()
    {
        CasosCount = cases.Count(new CaseFilter());
        OverdueCasesCount = cases.Count(new CaseFilter { OnlyOverdue = true });
        OtherComunaCount = cases.Count(new CaseFilter { OnlyOtherComuna = true });
        NeedsReviewCount = cases.Count(new CaseFilter { OnlyNeedsReview = true });

        CurrentYear = DateTime.Today.Year;
        CurrentMonth = DateTime.Today.Month;
        MonthStats = statisticsService.ForMonth(CurrentYear, CurrentMonth, cases.Scope);

        PuedeCambioDomicilio = User.HasClaim("mod:cambio-domicilio", "true");
        if (PuedeCambioDomicilio)
        {
            var requests = cambioDomicilioRequests.GetAll();
            CambioDomicilioPendientes = cambioDomicilioStatistics.GetStatusCounts(requests).Pending;
            EwsStatus = ewsHealth?.Last;
        }

        PuedeF8 = User.HasClaim("mod:f8-urgentes", "true");
        if (PuedeF8)
        {
            var all = urgentRequests.GetAll();
            F8Count = all.Count(r => r.EstadoActual != F8EstadoActualSubida);
        }
    }
}
