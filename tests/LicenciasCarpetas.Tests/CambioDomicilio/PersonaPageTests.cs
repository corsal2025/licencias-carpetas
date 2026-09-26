using System.Net;
using System.Text.RegularExpressions;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>Change "sgl-ficha-persona": POST-redirect-GET so the RUT never lands in a URL.</summary>
public class PersonaPageTests : IClassFixture<CambioDomicilioWebAppFactory>
{
    private readonly CambioDomicilioWebAppFactory factory;

    public PersonaPageTests(CambioDomicilioWebAppFactory factory) => this.factory = factory;

    [Fact]
    public async Task A_valid_rut_redirects_without_the_rut_and_then_shows_the_file()
    {
        using var client = factory.CreateClientWithRole("Administrativo", "Placilla");
        factory.Services.GetRequiredService<IFolderCaseRepository>().Insert(new FolderCase
        {
            FullName = "MARIA SOTO", Rut = "12.345.678-5", Office = Office.Placilla,
            CitationDate = new DateOnly(2026, 4, 2)
        });

        var post = await PostRut(client, "12345678-5");

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var location = post.Headers.Location!.ToString();
        Assert.DoesNotContain("12345678", location);
        Assert.DoesNotContain("345", location);

        var body = await (await client.GetAsync(location)).Content.ReadAsStringAsync();
        Assert.Contains("MARIA SOTO", body);
        Assert.Contains("12.345.678-5", body);
        Assert.Contains("Placilla", body);
    }

    [Fact]
    public async Task Cases_of_other_offices_are_left_out()
    {
        factory.Services.GetRequiredService<IFolderCaseRepository>().Insert(new FolderCase
        {
            FullName = "PEDRO ROJAS", Rut = "11.111.111-1", Office = Office.MercadoPuerto,
            CitationDate = new DateOnly(2026, 4, 2)
        });
        using var client = factory.CreateClientWithRole("Administrativo", "Placilla");

        var post = await PostRut(client, "11.111.111-1");
        var body = await (await client.GetAsync(post.Headers.Location)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("PEDRO ROJAS", body);
        Assert.DoesNotContain("Merc. Puerto", body);
    }

    [Fact]
    public async Task An_invalid_check_digit_shows_an_error()
    {
        using var client = factory.CreateAuthenticatedClient(canAccessCambioDomicilio: true);

        var post = await PostRut(client, "12.345.678-0");
        var body = WebUtility.HtmlDecode(await (await client.GetAsync(post.Headers.Location)).Content.ReadAsStringAsync());

        Assert.Contains("RUT inválido", body);
    }

    private static async Task<HttpResponseMessage> PostRut(HttpClient client, string rut)
    {
        var page = await (await client.GetAsync("/Persona")).Content.ReadAsStringAsync();
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        return await client.PostAsync("/Persona", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["rut"] = rut,
            ["__RequestVerificationToken"] = token
        }));
    }
}
