using System.Security.Claims;
using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Statistics;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8.Domain;
using LicenciasCarpetas.Tests.CambioDomicilio.Routing;
using LicenciasCarpetas.Tests.F8;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Routing;

namespace LicenciasCarpetas.Tests;

/// <summary>The hub's headline numbers must agree with each módulo's own Estadísticas page — this
/// exercises InicioModel.OnGet() directly against seeded data instead of asserting on rendered HTML,
/// so a future edit that computes a count differently (or moves a query outside its claim guard)
/// fails here instead of only being "correct on inspection".</summary>
public class InicioModelTests
{
    private static InicioModel Model(SqliteTestDatabase db, FakeCambioDomicilioRequestRepository cambioDomicilio,
        FakeUrgentRequestRepository urgent, params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        var stats = new LicenciasCarpetas.Statistics.StatisticsService(db.Cases, db.Counters);
        return new InicioModel(db.ScopedCases(), cambioDomicilio, new CambioDomicilioStatisticsService(new CambioDomicilioOptions()), urgent, stats)
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
        };
    }

    private static UrgentRequest Urgent(string estadoActual) => new() { EstadoActual = estadoActual, Origin = "Web" };

    private static PersonRequest Pending(string rut, string fullName) => new()
    {
        Status = RequestStatus.Pending,
        Rut = rut,
        FullName = fullName,
        Comuna = "Valparaíso",
        SourceMessageId = $"msg-{rut}",
        SourceSubject = "Cambio de domicilio",
        SourceSender = "test@munivalpo.cl"
    };

    [Fact]
    public void Sin_claims_de_modulo_solo_muestra_el_conteo_de_Casos()
    {
        using var db = new SqliteTestDatabase();
        db.Cases.Insert(new() { FullName = "JUAN PEREZ", Rut = "13.025.150-1", Office = Office.AvenidaArgentina, SourceSheet = "S", SourceRow = 1 });
        db.Cases.Insert(new() { FullName = "ANA SOTO", Rut = "16.487.222-K", Office = Office.AvenidaArgentina, SourceSheet = "S", SourceRow = 2 });
        var cambioDomicilio = new FakeCambioDomicilioRequestRepository();
        var urgent = new FakeUrgentRequestRepository();
        var model = Model(db, cambioDomicilio, urgent);

        model.OnGet();

        Assert.Equal(2, model.CasosCount);
        Assert.False(model.PuedeCambioDomicilio);
        Assert.Null(model.CambioDomicilioPendientes);
        Assert.False(model.PuedeF8);
        Assert.Null(model.F8Count);
    }

    [Fact]
    public void Con_claim_de_cambio_de_domicilio_usa_el_mismo_conteo_de_pendientes_que_su_propia_estadistica()
    {
        using var db = new SqliteTestDatabase();
        var cambioDomicilio = new FakeCambioDomicilioRequestRepository();
        cambioDomicilio.Insert(Pending("1", "A"));
        cambioDomicilio.Insert(Pending("2", "B"));
        var confirmed = Pending("3", "C");
        confirmed.Status = RequestStatus.Confirmed;
        cambioDomicilio.Insert(confirmed);
        var urgent = new FakeUrgentRequestRepository();
        var model = Model(db, cambioDomicilio, urgent, new Claim("mod:cambio-domicilio", "true"));

        model.OnGet();

        Assert.True(model.PuedeCambioDomicilio);
        Assert.Equal(2, model.CambioDomicilioPendientes);
        Assert.False(model.PuedeF8);
        Assert.Null(model.F8Count);
    }

    [Fact]
    public void Con_claim_de_F8_cuenta_todo_lo_que_no_esta_subido_a_conaset()
    {
        using var db = new SqliteTestDatabase();
        var cambioDomicilio = new FakeCambioDomicilioRequestRepository();
        var urgent = new FakeUrgentRequestRepository();
        urgent.Seed(Urgent("PENDIENTE"));
        urgent.Seed(Urgent("CREAR CERTIFICADO"));
        urgent.Seed(Urgent("SUBIDA A CONASET"));
        var model = Model(db, cambioDomicilio, urgent, new Claim("mod:f8-urgentes", "true"));

        model.OnGet();

        Assert.True(model.PuedeF8);
        Assert.Equal(2, model.F8Count);
        Assert.False(model.PuedeCambioDomicilio);
        Assert.Null(model.CambioDomicilioPendientes);
    }
}
