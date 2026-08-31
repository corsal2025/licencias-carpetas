using System.Security.Claims;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8.Services;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;
using LicenciasCarpetas.Tests.CambioDomicilio.Solicitar;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace LicenciasCarpetas.Tests;

/// <summary>Exercises IndexModel.OnPostSolicitarCambioDomicilio directly (no HTTP): the one-click
/// "Solicitar" button on Casos that creates an outbound Cambio de Domicilio request and sends it,
/// reusing the same fakes written for NuevaModelTests.</summary>
public class IndexModelSolicitarCambioDomicilioTests
{
    private const long UserId = 42;

    private sealed class NoopExporter : IExcelCaseExporter
    {
        public byte[] Export(IReadOnlyList<FolderCase> cases, string sheetTitle) => [];
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private sealed class FakeComunaContactRepository : IComunaContactRepository
    {
        private readonly List<ComunaContact> _contacts = [];
        private long _nextId = 1;

        public void EnsureSchema() { }
        public void EnsureSeed(string? csvPath = null) { }

        public void Upsert(ComunaContact contact)
        {
            contact.Id = _nextId++;
            _contacts.Add(contact);
        }

        public void Update(long id, string comuna, string contactEmail, string? notes = null)
        {
            var existing = _contacts.FirstOrDefault(c => c.Id == id);
            if (existing != null)
            {
                existing.Comuna = comuna;
                existing.Email = contactEmail;
                existing.Notes = notes;
            }
        }

        public IReadOnlyList<ComunaContact> All(string? search = null) => _contacts;

        public void Delete(long id) => _contacts.RemoveAll(c => c.Id == id);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public string? To { get; private set; }
        public string? Body { get; private set; }
        public int CallCount { get; private set; }

        public Task SendAsync(string to, string subject, string body,
            IReadOnlyList<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            To = to;
            Body = body;
            return Task.CompletedTask;
        }
    }

    private sealed record Fixture(
        IndexModel Model,
        SqliteTestDatabase Db,
        FakeOutboundAddressChangeRequestRepository OutboundRequests,
        FakeComunaContactRepository ComunaContacts,
        RecordingEmailSender EmailSender);

    private static Fixture BuildModel(SqliteTestDatabase db, bool hasCambioDomicilioAccess = true)
    {
        var outboundRequests = new FakeOutboundAddressChangeRequestRepository();
        var comunaContacts = new FakeComunaContactRepository();
        var emailSender = new RecordingEmailSender();
        var sender = new OutboundRequestSender(outboundRequests, comunaContacts, emailSender);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, UserId.ToString()) };
        if (hasCambioDomicilioAccess)
        {
            claims.Add(new Claim("mod:cambio-domicilio", "true"));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };

