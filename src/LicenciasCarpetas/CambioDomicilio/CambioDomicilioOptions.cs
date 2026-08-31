namespace LicenciasCarpetas.CambioDomicilio;

// Todas las propiedades son opcionales (a diferencia de RouterOptions en la app hermana):
// sin sección configurada, el resto de la app arranca igual (mismo patrón que F8Options),
// y la ausencia se reporta al operador en la página, no en el arranque.
// SqliteDbPath, PollIntervalMinutes y PublicBaseUrl de RouterOptions no se portan: usa la
// carpetas.db compartida, no hay polling automático y no hay reset de contraseña en este flujo.
public sealed class CambioDomicilioOptions
{
    public const string SectionName = "CambioDomicilio";

    public EwsOptions? Ews { get; set; }
    public string? MailboxAddress { get; set; }
    public string? OwnDomain { get; set; }
    public string SourceFolderName { get; set; } = "CARP. PARA PEDIR";
    public string ConfirmationFolderName { get; set; } = "CARP. YA SUBIDAS";

    /// <summary>Legal deadline to upload the folder, in business days from the request email's received date.</summary>
    public int PlazoDiasHabiles { get; set; } = 15;

    public string? ComunaDirectoryCsvPath { get; set; }

    /// <summary>Excel opcional con las peticiones de cambio de domicilio a cargar en "Solicitar
    /// Cambios de Domicilio" (botón "Sincronizar Ahora") — mismo mecanismo que
    /// F8:MatrizExcelPath: sin configurar, el botón lo indica y no hace nada. Encabezados
    /// esperados en la primera hoja: RUT, NOMBRE (o NOMBRE COMPLETO), COMUNA.</summary>
    public string? SolicitarMatrizExcelPath { get; set; }
    public string? ReportCsvPath { get; set; }
    public string? NotificationEmailAddress { get; set; }

    /// <summary>Recipient for the "carpeta no encontrada" batch notification.</summary>
    public string CertificateRequestEmailAddress { get; set; } = "javiera.sanchez@munivalpo.cl";

    public bool ToastNotificationsEnabled { get; set; } = true;
}

public sealed class EwsOptions
{
    public string? Url { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
