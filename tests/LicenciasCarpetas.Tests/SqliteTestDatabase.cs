using LicenciasCarpetas.Persistence;

namespace LicenciasCarpetas.Tests;

/// <summary>A throwaway SQLite file per test, deleted on dispose.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    public SqliteTestDatabase()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"licencias-carpetas-{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={Path}";

        Cases = new FolderCaseRepository(ConnectionString);
        Counters = new DailyCounterRepository(ConnectionString);
        Contacts = new ComunaContactRepository(ConnectionString);

        Cases.EnsureSchema();
        Counters.EnsureSchema();
        Contacts.EnsureSchema();
    }

    public string Path { get; }
    public string ConnectionString { get; }
    public FolderCaseRepository Cases { get; }
    public DailyCounterRepository Counters { get; }
    public ComunaContactRepository Contacts { get; }

    /// <summary>The cases as a screen sees them: through the per-request office scope.</summary>
    public ScopedCaseRepository ScopedCases(LicenciasCarpetas.Domain.OfficeScope? scope = null)
        => new(Cases, scope ?? LicenciasCarpetas.Domain.OfficeScope.All);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
