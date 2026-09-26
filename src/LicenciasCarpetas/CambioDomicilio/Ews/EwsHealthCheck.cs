namespace LicenciasCarpetas.CambioDomicilio.Ews;

public enum EwsHealth
{
    NotChecked,
    NotConfigured,
    Ok,
    Failed
}

public sealed record EwsStatus(EwsHealth Health, DateTimeOffset? CheckedAt, string? Detail);

/// <summary>
/// Estado del correo institucional (Exchange/EWS) para el Panel de Control. La verificación la pide
/// el operador (lista la carpeta de entrada, con tope de tiempo); el resultado queda en memoria para
/// que abrir el panel nunca espere al servidor de correo.
/// </summary>
public sealed class EwsHealthCheck(CambioDomicilioOptions options, IEmailReader reader)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private EwsStatus last = new(EwsHealth.NotChecked, null, null);

    public EwsStatus Last => IsConfigured ? last : new EwsStatus(EwsHealth.NotConfigured, null,
        "Falta CambioDomicilio:Ews (Url, Username, Password) en appsettings.Local.json.");

    private bool IsConfigured => options.Ews is { } ews
        && !string.IsNullOrWhiteSpace(ews.Url)
        && !string.IsNullOrWhiteSpace(ews.Username)
        && !string.IsNullOrWhiteSpace(ews.Password);

    public async Task<EwsStatus> CheckAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Last;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            var messages = await reader.GetMessagesInFolderAsync(options.SourceFolderName, timeout.Token);
            last = new EwsStatus(EwsHealth.Ok, now, $"'{options.SourceFolderName}': {messages.Count} correo(s).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            last = new EwsStatus(EwsHealth.Failed, now, $"Sin respuesta del servidor en {Timeout.TotalSeconds:0} s.");
        }
        catch (Exception ex)
        {
            last = new EwsStatus(EwsHealth.Failed, now, ex.GetBaseException().Message);
        }

        return last;
    }
}
