using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.Dashboard.Pages.CambioDomicilio;
using LicenciasCarpetas.Tests.CambioDomicilio.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>The per-row "Solicitar certificado" button: sends the predetermined request email for
/// one contributor to <see cref="CambioDomicilioOptions.CertificateRequestEmailAddress"/> carrying
/// that person's name and RUT, then marks the case notified so it can't be re-sent.</summary>
public class SolicitarCertificadoTests
{
    private sealed class RecordingMailSender : IMailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
        {
            Sent.Add((toAddress, subject, body));
            return Task.CompletedTask;
        }
    }

    private static (CertificadoModel Model, RecordingMailSender Mail, FakeCambioDomicilioRequestRepository Repo)
        BuildModel(CambioDomicilioOptions? options = null)
    {
        var repo = new FakeCambioDomicilioRequestRepository();
        var mail = new RecordingMailSender();
        options ??= new CambioDomicilioOptions { CertificateRequestEmailAddress = "javiera.sanchez@munivalpo.cl" };

        var routing = new AddressChangeRoutingService(
            repo,
            new FakeDiscardedEmailRepository(),
            new ComunaDirectory(),
            new NoopMailSender(),
            new NoopEmailMover(),
            new NoopUserRepository(),
            [],
            options,
            NullLogger<AddressChangeRoutingService>.Instance);

        var model = new CertificadoModel(repo, routing, mail, options)
        {
            PageContext = new PageContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new PageActionDescriptor()))
        };
        return (model, mail, repo);
    }

    private static long SeedCertificadoCase(FakeCambioDomicilioRequestRepository repo, string? name, string? rut) =>
        repo.Insert(new PersonRequest
        {
            SourceMessageId = "msg-1",
            SourceSubject = "Solicitud",
            SourceSender = "oficina@municatemu.cl",
            FullName = name,
            Rut = rut,
            Comuna = "CATEMU",
            Destination = CaseDestination.Certificado,
        });

    [Fact]
    public async Task Sends_the_predetermined_request_with_name_and_rut_to_the_configured_address()
    {
        var (model, mail, repo) = BuildModel();
        var id = SeedCertificadoCase(repo, "GUSTAVO PEÑA CASTRO", "18.785.387-7");

        var result = await model.OnPostSolicitarCertificadoAsync(id);

        Assert.IsType<PageResult>(result);
        Assert.False(model.MessageIsError);
        var sent = Assert.Single(mail.Sent, m => m.To == "javiera.sanchez@munivalpo.cl");
        Assert.Contains("GUSTAVO PEÑA CASTRO", sent.Subject);
        Assert.Contains("18.785.387-7", sent.Subject);
        Assert.Contains("GUSTAVO PEÑA CASTRO", sent.Body);
        Assert.Contains("18.785.387-7", sent.Body);
        // La comuna no va en el correo: todos estos casos son personas de Valparaíso.
        Assert.DoesNotContain("CATEMU", sent.Body);
        Assert.NotNull(repo.FindById(id)!.CertificadoNotifiedAt);
    }

    [Fact]
    public async Task Cannot_be_sent_twice_for_the_same_case()
    {
        var (model, mail, repo) = BuildModel();
        var id = SeedCertificadoCase(repo, "GUSTAVO PEÑA CASTRO", "18.785.387-7");

        await model.OnPostSolicitarCertificadoAsync(id);
        var second = await model.OnPostSolicitarCertificadoAsync(id);

        Assert.IsType<PageResult>(second);
        Assert.True(model.MessageIsError);
        Assert.Single(mail.Sent.Where(m => m.To == "javiera.sanchez@munivalpo.cl"));
    }

    [Fact]
    public async Task Refuses_when_name_or_rut_is_missing()
    {
        var (model, mail, repo) = BuildModel();
        var id = SeedCertificadoCase(repo, name: null, rut: "18.785.387-7");

        await model.OnPostSolicitarCertificadoAsync(id);

        Assert.True(model.MessageIsError);
        Assert.Empty(mail.Sent);
        Assert.Null(repo.FindById(id)!.CertificadoNotifiedAt);
    }
}
