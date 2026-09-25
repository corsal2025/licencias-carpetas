namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>Proposal "sgl-branding-y-diagrama": the app never showed who built it. The layout
/// (used by every authenticated page) and the login page (Layout = null, its own markup) must
/// both carry the credit independently.</summary>
public class AuthorshipFooterTests : IClassFixture<CambioDomicilioWebAppFactory>
{
    private readonly CambioDomicilioWebAppFactory factory;

    public AuthorshipFooterTests(CambioDomicilioWebAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task The_authenticated_layout_shows_the_authorship_credit()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true);

        var body = await (await client.GetAsync("/Inicio")).Content.ReadAsStringAsync();

        Assert.Contains("Desarrollado por Raúl Salazar", body);
    }

    [Fact]
    public async Task The_login_page_shows_the_authorship_credit()
    {
        using var client = factory.CreateClient();

        var body = await (await client.GetAsync("/Login")).Content.ReadAsStringAsync();

        Assert.Contains("Desarrollado por Raúl Salazar", body);
    }
}
