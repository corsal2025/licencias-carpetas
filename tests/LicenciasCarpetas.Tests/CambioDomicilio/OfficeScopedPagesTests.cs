using System.Net;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>Change "sgl-roles-por-sede", through the real host: a user limited to Placilla neither
/// sees nor reaches Mercado Puerto's cases, whatever the URL.</summary>
public class OfficeScopedPagesTests : IClassFixture<CambioDomicilioWebAppFactory>
{
    private readonly CambioDomicilioWebAppFactory factory;
    private readonly long placillaId;
    private readonly long puertoId;

    public OfficeScopedPagesTests(CambioDomicilioWebAppFactory factory)
    {
        this.factory = factory;
        var cases = factory.Services.GetRequiredService<IFolderCaseRepository>();
        placillaId = cases.Insert(Case("ROSA PLACILLA", Office.Placilla));
        puertoId = cases.Insert(Case("TOMAS PUERTO", Office.MercadoPuerto));
    }

    [Fact]
    public async Task The_case_list_only_shows_the_users_offices()
    {
        using var client = factory.CreateClientWithRole("Administrativo", "Placilla");

        var body = await (await client.GetAsync("/Index")).Content.ReadAsStringAsync();

        Assert.Contains("ROSA PLACILLA", body);
        Assert.DoesNotContain("TOMAS PUERTO", body);
    }

    [Fact]
    public async Task The_audit_log_of_another_offices_case_is_a_404()
    {
        using var client = factory.CreateClientWithRole("Administrativo", "Placilla");

        var own = await client.GetAsync($"/Index?handler=AuditLog&id={placillaId}");
        var other = await client.GetAsync($"/Index?handler=AuditLog&id={puertoId}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task The_export_only_contains_the_users_offices()
    {
        using var client = factory.CreateClientWithRole("Coordinador", "Placilla");

        var bytes = await (await client.GetAsync("/Index?handler=Export")).Content.ReadAsByteArrayAsync();

        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var text = string.Join("|", workbook.Worksheets.First().CellsUsed().Select(c => c.GetString()));
        Assert.Contains("ROSA PLACILLA", text);
        Assert.DoesNotContain("TOMAS PUERTO", text);
    }

    [Fact]
    public async Task Global_search_hides_other_offices_but_management_sees_everything()
    {
        using var restricted = factory.CreateClientWithRole("Administrativo", "Placilla");
        using var jefatura = factory.CreateClientWithRole("Jefatura");

        var limited = await (await restricted.GetAsync("/api/global-search?q=PUERTO")).Content.ReadAsStringAsync();
        var all = await (await jefatura.GetAsync("/api/global-search?q=PUERTO")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("TOMAS PUERTO", limited);
        Assert.Contains("TOMAS PUERTO", all);
    }

    [Fact]
    public async Task Comparativo_only_lists_the_coordinators_offices()
    {
        using var client = factory.CreateClientWithRole("Coordinador", "Placilla");

        var body = WebUtility.HtmlDecode(await (await client.GetAsync("/Estadisticas/Comparativo")).Content.ReadAsStringAsync());

        Assert.DoesNotContain("Merc. Puerto", body);
        Assert.Contains("Placilla", body);
    }

    private static FolderCase Case(string name, Office office) => new()
    {
        FullName = name,
        Rut = "12.345.678-5",
        Office = office,
        CitationDate = DateOnly.FromDateTime(DateTime.Today)
    };
}
