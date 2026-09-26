using LicenciasCarpetas.Persistence;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Every case, every edit and every user live in a single 8 MB file. Losing it means retyping a
/// year of agenda, so a copy is taken on each start.
/// </summary>
public class DatabaseBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"carpetas-backup-{Guid.NewGuid():N}");

    public DatabaseBackupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // Backups are validated with PRAGMA integrity_check, so the source file has to be a real
    // SQLite database — a plain text stand-in would now be (correctly) rejected as corrupt.
    private string WriteDatabase(string marker = "datos-de-prueba")
    {
        var path = Path.Combine(_root, "carpetas.db");
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Marker (Value TEXT); INSERT INTO Marker VALUES ($value)";
        command.Parameters.AddWithValue("$value", marker);
        command.ExecuteNonQuery();
        return path;
    }

    private static string ReadMarker(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Marker";
        return (string)command.ExecuteScalar()!;
    }

    private string BackupDirectory => Path.Combine(_root, "backups");

    private string[] Backups() => Directory.Exists(BackupDirectory)
        ? [.. Directory.GetFiles(BackupDirectory).Order()]
        : [];

    [Fact]
    public void Copies_the_database_into_the_backup_folder()
    {
        var databasePath = WriteDatabase();
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 5);

        var created = backup.Run(new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero));

        Assert.NotNull(created);
        Assert.True(File.Exists(created));
        Assert.Equal("datos-de-prueba", ReadMarker(created));
        Assert.Contains("20260814-0930", Path.GetFileName(created));
    }

    [Fact]
    public void Keeps_only_the_newest_copies()
    {
        var databasePath = WriteDatabase();
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 3);

        for (var day = 1; day <= 5; day++)
        {
            backup.Run(new DateTimeOffset(2026, 8, day, 9, 0, 0, TimeSpan.Zero));
        }

        var remaining = Backups();
        Assert.Equal(3, remaining.Length);
        // The three most recent runs survive; the first two are gone.
        Assert.Contains("20260803", string.Join(' ', remaining));
        Assert.Contains("20260805", string.Join(' ', remaining));
        Assert.DoesNotContain("20260801", string.Join(' ', remaining));
    }

    [Fact]
    public void Two_runs_in_the_same_minute_do_not_collide()
    {
        var databasePath = WriteDatabase();
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 5);
        var moment = new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero);

        var first = backup.Run(moment);
        var second = backup.Run(moment);

        Assert.Equal(first, second);
        Assert.Single(Backups());
    }

    [Fact]
    public void Does_nothing_when_there_is_no_database_yet()
    {
        var backup = new DatabaseBackup(Path.Combine(_root, "no-existe.db"), BackupDirectory, keep: 5);

        Assert.Null(backup.Run(DateTimeOffset.UtcNow));
        Assert.Empty(Backups());
    }

    /// <summary>A backup that throws must never stop the operator from working.</summary>
    [Fact]
    public void A_failure_is_reported_as_no_backup_rather_than_thrown()
    {
        var databasePath = WriteDatabase();
        // A file where the backup folder should go: creating the directory cannot succeed.
        File.WriteAllText(BackupDirectory, "no soy una carpeta");
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 5);

        Assert.Null(backup.Run(DateTimeOffset.UtcNow));
    }

    /// <summary>A file-copy can succeed while landing mid-write and leaving a torn, unreadable
    /// database — that must be reported as no backup, never as a green light to delete the original.</summary>
    [Fact]
    public void A_torn_copy_is_reported_as_no_backup_and_is_not_left_behind()
    {
        WriteDatabase();
        var databasePath = Path.Combine(_root, "carpetas.db");
        // Simulate a copy that landed mid-write: valid SQLite header, garbage after it.
        File.WriteAllBytes(databasePath, [.. "SQLite format 3\0"u8.ToArray(), .. new byte[64]]);
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 5);

        var result = backup.Run(new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero));

        Assert.Null(result);
        Assert.Empty(Backups());
    }

    [Fact]
    public void Copies_backup_to_secondary_directory_when_configured()
    {
        var databasePath = WriteDatabase();
        var secondaryDirectory = Path.Combine(_root, "secondary-backups");
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 3, secondaryDirectory);

        var created = backup.Run(new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero));

        Assert.NotNull(created);
        Assert.True(File.Exists(created));
        Assert.True(Directory.Exists(secondaryDirectory));
        var secondaryFiles = Directory.GetFiles(secondaryDirectory);
        Assert.Single(secondaryFiles);
        Assert.Equal("datos-de-prueba", ReadMarker(secondaryFiles[0]));
    }

    [Fact]
    public void Unreachable_secondary_directory_does_not_fail_primary_backup()
    {
        var databasePath = WriteDatabase();
        // Point secondary to an invalid path that cannot be created (e.g. invalid character or file in place)
        var invalidSecondary = Path.Combine(_root, "invalid-secondary");
        File.WriteAllText(invalidSecondary, "file instead of dir");
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 3, invalidSecondary);

        var created = backup.Run(new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero));

        Assert.NotNull(created);
        Assert.True(File.Exists(created));
    }

    [Fact]
    public void With_a_key_only_an_encrypted_copy_is_kept_and_it_round_trips()
    {
        var databasePath = WriteDatabase("rut-secreto");
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 5, encryptionKey: "frase-larga-de-prueba");

        var path = backup.Run(new DateTimeOffset(2026, 9, 26, 10, 0, 0, TimeSpan.Zero))!;

        Assert.EndsWith(".db.enc", path);
        Assert.Empty(Directory.GetFiles(BackupDirectory, "*.db"));
        var bytes = File.ReadAllBytes(path);
        Assert.DoesNotContain("rut-secreto", System.Text.Encoding.UTF8.GetString(bytes));
        var restored = Path.Combine(_root, "restaurada.db");
        File.WriteAllBytes(restored, BackupCipher.Decrypt(bytes, "frase-larga-de-prueba"));
        Assert.Equal("rut-secreto", ReadMarker(restored));
    }

    [Fact]
    public void A_wrong_key_fails_instead_of_returning_garbage()
    {
        var data = BackupCipher.Encrypt([1, 2, 3], "clave-buena");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => BackupCipher.Decrypt(data, "clave-mala"));
    }

    [Fact]
    public void Encrypted_copies_are_rotated_like_plain_ones()
    {
        var databasePath = WriteDatabase();
        var backup = new DatabaseBackup(databasePath, BackupDirectory, keep: 2, encryptionKey: "k");

        for (var minute = 0; minute < 4; minute++)
        {
            backup.Run(new DateTimeOffset(2026, 9, 26, 10, minute, 0, TimeSpan.Zero));
        }

        Assert.Equal(2, Directory.GetFiles(BackupDirectory, "*.enc").Length);
    }
}
