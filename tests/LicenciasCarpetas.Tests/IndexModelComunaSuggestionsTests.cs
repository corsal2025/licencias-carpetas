using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Reporting;

namespace LicenciasCarpetas.Tests;

public sealed class IndexModelComunaSuggestionsTests
{
    private sealed class NoopExporter : IExcelCaseExporter
    {
        public byte[] Export(IReadOnlyList<FolderCase> cases, string sheetTitle) => [];
    }

    [Fact]
    public void OnGet_exposes_official_comuna_names_uppercased_deduped_and_sorted()
    {
        using var db = new SqliteTestDatabase();
        db.Contacts.Upsert(new ComunaContact { Comuna = "quilpué", Email = "a@quilpue.cl" });
        db.Contacts.Upsert(new ComunaContact { Comuna = "Antofagasta", Email = "b@antofagasta.cl" });
        db.Contacts.Upsert(new ComunaContact { Comuna = "ANTOFAGASTA", Email = "c@antofagasta.cl" });

        var model = IndexModelTestFactory.Create(db, new NoopExporter(), new CarpetasOptions());
        model.OnGet();

        Assert.Equal(new[] { "ANTOFAGASTA", "QUILPUÉ" }, model.ComunaSuggestions);
    }

    [Fact]
    public void OnGet_returns_empty_suggestions_when_the_directory_is_empty()
    {
        using var db = new SqliteTestDatabase();

        var model = IndexModelTestFactory.Create(db, new NoopExporter(), new CarpetasOptions());
        model.OnGet();

        Assert.Empty(model.ComunaSuggestions);
    }
}
