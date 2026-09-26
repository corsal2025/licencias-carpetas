namespace LicenciasCarpetas.Configuration;

public sealed class CarpetasOptions
{
    public const string SectionName = "Carpetas";

    /// <summary>SQLite file that becomes the source of truth once the workbook has been imported.</summary>
    public string SqliteDbPath { get; set; } = "data/carpetas.db";

    /// <summary>Default workbook offered on the import screen — the operator can point at any other file.</summary>
    public string DefaultWorkbookPath { get; set; } = string.Empty;

    /// <summary>Folder where uploaded workbooks land. Empty means "Excels Licencias" on the operator's Desktop,
    /// created automatically on first run so a fresh install always has somewhere to drop files.</summary>
    public string UploadDirectory { get; set; } = string.Empty;

    /// <summary>Folder where generated .xlsx exports are written.</summary>
    public string ExportDirectory { get; set; } = "data/exports";

    /// <summary>Rows per page on the cases screen — the workbook holds ~21.000 rows, so paging is not optional.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>How many startup copies of the database to keep in data/backups (about 8 MB each).</summary>
    public int BackupsToKeep { get; set; } = 10;

    /// <summary>Optional secondary or offsite backup directory (e.g. UNC share \\server\backups or external drive).
    /// If configured and reachable, verified backups are copied here automatically.</summary>
    public string? SecondaryBackupDirectory { get; set; }

    /// <summary>Frase para cifrar los respaldos (datos personales). Va en appsettings.Local.json, nunca
    /// en git. Sin ella no se puede restaurar un respaldo cifrado: guardarla también fuera del equipo.</summary>
    public string? BackupEncryptionKey { get; set; }
}
