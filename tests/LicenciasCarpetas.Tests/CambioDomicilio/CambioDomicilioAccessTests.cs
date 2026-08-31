using System.Net;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>Dashboard spec's Access Control requirement: the "CambioDomicilioAccess" policy
/// gates the page for real, not just the nav link that hides it.</summary>
public class CambioDomicilioAccessTests : IClassFixture<CambioDomicilioWebAppFactory>
{
    private readonly CambioDomicilioWebAppFactory factory;

    public CambioDomicilioAccessTests(CambioDomicilioWebAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task An_authorized_operator_reaches_the_index_page()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true);

        var response = await client.GetAsync("/CambioDomicilio/Index");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>This page used to reference ~/js/vendor/chart.umd.js and ~/js/estadisticas.js,
    /// neither of which ever existed in the repo — it rendered a 200 with no useful content for an
    /// unknown stretch of time, with nothing here to catch it. Rewritten to reuse F8's CSS-only
    /// chart components (dashboard.css); this guards both that a render-time exception (e.g. a
    /// null-ref on one of the statistics model's lists) doesn't creep back in, and specifically
    /// that neither of those two missing files sneaks back into the markup.</summary>
    [Fact]
    public async Task An_authorized_operator_reaches_a_working_statistics_page()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true);

        var response = await client.GetAsync("/CambioDomicilio/Estadisticas");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("chart.umd.js", body);
        Assert.DoesNotContain("estadisticas.js", body);
        Assert.Contains("stats-grid", body);
    }

    /// <summary>Dashboard spec's Internal Navigation scenario "Unauthorized user does not see the
    /// entry": the sidebar nav must hide the Cambio de Domicilio links from an authenticated user
    /// who lacks the "mod:cambio-domicilio" claim — not just gate the pages behind them.</summary>
    [Fact]
    public async Task The_nav_hides_the_cambio_domicilio_entry_from_a_user_without_the_claim()
    {
        using var denied = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: false);
        using var allowed = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true);

        var deniedBody = await (await denied.GetAsync("/Inicio")).Content.ReadAsStringAsync();
        var allowedBody = await (await allowed.GetAsync("/Inicio")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("Enviar Cambio de Domicilio", deniedBody);
        Assert.DoesNotContain("Solicitar Cambios de Domicilio", deniedBody);
        Assert.Contains("Enviar Cambio de Domicilio", allowedBody); // sanity: the entry IS there when authorized
    }

    /// <summary>Same shape as an F8-only operator: authenticated, but without the
    /// "mod:cambio-domicilio" claim — the policy must refuse without ever throwing.</summary>
    [Fact]
    public async Task An_operator_without_the_module_claim_is_refused_without_an_unhandled_exception()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: false);

        var response = await client.GetAsync("/CambioDomicilio/Index");

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect or HttpStatusCode.Found,
            $"expected 403 or a redirect, got {(int)response.StatusCode} {response.StatusCode}");
    }
}
