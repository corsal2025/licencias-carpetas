using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Notifications;
using LicenciasCarpetas.CambioDomicilio.Reporting;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.Dashboard.Auth;
using LicenciasCarpetas.Dashboard.Pages.CambioDomicilio;
using LicenciasCarpetas.Tests.CambioDomicilio.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>
/// Routing spec's Cycle Reporting scenario, driven end-to-end from
/// <see cref="IndexModel.OnPostSyncNowAsync"/> (the "Sincronizar ahora" button) down through
/// <see cref="CambioDomicilioSyncService.RunCycleAsync"/> and
/// <see cref="AddressChangeRoutingService.ProcessIncomingRequest"/>, with a fake
/// <see cref="IEmailReader"/> standing in for the real EWS transport (<see cref="IEwsClient"/> is
/// never resolved on this path — see EwsClient.cs's comment on why it stays unconstructed until an
/// actual sync needs it — so a fake reader is the correct seam here, not a fake EwsClient).
/// </summary>
public class OnPostSyncNowTests
{
    private const string CatemuDomain = "municatemu.cl";
    private const string OwnDomain = "munivalpo.cl";

    [Fact]
    public async Task OnPostSyncNowAsync_OneCycle_CreatesDiscardsAndFlagsForReviewAsExpected()
    {
        var csvPath = WriteComunaCsv();
        try
        {
            var repository = new FakeCambioDomicilioRequestRepository();
            var discardedRepository = new FakeDiscardedEmailRepository();
            var options = new CambioDomicilioOptions
            {
                OwnDomain = OwnDomain,
                ComunaDirectoryCsvPath = csvPath,
                SourceFolderName = "CARP. PARA PEDIR",
                ConfirmationFolderName = "CARP. YA SUBIDAS"
            };

            var routingService = new AddressChangeRoutingService(
                repository,
                discardedRepository,
                new ComunaDirectory(),
                new NoopMailSender(),
                new NoopEmailMover(),
                new NoopUserRepository(),
                [],
                options,
                NullLogger<AddressChangeRoutingService>.Instance);

            var reader = new FakeEmailReader(
            [
                // Recognized comuna domain, full data extractable -> a new Pending case.
                Email("msg-created", CatemuDomain, "GUSTAVO PEÑA CASTRO RUT: 18.785.387-7"),
                // Unrecognized domain -> discarded instead of tracked.
                Email("msg-discarded", "otra-comuna-no-registrada.cl", "Solicitud de cambio de domicilio"),
                // Recognized domain, but no name/RUT extractable from the body -> tracked but flagged for manual review.
                Email("msg-unresolved", CatemuDomain, "Favor tramitar cambio de domicilio adjunto."),
            ]);

            var syncService = new CambioDomicilioSyncService(
                routingService, reader, repository, new NoopCsvReportWriter(), options, NullLogger<CambioDomicilioSyncService>.Instance);

            var pageModel = new IndexModel(
                repository, discardedRepository, routingService, syncService, options, NullLogger<IndexModel>.Instance)
            {
                PageContext = new PageContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new PageActionDescriptor()))
            };

            var result = await pageModel.OnPostSyncNowAsync();

            Assert.IsType<PageResult>(result);
            Assert.False(pageModel.MessageIsError);
            Assert.Contains("completada", pageModel.Message, StringComparison.OrdinalIgnoreCase);
            // W2: the routing spec's Cycle Reporting scenario — the exact counts must reach the UI
            // message, not just the grid: 1 created, 1 for review, 1 discarded.
            Assert.Contains("1 caso(s) creado(s)", pageModel.Message);
            Assert.Contains("1 para revisión", pageModel.Message);
            Assert.Contains("1 correo(s) descartado(s)", pageModel.Message);

            var all = repository.GetAll();
            Assert.Equal(2, all.Count); // created + unresolved; the discarded email never becomes a PersonRequest

            var created = Assert.Single(all, r => !r.NeedsReview);
            Assert.Equal("18.785.387-7", created.Rut);
            Assert.Equal("Catemu", created.Comuna);
            Assert.Equal(RequestStatus.Pending, created.Status);

            var unresolved = Assert.Single(all, r => r.NeedsReview);
            Assert.Null(unresolved.Rut);
            Assert.Equal("Catemu", unresolved.Comuna);

            Assert.Single(discardedRepository.GetAll());
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task OnPostSyncNowAsync_WhileACycleIsAlreadyRunning_ReportsBusyInsteadOfRunningTwice()
    {
        var csvPath = WriteComunaCsv();
        try
        {
            var repository = new FakeCambioDomicilioRequestRepository();
            var discardedRepository = new FakeDiscardedEmailRepository();
            var options = new CambioDomicilioOptions { OwnDomain = OwnDomain, ComunaDirectoryCsvPath = csvPath };

            var routingService = new AddressChangeRoutingService(
                repository, discardedRepository, new ComunaDirectory(), new NoopMailSender(), new NoopEmailMover(),
                new NoopUserRepository(), [], options, NullLogger<AddressChangeRoutingService>.Instance);

            var gate = new TaskCompletionSource();
            var syncService = new CambioDomicilioSyncService(
                routingService, new BlockingEmailReader(gate.Task), repository, new NoopCsvReportWriter(), options,
                NullLogger<CambioDomicilioSyncService>.Instance);

            var httpContext = new DefaultHttpContext();
            var firstModel = new IndexModel(repository, discardedRepository, routingService, syncService, options, NullLogger<IndexModel>.Instance)
            {
                PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
            };
            var secondModel = new IndexModel(repository, discardedRepository, routingService, syncService, options, NullLogger<IndexModel>.Instance)
            {
                PageContext = new PageContext(new ActionContext(httpContext, new RouteData(), new PageActionDescriptor()))
            };

            var firstCall = firstModel.OnPostSyncNowAsync();
            await secondModel.OnPostSyncNowAsync();

            Assert.True(secondModel.MessageIsError);
            Assert.Contains("en curso", secondModel.Message, StringComparison.OrdinalIgnoreCase);

            gate.SetResult();
            await firstCall;
            Assert.False(firstModel.MessageIsError);
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    private static IncomingEmail Email(string messageId, string senderDomain, string bodyText) => new(
        MessageId: messageId,
        ConversationId: $"conv-{messageId}",
        Subject: "Cambio de domicilio",
        SenderAddress: $"contacto@{senderDomain}",
        BodyText: bodyText,
        ReceivedAt: DateTimeOffset.UtcNow);

    private static string WriteComunaCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), $"comunas-onpostsync-{Guid.NewGuid():N}.csv");
        File.WriteAllLines(path, ["Comuna,ContactEmail,Domain", $"Catemu,rfloresc@{CatemuDomain},{CatemuDomain}"]);
        return path;
    }

    private sealed class FakeEmailReader(IReadOnlyList<IncomingEmail> sourceFolderEmails) : IEmailReader
    {
        public Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
            => Task.FromResult(folderDisplayName == "CARP. PARA PEDIR"
                ? sourceFolderEmails
                : (IReadOnlyList<IncomingEmail>)[]);
    }

}
