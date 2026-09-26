using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Reporting;
using LicenciasCarpetas.Tests.CambioDomicilio.Solicitar;

namespace LicenciasCarpetas.Tests;

/// <summary>Builds a Casos IndexModel wired to the same fakes every test that doesn't exercise
/// "Solicitar Cambio de Domicilio" still needs to satisfy the constructor — keeps that plumbing out
/// of tests whose actual subject is listing/paging/export/editing.</summary>
internal static class IndexModelTestFactory
{
    private sealed class NoopEmailSender : IMailSender
    {
        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public static IndexModel Create(SqliteTestDatabase db, IExcelCaseExporter exporter, CarpetasOptions options) =>
        CreateWithUrgentRequests(db, exporter, options, out _);

    /// <summary>For tests that need to assert on the F8 Urgentes rows OnPostSave auto-creates when
    /// "NO EXISTE CARPETA" is chosen — see IndexModel.OnPostSave.</summary>
    public static IndexModel CreateWithUrgentRequests(SqliteTestDatabase db, IExcelCaseExporter exporter,
        CarpetasOptions options, out FakeUrgentRequestRepositoryForCasos urgentRequests)
    {
        var outboundRequests = new FakeOutboundAddressChangeRequestRepository();
        var sender = new OutboundRequestSender(outboundRequests, db.Contacts, new NoopEmailSender());
        urgentRequests = new FakeUrgentRequestRepositoryForCasos();
        return new(db.ScopedCases(), exporter, options, outboundRequests, sender, urgentRequests, db.Contacts);
    }
}
