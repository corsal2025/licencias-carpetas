namespace LicenciasCarpetas.CambioDomicilio;

/// <summary>
/// Resolves a possibly-relative Cambio de Domicilio config path (ComunaDirectoryCsvPath,
/// ReportCsvPath, SolicitarMatrizExcelPath) against the app's base directory — the same pin
/// applied to <c>Carpetas:SqliteDbPath</c> in Program.cs. Without this, a relative path resolves
/// against the process working directory, which changes with how the app was launched (shortcut,
/// Task Scheduler, dotnet run); the routing directory would then read empty and every cycle would
/// skip silently.
/// </summary>
public static class CambioDomicilioPathResolver
{
    public static string? ResolveAgainstBaseDirectory(string? path, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(baseDirectory, path);
    }
}
