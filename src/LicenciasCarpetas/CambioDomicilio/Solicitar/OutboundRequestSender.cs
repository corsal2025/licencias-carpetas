using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.CambioDomicilio.Solicitar;

public enum OutboundSendOutcome
{
    /// <summary>Email sent and the request transitioned Borrador → Enviada.</summary>
    Sent,

    /// <summary>Email sent, but the request already figured as Enviada (another tab/operator got
    /// there first) — not an error, just nothing left to mark.</summary>
    AlreadySent,

    /// <summary>No comuna contact is registered for the request's destination — nothing was sent.</summary>
    NoContact,

    /// <summary>The email send itself failed — the request stays Borrador so the operator can retry.</summary>
    SendFailed
}

public sealed record OutboundSendResult(OutboundSendOutcome Outcome, string DestinationComuna);

/// <summary>Everything shared by every "send this outbound Cambio de Domicilio request" flow:
/// comuna-contact lookup, subject/body construction, the send (por EWS, el mismo transporte del
/// buzón institucional que usa el flujo entrante) with its failure handling, and MarkSent — used by
/// both Solicitar/IndexModel.OnPostSolicitar (the outbound-requests list) and
/// IndexModel.OnPostSolicitarCambioDomicilio (the one-click Casos button).</summary>
public sealed class OutboundRequestSender(
    IOutboundAddressChangeRequestRepository repository,
    IComunaContactRepository comunaContactRepository,
    IMailSender mailSender,
    TimeSpan? interactiveSendTimeout = null,
    CambioDomicilioOptions? options = null)
{
    /// <summary>El envío es siempre interactivo — el operador espera en la pantalla. EwsClient
    /// reintenta hasta 4 veces con 100 s de timeout cada una, así que un EWS caído dejaría la
    /// pantalla colgada varios minutos antes de mostrar el error. Se corta acá y se reporta
    /// SendFailed: la solicitud queda como Borrador para reintentar cuando el correo vuelva.</summary>
    private readonly TimeSpan _sendTimeout = interactiveSendTimeout ?? TimeSpan.FromSeconds(20);

    public async Task<OutboundSendResult> SendAsync(
        OutboundAddressChangeRequest request,
        IReadOnlyList<OutboundAddressChangeAttachment> attachments,
        long userId,
        CancellationToken cancellationToken = default)
    {
        var contacts = comunaContactRepository.All()
            .Where(c => string.Equals(c.Comuna, request.DestinationComuna, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Email)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (contacts.Count == 0)
        {
            return new OutboundSendResult(OutboundSendOutcome.NoContact, request.DestinationComuna);
        }

        var subject = $"Solicitud de cambio de domicilio — {request.FullName} ({request.Rut})";
        var body = BuildBody(request);

        // Modo pruebas y mejoras: omitir el envío real por EWS/SMTP
        if (options?.DisableOutgoingEmails == true)
        {
            Console.WriteLine($"[MODO PRUEBA] Solicitud #{request.Id} a '{request.DestinationComuna}' registrada (envío de correos desactivado).");
            var sentTest = repository.MarkSent(request.Id, DateTimeOffset.UtcNow, userId);
            return new OutboundSendResult(
                sentTest ? OutboundSendOutcome.Sent : OutboundSendOutcome.AlreadySent,
                request.DestinationComuna);
        }

        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sendCts.CancelAfter(_sendTimeout);

        try
        {
            // Un correo por dirección registrada de la comuna (EWS envía a un destinatario por vez).
            foreach (var to in contacts)
            {
                await mailSender.SendAsync(to, subject, body, sendCts.Token);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or System.Net.Sockets.SocketException or OperationCanceledException)
        {
            var motivo = ex is OperationCanceledException && !cancellationToken.IsCancellationRequested
                ? $"el servidor de correo (EWS) no respondió en {_sendTimeout.TotalSeconds:0} s"
                : ex.Message;
            Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] Envío de solicitud de cambio de domicilio #{request.Id} a comuna '{request.DestinationComuna}' falló: {motivo}");
            return new OutboundSendResult(OutboundSendOutcome.SendFailed, request.DestinationComuna);
        }

        var sent = repository.MarkSent(request.Id, DateTimeOffset.UtcNow, userId);
        return new OutboundSendResult(
            sent ? OutboundSendOutcome.Sent : OutboundSendOutcome.AlreadySent,
            request.DestinationComuna);
    }

    /// <summary>Texto fijo pedido por el operador, con cita legal del artículo 14 del Decreto 170
    /// (MTT) — Nombre y RUT son los únicos datos que cambian según de quién se esté pidiendo la
    /// carpeta. Calle/Número/Depto ya no se piden en ningún formulario de esta pantalla (se sacaron
    /// de Nueva.cshtml), así que el cuerpo no los necesita.</summary>
    private static string BuildBody(OutboundAddressChangeRequest request)
    {
        var lines = new List<string>
        {
            "Estimados,",
            "",
            "Junto con saludar, y conforme a lo establecido en el artículo 14 del Decreto N.º 170 del " +
                "Ministerio de Transportes y Telecomunicaciones, \"Reglamento para el Otorgamiento de " +
                "Licencias de Conducir\", solicito a ustedes tengan a bien remitir, a través de la " +
                "Plataforma SGL, la carpeta con los antecedentes del siguiente conductor, para la " +
                "correspondiente emisión de su licencia de conducir:",
            "",
            $"Nombre: {request.FullName}",
            $"RUT: {request.Rut}",
            "",
            "Quedamos atentos a su respuesta.",
            "",
            "Saludos cordiales,",
            "Departamento de Licencias de Conducir",
            "Municipalidad de Valparaíso"
        };

        return string.Join("\n", lines);
    }
}
