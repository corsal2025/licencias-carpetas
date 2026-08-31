using Microsoft.Extensions.Logging;
using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Reporting;

namespace LicenciasCarpetas.CambioDomicilio.Routing;

/// <summary>What one poll cycle actually did — distinguishes "nothing ran because a previous cycle
/// was still in flight" from "the cycle ran but blew up partway through" from "there was no comuna
/// directory to route against", so the operator's "Sincronizar ahora" button can report a real
/// outcome instead of a false "completada".</summary>
public enum CambioDomicilioSyncOutcome
{
    Completed,
    SkippedBusy,
    SkippedNoDirectory,
    Failed,
}

/// <summary>Outcome of one cycle plus the per-item counts the routing spec's Cycle Reporting
/// scenario requires the UI (and the CSV report) to show: cases created, emails discarded, and
/// cases flagged for manual review.</summary>
public sealed record CambioDomicilioSyncResult(
    CambioDomicilioSyncOutcome Outcome,
    int Creados = 0,
    int Descartados = 0,
    int ParaRevision = 0);

/// <summary>
/// Runs the Cambio de Domicilio sync cycle on demand. Not a <c>BackgroundService</c>: there is no
/// automatic polling (see the proposal's "no se repone un worker de fondo"), the cycle only runs
/// when the operator presses "Sincronizar ahora" (IndexModel.OnPostSyncNowAsync calls
/// <see cref="RunCycleAsync"/> directly). Registered as a singleton so the <see cref="cycleGuard"/>
/// semaphore is shared across the transient Razor page models — that shared instance is the only
/// thing that makes the overlap guard real.
/// </summary>
public sealed class CambioDomicilioSyncService(
    AddressChangeRoutingService routingService,
    IEmailReader emailReader,
    ICambioDomicilioRequestRepository repository,
    ICsvReportWriter reportWriter,
    CambioDomicilioOptions options,
    ILogger<CambioDomicilioSyncService> logger)
{
    private readonly SemaphoreSlim cycleGuard = new(1, 1);

    /// <summary>Runs one poll cycle. Public because the dashboard's manual "sync now" action is the
    /// only caller — see <see cref="CambioDomicilioSyncResult"/> for the feedback it returns.</summary>
    public async Task<CambioDomicilioSyncResult> RunCycleAsync(CancellationToken cancellationToken)
    {
        if (!await cycleGuard.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            logger.LogWarning("Ciclo anterior aún en ejecución, se omite esta ejecución");
            return new CambioDomicilioSyncResult(CambioDomicilioSyncOutcome.SkippedBusy);
        }

        try
        {
            var contacts = routingService.LoadDirectory();
            if (contacts.Count == 0)
            {
                logger.LogCritical(
                    "El directorio de comunas ({CsvPath}) está vacío o no se pudo leer. No se procesó ningún correo para no perderlos silenciosamente",
                    options.ComunaDirectoryCsvPath);
                return new CambioDomicilioSyncResult(CambioDomicilioSyncOutcome.SkippedNoDirectory);
            }

            var idsBefore = repository.GetAll().Select(r => r.Id).ToHashSet();
            var discardedBefore = routingService.DiscardedCount();

            var incoming = await emailReader.GetMessagesInFolderAsync(options.SourceFolderName, cancellationToken);
            foreach (var email in incoming)
            {
                try
                {
                    routingService.ProcessIncomingRequest(email, contacts);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error procesando un correo de '{Folder}', se continúa con el resto del lote", options.SourceFolderName);
                }
            }

            var confirmations = await emailReader.GetMessagesInFolderAsync(options.ConfirmationFolderName, cancellationToken);
            foreach (var email in confirmations)
            {
                try
                {
                    routingService.ProcessUploadedCase(email);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error procesando un correo de '{Folder}', se continúa con el resto del lote", options.ConfirmationFolderName);
                }
            }

            var newRows = repository.GetAll().Where(r => !idsBefore.Contains(r.Id)).ToList();
            var creados = newRows.Count(r => !r.NeedsReview);
            var paraRevision = newRows.Count(r => r.NeedsReview);
            var descartados = Math.Max(0, routingService.DiscardedCount() - discardedBefore);

            if (!string.IsNullOrWhiteSpace(options.ReportCsvPath))
            {
                reportWriter.Write(repository.GetAll(), options.ReportCsvPath);
            }

            logger.LogInformation(
                "Ciclo completado: {Creados} creado(s), {ParaRevision} para revisión, {Descartados} descartado(s) ({IncomingCount} en '{SourceFolder}', {ConfirmationCount} en '{ConfirmationFolder}')",
                creados, paraRevision, descartados, incoming.Count, options.SourceFolderName, confirmations.Count, options.ConfirmationFolderName);
            return new CambioDomicilioSyncResult(CambioDomicilioSyncOutcome.Completed, creados, descartados, paraRevision);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falló el ciclo de sincronización");
            return new CambioDomicilioSyncResult(CambioDomicilioSyncOutcome.Failed);
        }
        finally
        {
            cycleGuard.Release();
        }
    }
}
