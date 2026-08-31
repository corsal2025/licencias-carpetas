using LicenciasCarpetas.CambioDomicilio;

namespace LicenciasCarpetas.Tests.CambioDomicilio;

/// <summary>
/// W4: every consumer of ComunaDirectoryCsvPath / ReportCsvPath must resolve a relative path
/// against AppContext.BaseDirectory, the same way Carpetas:SqliteDbPath is pinned in Program.cs.
/// </summary>
public class CambioDomicilioPathResolverTests
{
    [Fact]
    public void Relative_path_is_combined_with_the_base_directory()
    {
        var resolved = CambioDomicilioPathResolver.ResolveAgainstBaseDirectory(
            "data/comunas.csv", @"C:\app\bin");

        Assert.Equal(Path.Combine(@"C:\app\bin", "data/comunas.csv"), resolved);
        Assert.True(Path.IsPathRooted(resolved));
    }

    [Fact]
    public void Absolute_path_is_returned_unchanged()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "comunas.csv");

        var resolved = CambioDomicilioPathResolver.ResolveAgainstBaseDirectory(absolute, @"C:\app\bin");

        Assert.Equal(absolute, resolved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_path_is_passed_through_untouched(string? blank)
    {
        var resolved = CambioDomicilioPathResolver.ResolveAgainstBaseDirectory(blank, @"C:\app\bin");

        Assert.Equal(blank, resolved);
    }
}
