using System;
using System.IO;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace LicenciasCarpetas.Tests;

public class ComunaSeedValidationTests(ITestOutputHelper output)
{
    [Fact]
    public void VerifyAllRealComunasLoaded()
    {
        var csvPath = @"C:\Users\raul.salazar\Desktop\1.-licencias-carpetas\data\comunas.csv";
        Assert.True(File.Exists(csvPath), "data/comunas.csv debe existir");

        var dir = new ComunaDirectory();
        var contacts = dir.LoadFromCsv(csvPath);
        output.WriteLine($"Total cargados por ComunaDirectory: {contacts.Count}");
        Assert.True(contacts.Count >= 250, $"Se esperaban al menos 250 comunas agrupadas, se encontraron {contacts.Count}");

        // Validar algunas comunas específicas de la hoja original
        var altoHospicio = dir.ResolveByDomain("nmolina@maho.cl", "munivalpo.cl", contacts);
        Assert.NotNull(altoHospicio);
        Assert.Equal("ALTO HOSPICIO", altoHospicio!.Comuna);

        var angol = dir.ResolveByDomain("cristian.neira@angol.cl", "munivalpo.cl", contacts);
        Assert.NotNull(angol);
        Assert.Equal("ANGOL", angol!.Comuna);

        // Probar en base de datos SQLite real
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_comunas_{Guid.NewGuid():N}.db");
        try
        {
            var repo = new ComunaContactRepository($"Data Source={tempDb}");
            repo.EnsureSchema();
            repo.EnsureSeed(csvPath);

            var dbContacts = repo.All();
            output.WriteLine($"Total en base de datos SQLite: {dbContacts.Count}");
            Assert.True(dbContacts.Count >= 300);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }

    /// <summary>En un contenedor sin volumen, la ruta configurada (data/comunas.csv) no existe.
    /// EnsureSeed debe caer al directorio oficial embebido en seed/comunas.csv y poblar la tabla
    /// igual.</summary>
    [Fact]
    public void EnsureSeed_falls_back_to_the_bundled_seed_csv_when_the_configured_path_is_missing()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "seed", "comunas.csv");
        Assert.True(File.Exists(bundled), "seed/comunas.csv debe viajar con la app (LicenciasCarpetas.csproj)");

        var tempDb = Path.Combine(Path.GetTempPath(), $"test_seedfallback_{Guid.NewGuid():N}.db");
        try
        {
            var repo = new ComunaContactRepository($"Data Source={tempDb}");
            repo.EnsureSchema();
            repo.EnsureSeed(Path.Combine(Path.GetTempPath(), $"no-existe-{Guid.NewGuid():N}", "comunas.csv"));

            Assert.True(repo.All().Count >= 300, $"esperaba >=300 comunas desde el seed embebido, hubo {repo.All().Count}");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tempDb)) File.Delete(tempDb);
        }
    }
}
