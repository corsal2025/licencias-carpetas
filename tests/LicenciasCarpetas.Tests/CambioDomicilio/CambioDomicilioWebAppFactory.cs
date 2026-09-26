using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>
/// Boots the real host (<see cref="Program"/>) against a throwaway SQLite file and a fake
/// authentication scheme, so access-control and startup tests exercise the real
/// <c>[Authorize(Policy = "CambioDomicilioAccess")]</c> pipeline instead of a hand-built page model.
/// The real cookie login flow is not driven here (would need a hashed password + antiforgery
/// handshake for no extra coverage) — <see cref="TestAuthHandler"/> fabricates the same claims
/// <c>Login.cshtml.cs</c> writes on a successful sign-in.
/// </summary>
public sealed class CambioDomicilioWebAppFactory : WebApplicationFactory<Program>
{
    // One dedicated subdirectory per factory instance, not the bare OS temp root — DatabaseBackup
    // writes a "backups" folder next to the db file on every startup, and a previous version of
    // this fixture deleted that folder from Path.GetTempPath() directly on teardown, which is a
    // machine-wide shared path other processes/test runs may also be using.
    private readonly string testRootDirectory = Path.Combine(Path.GetTempPath(), $"licencias-carpetas-webtest-{Guid.NewGuid():N}");
    private string dbPath => Path.Combine(testRootDirectory, "carpetas.db");
    private string uploadDirectory => Path.Combine(testRootDirectory, "uploads");
    private string exportDirectory => Path.Combine(testRootDirectory, "exports");

    /// <summary>Extra config overrides applied on top of the base host config (e.g. to blank out
    /// the whole "CambioDomicilio" section for the startup-without-config scenario).</summary>
    public Dictionary<string, string?> ExtraSettings { get; init; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development" (the WebApplicationFactory default) so Program.cs's browser-launch
        // Task.Run stays off — that block runs before app.Run(), which WebApplicationFactory does
        // not short-circuit.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Carpetas:SqliteDbPath", dbPath);
        builder.UseSetting("Carpetas:UploadDirectory", uploadDirectory);
        builder.UseSetting("Carpetas:ExportDirectory", exportDirectory);

        foreach (var (key, value) in ExtraSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            // Replaces the cookie scheme's DefaultScheme with the fake one below — Configure<T>
            // delegates run in registration order, so this call (added after Program.cs's own
            // AddAuthentication/AddCookie) wins.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>An <see cref="HttpClient"/> whose requests authenticate with the given
    /// "mod:cambio-domicilio" claim value ("true"/"false") via <see cref="TestAuthHandler"/>.</summary>
    public HttpClient CreateAuthenticatedClient(bool canAccessCambioDomicilio)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.ClaimHeader, canAccessCambioDomicilio ? "true" : "false");
        return client;
    }

    /// <summary>An authenticated client with a global role and, optionally, per-office claims
    /// (as <c>ClaimsFactory</c> writes them at login).</summary>
    public HttpClient CreateClientWithRole(string role, params string[] offices)
    {
        var client = CreateAuthenticatedClient(canAccessCambioDomicilio: true);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        if (offices.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.OfficesHeader, string.Join(",", offices));
        }

        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            // One recursive delete of this instance's own subdirectory (db + uploads + exports +
            // DatabaseBackup's "backups" folder, all scoped under testRootDirectory) — no other
            // process/test run shares this path, unlike the old bare-temp-root "backups" delete.
            TryDeleteDirectory(testRootDirectory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup: a lingering file handle (AV scanner, delayed SQLite pool
            // release) is not worth failing an otherwise-passing test suite over.
        }
    }

    /// <summary>Always authenticates the caller — real logged-in users always carry the
    /// "mod:cambio-domicilio" claim (true or false, see Login.cshtml.cs); there is no
    /// "authenticated but claim-less" state to simulate, so this mirrors that instead of
    /// modeling an anonymous request.</summary>
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string ClaimHeader = "X-Test-Cambio-Domicilio-Claim";
        public const string RoleHeader = "X-Test-Role";
        public const string OfficesHeader = "X-Test-Offices";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claimValue = Request.Headers.TryGetValue(ClaimHeader, out var values) ? values.ToString() : "true";
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "1"),
                new(ClaimTypes.Name, "test-operador"),
                new("mod:cambio-domicilio", claimValue)
            };
            if (Request.Headers.TryGetValue(RoleHeader, out var role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            if (Request.Headers.TryGetValue(OfficesHeader, out var offices))
            {
                claims.AddRange(offices.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(office => new Claim("office", office)));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
