using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Persistence;

/// <summary>
/// Copies the SQLite file on every start and keeps the last few copies. The whole department's
/// agenda — cases, edits and users — is that one file; a copy costs a few megabytes and a moment,
/// and losing it costs a year of retyping.
/// </summary>
/// <remarks>Con <c>encryptionKey</c> (Carpetas:BackupEncryptionKey) la copia se verifica en claro y
/// luego se guarda solo cifrada (<c>.db.enc</c>, ver <see cref="BackupCipher"/>); la copia en claro se
/// borra. Sin clave, el comportamiento es el de siempre.</remarks>
public sealed class DatabaseBackup(string databasePath, string backupDirectory, int keep,
    string? secondaryBackupDirectory = null, string? encryptionKey = null)
{
    /// <summary>
    /// Returns the path of the primary copy, or null when there was nothing to copy or the copy failed —
    /// a backup problem is never a reason to stop the operator from working.
    /// </summary>
    public string? Run(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(databasePath))
            {
                return null;
            }

            Directory.CreateDirectory(backupDirectory);

            var name = $"{Path.GetFileNameWithoutExtension(databasePath)}-{now:yyyyMMdd-HHmm}.db";
            var destination = Path.Combine(backupDirectory, name);

            // Restarting the app twice within the same minute must not pile up identical copies.
            if (!string.IsNullOrEmpty(encryptionKey) && File.Exists(destination + BackupCipher.Extension))
            {
                return destination + BackupCipher.Extension;
            }

            if (!File.Exists(destination))
            {
                File.Copy(databasePath, destination);
            }

            // A plain file copy can land mid-write and look fine while being torn — a corrupt
            // backup must never green-light a caller that is about to delete the original data.
            if (!IsValidDatabase(destination))
            {
                File.Delete(destination);
                return null;
            }

            if (!string.IsNullOrEmpty(encryptionKey))
            {
                var encrypted = destination + BackupCipher.Extension;
                File.WriteAllBytes(encrypted, BackupCipher.Encrypt(File.ReadAllBytes(destination), encryptionKey));
                File.Delete(destination);
                destination = encrypted;
                name += BackupCipher.Extension;
            }

            RemoveOldCopies(backupDirectory);

            if (!string.IsNullOrWhiteSpace(secondaryBackupDirectory))
            {
                CopyToSecondary(destination, name);
            }

            return destination;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void CopyToSecondary(string verifiedSourcePath, string fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(secondaryBackupDirectory))
            {
                return;
            }

            Directory.CreateDirectory(secondaryBackupDirectory);
            var secondaryDestination = Path.Combine(secondaryBackupDirectory, fileName);
            if (!File.Exists(secondaryDestination))
            {
                File.Copy(verifiedSourcePath, secondaryDestination, overwrite: true);
            }

            RemoveOldCopies(secondaryBackupDirectory);
        }
        catch (Exception)
        {
            // Network share unreachable or permissions issue: secondary backup failure must
            // never impact the primary app flow or cause a crash.
        }
    }

    private static bool IsValidDatabase(string path)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            var result = command.ExecuteScalar() as string;
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void RemoveOldCopies(string targetDirectory)
    {
        try
        {
            if (!Directory.Exists(targetDirectory))
            {
                return;
            }

            // Cifradas y en claro cuentan juntas: el nombre empieza con el mismo sello de tiempo.
            var copies = Directory.GetFiles(targetDirectory, "*.db")
                .Concat(Directory.GetFiles(targetDirectory, "*.db" + BackupCipher.Extension))
                .OrderByDescending(path => path, StringComparer.Ordinal) // the timestamp sorts as text
                .Skip(Math.Max(keep, 1))
                .ToList();

            foreach (var old in copies)
            {
                try
                {
                    File.Delete(old);
                }
                catch (IOException)
                {
                    // Locked or already gone: the next start will try again.
                }
            }
        }
        catch (Exception)
        {
            // Directory inaccessible
        }
    }
}
