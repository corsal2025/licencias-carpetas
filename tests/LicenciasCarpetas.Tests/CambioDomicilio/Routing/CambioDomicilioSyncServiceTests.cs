using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Reporting;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.Dashboard.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Routing;

public class CambioDomicilioSyncServiceTests
{
    private static CambioDomicilioSyncService BuildService(
        IEmailReader emailReader, FakeCambioDomicilioRequestRepository repository, out AddressChangeRoutingService routingService,
        string comunaDirectoryCsvPath = "")
    {
        var options = new CambioDomicilioOptions
        {
            OwnDomain = "munivalpo.cl",
            // Empty by default: LoadDirectory() then returns [] rather than throwing, and
            // RunCycleAsync short-circuits before ever reading mail — a real path is only
            // needed by tests that must reach the email-reading step.
            ComunaDirectoryCsvPath = comunaDirectoryCsvPath
        };
        routingService = new AddressChangeRoutingService(
            repository,
            new FakeDiscardedEmailRepository(),
            new ComunaDirectory(),
            new NoopMailSender(),
            new NoopEmailMover(),
            new NoopUserRepository(),
            [],
            options,
            NullLogger<AddressChangeRoutingService>.Instance);

        return new CambioDomicilioSyncService(
            routingService,
            emailReader,
            repository,
            new NoopCsvReportWriter(),
            options,
            NullLogger<CambioDomicilioSyncService>.Instance);
    }

    /// <summary>The routing spec's overlap-prevention scenario: a cycle already in flight must make
    /// a concurrent call return immediately (false) instead of running two cycles at once.</summary>
    [Fact]
    public async Task RunCycleAsync_SecondCallWhileFirstStillRunning_ReturnsFalseWithoutRunning()
    {
        var csvPath = WriteComunaCsv();
        try
        {
            var gate = new TaskCompletionSource();
            var repository = new FakeCambioDomicilioRequestRepository();
            var service = BuildService(new BlockingEmailReader(gate.Task), repository, out _, csvPath);

            var firstCycle = service.RunCycleAsync(CancellationToken.None);
            var secondCycle = await service.RunCycleAsync(CancellationToken.None); // must not block waiting for the first

            Assert.Equal(CambioDomicilioSyncOutcome.SkippedBusy, secondCycle.Outcome);

            gate.SetResult();
            var firstResult = await firstCycle;
            Assert.Equal(CambioDomicilioSyncOutcome.Completed, firstResult.Outcome);
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    private static string WriteComunaCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), $"comunas-test-{Guid.NewGuid():N}.csv");
        File.WriteAllLines(path, ["Comuna,ContactEmail,Domain", "Catemu,rfloresc@municatemu.cl,municatemu.cl"]);
        return path;
    }

    [Fact]
    public async Task RunCycleAsync_AfterPreviousCycleFinished_RunsAgain()
    {
        var repository = new FakeCambioDomicilioRequestRepository();
        var csvPath = WriteComunaCsv();
        try
        {
            var service = BuildService(new EmptyEmailReader(), repository, out _, csvPath);

            var first = await service.RunCycleAsync(CancellationToken.None);
            var second = await service.RunCycleAsync(CancellationToken.None);

            Assert.Equal(CambioDomicilioSyncOutcome.Completed, first.Outcome);
            Assert.Equal(CambioDomicilioSyncOutcome.Completed, second.Outcome); // the guard released after the first cycle, so this is a real run, not a skip
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    [Fact]
    public async Task RunCycleAsync_EmptyDirectory_SkipsWithADistinctOutcomeWithoutReadingMail()
    {
        var reader = new CountingEmailReader();
        var repository = new FakeCambioDomicilioRequestRepository();
        var service = BuildService(reader, repository, out _);

        var result = await service.RunCycleAsync(CancellationToken.None);

        // W3: an unreadable/empty directory is NOT a completed cycle — it must be its own outcome
        // so the operator sees an error instead of "Sincronización completada".
        Assert.Equal(CambioDomicilioSyncOutcome.SkippedNoDirectory, result.Outcome);
        Assert.Equal(0, reader.CallCount); // never got to reading mail: nowhere to route it to
    }

    [Fact]
    public async Task RunCycleAsync_MixedOutcomes_ReportsCreatedDiscardedAndForReviewCounts()
    {
        // Routing spec's Cycle Reporting scenario: creates cases, discards an unrecognized-domain
        // email, and flags a recognized email with no extractable person data for review.
        var csvPath = WriteComunaCsv();
        try
        {
            var repository = new FakeCambioDomicilioRequestRepository();
            var reader = new FixedFolderEmailReader(
            [
                MakeEmail("m-created", "contacto@municatemu.cl", "GUSTAVO PEÑA CASTRO RUT: 18.785.387-7"),
                MakeEmail("m-discarded", "alguien@dominio-no-registrado.cl", "Solicitud de cambio de domicilio"),
                MakeEmail("m-review", "contacto@municatemu.cl", "Favor tramitar cambio de domicilio adjunto."),
            ]);
            var service = BuildService(reader, repository, out _, csvPath);

            var result = await service.RunCycleAsync(CancellationToken.None);

            Assert.Equal(CambioDomicilioSyncOutcome.Completed, result.Outcome);
            Assert.Equal(1, result.Creados);
            Assert.Equal(1, result.ParaRevision);
            Assert.Equal(1, result.Descartados);
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    private static IncomingEmail MakeEmail(string messageId, string sender, string body) => new(
        MessageId: messageId,
        ConversationId: $"conv-{messageId}",
        Subject: "Cambio de domicilio",
        SenderAddress: sender,
        BodyText: body,
        ReceivedAt: DateTimeOffset.UtcNow);

    private sealed class FixedFolderEmailReader(IReadOnlyList<IncomingEmail> sourceFolderEmails) : IEmailReader
    {
        public Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
            => Task.FromResult(folderDisplayName == "CARP. PARA PEDIR"
                ? sourceFolderEmails
                : (IReadOnlyList<IncomingEmail>)[]);
    }

    /// <summary>The false-success bug this test guards against: a reader failure (e.g. EWS outage)
    /// must surface as Failed, not be swallowed into a misleading Completed.</summary>
    [Fact]
    public async Task RunCycleAsync_WhenEmailReaderThrows_ReturnsFailed()
    {
        var csvPath = WriteComunaCsv();
        try
        {
            var repository = new FakeCambioDomicilioRequestRepository();
            var service = BuildService(new ThrowingEmailReader(), repository, out _, csvPath);

            var outcome = await service.RunCycleAsync(CancellationToken.None);

            Assert.Equal(CambioDomicilioSyncOutcome.Failed, outcome.Outcome);
        }
        finally
        {
            File.Delete(csvPath);
        }
    }

    private sealed class EmptyEmailReader : IEmailReader
    {
        public Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<IncomingEmail>>([]);
    }

    private sealed class CountingEmailReader : IEmailReader
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyList<IncomingEmail>>([]);
        }
    }

    private sealed class ThrowingEmailReader : IEmailReader
    {
        public Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
            => throw new InvalidOperationException("EWS no disponible (simulado)");
    }
}
