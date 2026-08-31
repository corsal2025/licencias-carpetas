namespace LicenciasCarpetas.CambioDomicilio.Notifications;

public static class EmailTemplates
{
    public static (string Subject, string Body) UploadConfirmation(string fullName, string rut) => (
        Subject: $"Carpeta subida a Conaset – {fullName}, RUT {rut}",
        Body: $"""
            Junto con saludar,

            Se informa que la carpeta del contribuyente {fullName}, RUT {rut},
            solicitada por su comuna, ya fue subida al sistema de Conaset.

            Saluda atentamente,
            Municipalidad de Valparaíso
            """);

    /// <summary>Sent instead of <see cref="UploadConfirmation"/> when the case was resolved through
    /// the F8 process (physical folder never located in storage) rather than the normal upload —
    /// same "carpeta subida" outcome for the comuna, but the wording discloses how it was resolved.</summary>
    public static (string Subject, string Body) UploadConfirmationF8(string fullName, string rut) => (
        Subject: $"Carpeta subida a Conaset – {fullName}, RUT {rut}",
        Body: $"""
            Junto con saludar,

            Se informa que la información del contribuyente {fullName}, RUT {rut},
            solicitada por su comuna, ya fue subida al sistema de Conaset.

            Cabe hacer presente que, dado que no fue posible ubicar la carpeta física en nuestro
            sistema de almacenamiento, la gestión se realizó a través del proceso F8.

            Saluda atentamente,
            Municipalidad de Valparaíso
            """);

    /// <summary>Sent when the operator undoes a Confirmed case by mistake — the original
    /// UploadConfirmation email already reached the comuna, so this explicitly retracts it rather
    /// than silently reverting the case (which would leave the comuna believing it's done).</summary>
    public static (string Subject, string Body) ConfirmationRectification(string fullName, string rut) => (
        Subject: $"Rectificación – Carpeta subida a Conaset – {fullName}, RUT {rut}",
        Body: $"""
            Junto con saludar,

            Se informa que la confirmación de carpeta subida enviada anteriormente para el
            contribuyente {fullName}, RUT {rut}, fue enviada por error y debe considerarse sin
            efecto. La carpeta aún NO ha sido subida al sistema de Conaset.

            Disculpe las molestias.

            Saluda atentamente,
            Municipalidad de Valparaíso
            """);

    public static (string Subject, string Body) ConfirmationSentNotification(string fullName, string rut, string comuna) => (
        Subject: $"[Gestión de Licencias] Confirmación enviada – {fullName}, RUT {rut} ({comuna})",
        Body: $"""
            Se envió el correo de confirmación de subida a Conaset a la comuna de {comuna}
            para el contribuyente {fullName}, RUT {rut}.
            """);

    /// <summary>Predetermined request to Secretaría Municipal to produce the certification document
    /// for one contributor whose physical folder could not be located. Carries that person's name
    /// and RUT — one email per case, sent from the Certificado screen. No comuna: every case here is
    /// a person who held their licence in Valparaíso, that's exactly what the certificate attests.</summary>
    public static (string Subject, string Body) CertificateRequest(string fullName, string rut) => (
        Subject: $"Solicitud de certificado de acreditación – {fullName}, RUT {rut}",
        Body: $"""
            Junto con saludar,

            Se solicita la elaboración del certificado de acreditación para el siguiente
            contribuyente, cuya carpeta física no fue posible localizar en el sistema de
            almacenamiento:

            Nombre:  {fullName}
            RUT:     {rut}

            El certificado validará formalmente que la persona señalada obtuvo y mantuvo vigente su
            licencia de conducir en esta comuna.

            Saluda atentamente,
            Municipalidad de Valparaíso
            """);

    /// <summary>Sent to the requesting comuna to explain that, since the physical folder couldn't
    /// be found, a certification document will be issued instead (Secretaría Municipal process).</summary>
    public static (string Subject, string Body) CertificateAcknowledgement(string fullName, string rut) => (
        Subject: $"Certificado de acreditación en trámite – {fullName}, RUT {rut}",
        Body: $"""
            Estimados/as Colegas:

            Junto con saludarles, nos comunicamos en respuesta a la solicitud ingresada en relación
            con la situación del contribuyente {fullName}, RUN {rut}.

            Al respecto, se informa que, debido a que no es posible presentar el documento físico
            por motivos de antigüedad en los registros, se procederá a emitir un certificado de
            acreditación. Este documento validará formalmente que la persona señalada sí obtuvo y
            mantuvo vigente su licencia de conducir en esta comuna.

            Para fines de su gestión, se solicita tener en consideración el siguiente procedimiento
            interno:

            El certificado será elaborado por la Secretaría Municipal.

            El documento contará con la firma oficial del Director.

            Los plazos administrativos asociados para este proceso son de aproximadamente 5 días
            hábiles.

            Una vez que el documento se encuentre visado y firmado, se procederá al envío
            formalizado a su comuna o al interesado, según corresponda.

            Atentamente,
            Municipalidad de Valparaíso
            """);
}