        var model = new IndexModel(db.Cases, new NoopExporter(), new CarpetasOptions(),
            outboundRequests, sender, new FakeUrgentRequestRepositoryForCasos(), comunaContacts)
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor())),
            TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider())
        };

        return new Fixture(model, db, outboundRequests, comunaContacts, emailSender);
    }

    private static long SeedCase(SqliteTestDatabase db, FolderState? state, string? comuna)
    {
        return db.Cases.Insert(new FolderCase
        {
            FullName = "GUSTAVO PEÑA CASTRO",
            Rut = "18.785.387-7",
            Office = Office.AvenidaArgentina,
            FolderState = state,
            CambioDomicilioComuna = comuna
        });
    }

    [Fact]
    public async Task Ready_case_with_registered_contact_creates_and_sends_the_request()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedCase(db, FolderState.CambioDomicilioSolicitado, "Quillota");

        var result = await fixture.Model.OnPostSolicitarCambioDomicilio(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, fixture.EmailSender.CallCount);
        Assert.Equal("contacto@muniquillota.cl", fixture.EmailSender.To);

        var stored = Assert.Single(fixture.OutboundRequests.GetAll());
        Assert.Equal("GUSTAVO PEÑA CASTRO", stored.FullName);
        Assert.Equal("18.785.387-7", stored.Rut);
        Assert.Equal("Quillota", stored.DestinationComuna);
        Assert.Equal(OutboundRequestStatus.Enviada, stored.Status);
        Assert.Equal(UserId, stored.SentByUserId);

        // Texto fijo pedido por el operador (cita del artículo 14 del Decreto 170) — Nombre y RUT
        // son los únicos datos que cambian según de quién se pida la carpeta.
        Assert.NotNull(fixture.EmailSender.Body);
        Assert.Contains("artículo 14 del Decreto N.º 170", fixture.EmailSender.Body);
        Assert.Contains("Nombre: GUSTAVO PEÑA CASTRO", fixture.EmailSender.Body);
        Assert.Contains("RUT: 18.785.387-7", fixture.EmailSender.Body);
    }

    [Fact]
    public async Task Blank_comuna_blocks_without_inserting_or_sending()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        var id = SeedCase(db, FolderState.CambioDomicilioSolicitado, comuna: null);

        var result = await fixture.Model.OnPostSolicitarCambioDomicilio(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(0, fixture.EmailSender.CallCount);
        Assert.Empty(fixture.OutboundRequests.GetAll());
    }

    /// <summary>El input de Comuna viaja con el form de "Guardar", no con el de "Solicitar" — si el
    /// operador la tipeó y apretó Solicitar sin guardar antes, la base todavía no tiene ese valor.
    /// El handler debe aceptar la comuna posteada directamente por el form de Solicitar (ver
    /// prepararSolicitar en Index.cshtml) para que un solo clic alcance, sin pasar por Guardar.</summary>
    [Fact]
    public async Task Comuna_posted_directly_by_the_solicitar_form_is_used_even_when_not_saved_yet()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedCase(db, FolderState.CambioDomicilioSolicitado, comuna: null);

        var result = await fixture.Model.OnPostSolicitarCambioDomicilio(id, comuna: "Quillota");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, fixture.EmailSender.CallCount);
        var stored = Assert.Single(fixture.OutboundRequests.GetAll());
        Assert.Equal("Quillota", stored.DestinationComuna);
        // Queda persistida en el caso también, como si se hubiera guardado — así una segunda
        // consulta del caso (o un reintento) ve la misma comuna sin depender de un Guardar previo.
        var persisted = db.Cases.FindById(id)!;
        Assert.Equal("Quillota", persisted.CambioDomicilioComuna);
    }

    /// <summary>El desplegable Estado carpeta viaja con el form de "Guardar", no con el de
    /// "Solicitar" — si el operador recién lo eligió y apretó Solicitar sin guardar antes, la base
    /// todavía tiene el estado viejo. El handler debe aceptar el estado posteado directamente por
    /// el form de Solicitar (ver prepararSolicitar en Index.cshtml) para que un solo clic alcance.</summary>
    [Fact]
    public async Task Estado_posted_directly_by_the_solicitar_form_is_used_even_when_not_saved_yet()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedCase(db, state: null, comuna: null);

        var result = await fixture.Model.OnPostSolicitarCambioDomicilio(
            id, comuna: "Quillota", estado: "CambioDomicilioSolicitado");

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, fixture.EmailSender.CallCount);
        var stored = Assert.Single(fixture.OutboundRequests.GetAll());
        Assert.Equal("Quillota", stored.DestinationComuna);
        var persisted = db.Cases.FindById(id)!;
        Assert.Equal(FolderState.CambioDomicilioSolicitado, persisted.FolderState);
        Assert.Equal("Quillota", persisted.CambioDomicilioComuna);
    }

    [Fact]
    public async Task Wrong_folder_state_blocks_even_with_a_comuna_filled_in()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedCase(db, FolderState.PrimeraLicencia, "Quillota");

        var result = await fixture.Model.OnPostSolicitarCambioDomicilio(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(0, fixture.EmailSender.CallCount);
        Assert.Empty(fixture.OutboundRequests.GetAll());
    }

    [Fact]
    public async Task Comuna_without_a_registered_contact_blocks_sending_but_keeps_the_draft_for_a_retry()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        var id = SeedCase(db, FolderState.CambioDomicilioSolicitado, "Comuna Sin Contacto");

        var result = await fixture.Model.OnPostSolicitarCambioDomicilio(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(0, fixture.EmailSender.CallCount);
        var stored = Assert.Single(fixture.OutboundRequests.GetAll());
        Assert.Equal(OutboundRequestStatus.Borrador, stored.Status);
    }

    [Fact]
    public async Task User_without_the_module_claim_is_blocked_even_with_everything_else_ready()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db, hasCambioDomicilioAccess: false);
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedCase(db, FolderState.CambioDomicilioSolicitado, "Quillota");

        var result = await fixture.Model.OnPostSolicitarCambioDomicilio(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(0, fixture.EmailSender.CallCount);
        Assert.Empty(fixture.OutboundRequests.GetAll());
    }

    [Fact]
    public async Task Case_that_already_has_a_sent_request_is_not_solicited_twice()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedCase(db, FolderState.CambioDomicilioSolicitado, "Quillota");

        var firstResult = await fixture.Model.OnPostSolicitarCambioDomicilio(id);
        Assert.IsType<RedirectToPageResult>(firstResult);
        Assert.Equal(1, fixture.EmailSender.CallCount);

        var secondResult = await fixture.Model.OnPostSolicitarCambioDomicilio(id);

        Assert.IsType<RedirectToPageResult>(secondResult);
        Assert.Equal(1, fixture.EmailSender.CallCount);
        Assert.Single(fixture.OutboundRequests.GetAll());
    }
}
