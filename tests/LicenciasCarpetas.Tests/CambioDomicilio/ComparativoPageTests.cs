using System.Net;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>Change "sgl-kpi-dashboard-comparativo": the page renders every office and exports.</summary>
public class ComparativoPageTests : IClassFixture<CambioDomicilioWebAppFactory>
{
    private readonly CambioDomicilioWebAppFactory factory;

    public ComparativoPageTests(CambioDomicilioWebAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task Shows_the_three_offices_for_management()
    {
        factory.Services.GetRequiredService<IFolderCaseRepository>().Insert(new FolderCase
        {
            FullName = "X", Rut = "11.111.111-1", Office = Office.Placilla,
            CitationDate = new DateOnly(2026, 3, 2), FinalDecision = FinalDecision.Otorgado
        });
        using var client = factory.CreateClientWithRole("Jefatura");

        var body = WebUtility.HtmlDecode(await (await client.GetAsync("/Estadisticas/Comparativo?year=2026&kind=Mes&index=3")).Content.ReadAsStringAsync());

        Assert.Contains("Av. Argentina", body);
        Assert.Contains("Placilla", body);
        Assert.Contains("Merc. Puerto", body);
        Assert.Contains("Comparativo por sede", body);
    }

    [Fact]
    public async Task Exports_a_workbook()
    {
        using var client = factory.CreateClientWithRole("Administrador");

        var response = await client.GetAsync("/Estadisticas/Comparativo?handler=Export&year=2026&kind=Anio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Is_denied_to_users_without_a_management_role()
    {
        using var client = factory.CreateClientWithRole("Administrativo");

        var response = await client.GetAsync("/Estadisticas/Comparativo");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
