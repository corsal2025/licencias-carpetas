using LicenciasCarpetas.Dashboard.Auth;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace LicenciasCarpetas.Tests;

/// <summary>"Zona de peligro": vaciar Casos para reiniciar el proceso desde cero. Sin la palabra
/// exacta no borra nada; con ella, respalda primero y recién ahí borra.</summary>
public class UsuariosModelTests
{
    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private static (UsuariosModel Model, string BackupDirectory) Model(SqliteTestDatabase db)
    {
        var users = new UserRepository(db.ConnectionString);
        users.EnsureSchema();
        var backupDirectory = Path.Combine(Path.GetTempPath(), $"licencias-carpetas-backups-{Guid.NewGuid():N}");
        var backup = new DatabaseBackup(db.Path, backupDirectory, keep: 5);
        var offices = new UserOfficeRepository(db.ConnectionString);
        offices.EnsureSchema();
        var model = new UsuariosModel(users, new UserProvisioning(users), db.Cases, backup, offices)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new InMemoryTempDataProvider())
        };
        return (model, backupDirectory);
    }

    private static long Seed(SqliteTestDatabase db) => db.Cases.Insert(new FolderCase
    {
        FullName = "JUAN PEREZ",
        Rut = "13.025.150-1",
        CitationDate = new DateOnly(2026, 1, 2),
        Office = Office.AvenidaArgentina,
        SourceSheet = "ENERO AV. ARGENTINA",
        SourceRow = 3
    });

    [Fact]
    public void OnPostClearCasesData_WrongWord_DeletesNothing()
    {
        using var db = new SqliteTestDatabase();
        Seed(db);
        var (model, backupDirectory) = Model(db);

        var result = model.OnPostClearCasesData("borrar");

        Assert.Equal(1, db.Cases.Count(new CaseFilter()));
        Assert.IsType<RedirectToPageResult>(result);
        Assert.False(Directory.Exists(backupDirectory));
    }

    [Fact]
    public void OnPostClearCasesData_ExactWord_BacksUpThenDeletesEverything()
    {
        using var db = new SqliteTestDatabase();
        Seed(db);
        Seed(db);
        var (model, backupDirectory) = Model(db);

        var result = model.OnPostClearCasesData(UsuariosModel.ClearCasesConfirmationWord);

        Assert.Equal(0, db.Cases.Count(new CaseFilter()));
        Assert.IsType<RedirectToPageResult>(result);
        Assert.True(Directory.Exists(backupDirectory));
        Assert.NotEmpty(Directory.GetFiles(backupDirectory, "*.db"));

        Directory.Delete(backupDirectory, recursive: true);
    }

    [Theory]
    [InlineData("eliminar")] // case mismatch on purpose
    [InlineData(" ELIMINAR")]
    [InlineData("")]
    [InlineData(null)]
    public void OnPostClearCasesData_AnythingButTheExactWord_DeletesNothing(string? confirmWord)
    {
        using var db = new SqliteTestDatabase();
        Seed(db);
        var (model, _) = Model(db);

        model.OnPostClearCasesData(confirmWord);

        Assert.Equal(1, db.Cases.Count(new CaseFilter()));
    }
}
