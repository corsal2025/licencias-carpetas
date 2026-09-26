using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Ews;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

public class EwsHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static CambioDomicilioOptions Configured() => new()
    {
        Ews = new EwsOptions { Url = "https://mail/EWS", Username = "u", Password = "p" },
        SourceFolderName = "CARP. PARA PEDIR"
    };

    [Fact]
    public async Task Without_credentials_it_reports_not_configured_and_never_calls_exchange()
    {
        var reader = new FakeReader(() => throw new InvalidOperationException("no debería llamarse"));
        var check = new EwsHealthCheck(new CambioDomicilioOptions(), reader);

        var status = await check.CheckAsync(Now);

        Assert.Equal(EwsHealth.NotConfigured, status.Health);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task A_successful_listing_is_ok_and_remembered()
    {
        var check = new EwsHealthCheck(Configured(), new FakeReader(() => []));

        Assert.Equal(EwsHealth.NotChecked, check.Last.Health);
        await check.CheckAsync(Now);

        Assert.Equal(EwsHealth.Ok, check.Last.Health);
        Assert.Equal(Now, check.Last.CheckedAt);
    }

    [Fact]
    public async Task An_error_is_reported_with_its_message()
    {
        var check = new EwsHealthCheck(Configured(), new FakeReader(() => throw new HttpRequestException("401 Unauthorized")));

        var status = await check.CheckAsync(Now);

        Assert.Equal(EwsHealth.Failed, status.Health);
        Assert.Contains("401", status.Detail);
    }

    private sealed class FakeReader(Func<IReadOnlyList<IncomingEmail>> result) : IEmailReader
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<IncomingEmail>> GetMessagesInFolderAsync(string folderDisplayName, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result());
        }
    }
}
