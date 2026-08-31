using System.Security.Claims;
using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Solicitar;

/// <summary>Exercises the "Solicitar" button on the outbound-requests listing (IndexModel), which
/// sends a Borrador request directly from the table — same
/// send path (OutboundRequestSender) as NuevaModel.OnPostEnviar, just reached from a shorter click.</summary>
public class IndexModelTests
{
    private const long UserId = 42;

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

    private sealed class RecordingEmailSender : IMailSender
    {
        public string? To { get; private set; }
        public int CallCount { get; private set; }

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
        {
            CallCount++;
            To = toAddress;
            return Task.CompletedTask;
        }
    }

    private sealed record Fixture(
        IndexModel Model,
        FakeOutboundAddressChangeRequestRepository Repository,
        FakeComunaContactRepository ComunaContacts,
        RecordingEmailSender EmailSender);

    private static Fixture BuildModel(SqliteTestDatabase db, CambioDomicilioOptions? options = null)
    {
        var repository = new FakeOutboundAddressChangeRequestRepository();
        var comunaContacts = new FakeComunaContactRepository();
        var emailSender = new RecordingEmailSender();
        var sender = new OutboundRequestSender(repository, comunaContacts, emailSender);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test"))
        };

        var model = new IndexModel(repository, sender, db.Cases, options ?? new CambioDomicilioOptions())
        {
            PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor())),
            TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider())
        };

        return new Fixture(model, repository, comunaContacts, emailSender);
    }

    private static long SeedDraft(FakeOutboundAddressChangeRequestRepository repository, string? comuna = "Quillota") =>
        repository.Insert(new OutboundAddressChangeRequest
        {
            FullName = "GUSTAVO PEÑA CASTRO",
            Rut = "18.785.387-7",
            DestinationComuna = comuna!,
            CreatedByUserId = UserId
        });

    [Fact]
    public async Task OnPostSolicitar_DraftWithRegisteredContact_SendsAndMarksEnviada()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedDraft(fixture.Repository);

        var result = await fixture.Model.OnPostSolicitar(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, fixture.EmailSender.CallCount);
        Assert.Equal("contacto@muniquillota.cl", fixture.EmailSender.To);
        Assert.Equal(OutboundRequestStatus.Enviada, fixture.Repository.FindById(id)!.Status);
    }

    [Fact]
    public async Task OnPostSolicitar_NoRegisteredContact_KeepsItAsDraftAndDoesNotSend()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        var id = SeedDraft(fixture.Repository, comuna: "Comuna Sin Contacto");

        var result = await fixture.Model.OnPostSolicitar(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(0, fixture.EmailSender.CallCount);
        Assert.Equal(OutboundRequestStatus.Borrador, fixture.Repository.FindById(id)!.Status);
    }

    [Fact]
    public async Task OnPostSolicitar_AlreadySentRequest_DoesNotSendAgain()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        fixture.ComunaContacts.Upsert(new ComunaContact { Comuna = "Quillota", Email = "contacto@muniquillota.cl" });
        var id = SeedDraft(fixture.Repository);
        await fixture.Model.OnPostSolicitar(id);
        Assert.Equal(1, fixture.EmailSender.CallCount);

        var result = await fixture.Model.OnPostSolicitar(id);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(1, fixture.EmailSender.CallCount);
    }

    [Fact]
    public async Task OnPostSolicitar_UnknownId_DoesNotThrow()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);

        var result = await fixture.Model.OnPostSolicitar(999_999);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(0, fixture.EmailSender.CallCount);
    }

    [Fact]
    public void OnPostGuardarEstado_SavesWorkflowStateOnTheRequest()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        var id = SeedDraft(fixture.Repository);

        var result = fixture.Model.OnPostGuardarEstado(id, FolderState.SubidaConF8);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(FolderState.SubidaConF8, fixture.Repository.FindById(id)!.WorkflowState);
    }

    /// <summary>Cuando la solicitud nació del botón "Solicitar" en Casos, cambiar su Estado acá
    /// también actualiza el FolderState del FolderCase de origen — el operador no repite el cambio
    /// en las dos pantallas.</summary>
    [Fact]
    public void OnPostGuardarEstado_RequestWithSourceCase_PropagatesToTheFolderCase()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        var caseId = db.Cases.Insert(new FolderCase
        {
            FullName = "GUSTAVO PEÑA CASTRO",
            Rut = "18.785.387-7",
            Office = Office.AvenidaArgentina,
            FolderState = FolderState.CambioDomicilioSolicitado
        });
        var id = fixture.Repository.Insert(new OutboundAddressChangeRequest
        {
            FullName = "GUSTAVO PEÑA CASTRO",
            Rut = "18.785.387-7",
            DestinationComuna = "Quillota",
            CreatedByUserId = UserId,
            SourceFolderCaseId = caseId
        });

        fixture.Model.OnPostGuardarEstado(id, FolderState.CambioDomicilioSubidoAConaset);

        Assert.Equal(FolderState.CambioDomicilioSubidoAConaset, db.Cases.FindById(caseId)!.FolderState);
    }

    /// <summary>Sin caso de origen (creada desde "+ Nueva Solicitud"), no hay nada que propagar —
    /// el estado de la solicitud igual se guarda.</summary>
    [Fact]
    public void OnPostGuardarEstado_RequestWithoutSourceCase_JustSavesItsOwnState()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        var id = SeedDraft(fixture.Repository);

        var result = fixture.Model.OnPostGuardarEstado(id, FolderState.CambioDomicilioSubidoAConaset);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(FolderState.CambioDomicilioSubidoAConaset, fixture.Repository.FindById(id)!.WorkflowState);
    }

    /// <summary>Un estado fuera de WorkflowStateCatalog.Options (ej. PrimeraLicencia, que no está
    /// en la lista reducida de 5) se rechaza — no se guarda ni en la solicitud ni en el caso.</summary>
    [Fact]
    public void OnPostGuardarEstado_StateOutsideTheCatalog_IsRejected()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        var id = SeedDraft(fixture.Repository);

        var result = fixture.Model.OnPostGuardarEstado(id, FolderState.PrimeraLicencia);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(fixture.Repository.FindById(id)!.WorkflowState);
    }

    /// <summary>Volver el desplegable a "—" (null) limpia el campo de la solicitud, pero NO debe
    /// tocar el FolderState del caso de origen — ese es el registro autoritativo y puede estar en
    /// un estado fuera de este catálogo reducido (ej. "1° LICENCIA"); propagar null lo borraría en
    /// silencio.</summary>
    [Fact]
    public void OnPostGuardarEstado_ClearingToNull_DoesNotClobberTheSourceFolderCase()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        var caseId = db.Cases.Insert(new FolderCase
        {
            FullName = "GUSTAVO PEÑA CASTRO",
            Rut = "18.785.387-7",
            Office = Office.AvenidaArgentina,
            FolderState = FolderState.PrimeraLicencia
        });
        var id = fixture.Repository.Insert(new OutboundAddressChangeRequest
        {
            FullName = "GUSTAVO PEÑA CASTRO",
            Rut = "18.785.387-7",
            DestinationComuna = "Quillota",
            CreatedByUserId = UserId,
            SourceFolderCaseId = caseId,
            WorkflowState = FolderState.SubidaConF8
        });

        fixture.Model.OnPostGuardarEstado(id, null);

        Assert.Null(fixture.Repository.FindById(id)!.WorkflowState);
        Assert.Equal(FolderState.PrimeraLicencia, db.Cases.FindById(caseId)!.FolderState);
    }

    /// <summary>El caso de origen fue borrado mientras tanto — el estado de la solicitud igual se
    /// guarda, la propagación simplemente no tiene a quién escribirle.</summary>
    [Fact]
    public void OnPostGuardarEstado_SourceCaseNoLongerExists_StillSavesTheRequestsOwnState()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db);
        var id = fixture.Repository.Insert(new OutboundAddressChangeRequest
        {
            FullName = "GUSTAVO PEÑA CASTRO",
            Rut = "18.785.387-7",
            DestinationComuna = "Quillota",
            CreatedByUserId = UserId,
            SourceFolderCaseId = 999_999
        });

        var result = fixture.Model.OnPostGuardarEstado(id, FolderState.SubidaConF8);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal(FolderState.SubidaConF8, fixture.Repository.FindById(id)!.WorkflowState);
    }

    private static string WriteWorkbook(params (string Rut, string Nombre, string Comuna)[] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"solicitar-sync-{Guid.NewGuid():N}.xlsx");
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.Worksheets.Add("Solicitudes");
        sheet.Cell(1, 1).Value = "RUT";
        sheet.Cell(1, 2).Value = "NOMBRE";
        sheet.Cell(1, 3).Value = "COMUNA";
        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = rows[i].Rut;
            sheet.Cell(i + 2, 2).Value = rows[i].Nombre;
            sheet.Cell(i + 2, 3).Value = rows[i].Comuna;
        }
        workbook.SaveAs(path);
        return path;
    }

    [Fact]
    public void OnPostSincronizar_NoPathConfigured_ReportsNoOpWithoutThrowing()
    {
        using var db = new SqliteTestDatabase();
        var fixture = BuildModel(db, new CambioDomicilioOptions { SolicitarMatrizExcelPath = null });

        var result = fixture.Model.OnPostSincronizar();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Empty(fixture.Repository.GetAll());
    }

    [Fact]
    public void OnPostSincronizar_NewRowsInTheWorkbook_CreatesOneDraftPerRut()
    {
        var path = WriteWorkbook(
            ("18.785.387-7", "GUSTAVO PEÑA CASTRO", "Quillota"),
            ("13.025.150-1", "JUAN PEREZ", "Catemu"));
        try
        {
            using var db = new SqliteTestDatabase();
            var fixture = BuildModel(db, new CambioDomicilioOptions { SolicitarMatrizExcelPath = path });

            var result = fixture.Model.OnPostSincronizar();

            Assert.IsType<RedirectToPageResult>(result);
            var created = fixture.Repository.GetAll();
            Assert.Equal(2, created.Count);
            Assert.Contains(created, r => r.Rut == "18.785.387-7" && r.DestinationComuna == "Quillota");
            Assert.Contains(created, r => r.Rut == "13.025.150-1" && r.DestinationComuna == "Catemu");
            Assert.All(created, r => Assert.Equal(OutboundRequestStatus.Borrador, r.Status));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Sincronizar dos veces con el mismo Excel (el archivo nunca se vacía ni se marca
    /// fila por fila) no debe duplicar la solicitud ya cargada.</summary>
    [Fact]
    public void OnPostSincronizar_RunTwiceOnTheSameWorkbook_DoesNotDuplicateAnAlreadyLoadedRut()
    {
        var path = WriteWorkbook(("18.785.387-7", "GUSTAVO PEÑA CASTRO", "Quillota"));
        try
        {
            using var db = new SqliteTestDatabase();
            var fixture = BuildModel(db, new CambioDomicilioOptions { SolicitarMatrizExcelPath = path });

            fixture.Model.OnPostSincronizar();
            fixture.Model.OnPostSincronizar();

            Assert.Single(fixture.Repository.GetAll());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
