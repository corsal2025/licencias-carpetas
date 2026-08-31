using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Domain;
using LicenciasCarpetas.Persistence;
using Xunit;

namespace LicenciasCarpetas.Tests.CambioDomicilio.Directories;

public class ComunaDirectoryTests
{
    /// <summary>C2: the routing directory CSV is a one-directional, read-only projection of the
    /// ComunaContact table. EnsureSeed regenerates the CSV from that table — never the other way
    /// around, and never with fabricated data.</summary>
    [Fact]
    public void EnsureSeed_MissingCsv_ProjectsRowsFromComunaContactTableOneDirectionally()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        var contactRepo = new FakeComunaContactRepository(
            new LicenciasCarpetas.Domain.ComunaContact { Comuna = "CATEMU", Email = "rfloresc@municatemu.cl" },
            new LicenciasCarpetas.Domain.ComunaContact { Comuna = "COLINA", Email = "luis@colina.cl" });
        var directory = new ComunaDirectory(contactRepo);

        directory.EnsureSeed(path);

        try
        {
            var reloaded = directory.LoadFromCsv(path);
            Assert.Equal(2, reloaded.Count);
            Assert.Equal("rfloresc@municatemu.cl", reloaded.Single(c => c.Comuna == "CATEMU").ContactEmail);
            Assert.Equal("municatemu.cl", reloaded.Single(c => c.Comuna == "CATEMU").Domain);
            Assert.Empty(contactRepo.UpsertedContacts); // never writes back to ComunaContact
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EnsureSeed_EmptyComunaContactTable_DoesNotFabricateADirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        var directory = new ComunaDirectory(new FakeComunaContactRepository());

        directory.EnsureSeed(path);

        Assert.False(File.Exists(path)); // no fake comunas — an empty source stays empty
    }

    private sealed class FakeComunaContactRepository(params LicenciasCarpetas.Domain.ComunaContact[] seed) : IComunaContactRepository
    {
        private readonly List<LicenciasCarpetas.Domain.ComunaContact> _contacts = [.. seed];
        public List<LicenciasCarpetas.Domain.ComunaContact> UpsertedContacts { get; } = [];

        public void EnsureSchema() { }
        public void EnsureSeed(string? csvPath = null) { }
        public void Upsert(LicenciasCarpetas.Domain.ComunaContact contact) { UpsertedContacts.Add(contact); _contacts.Add(contact); }
        public void Update(long id, string comuna, string email, string? notes = null) { }
        public IReadOnlyList<LicenciasCarpetas.Domain.ComunaContact> All(string? search = null) => _contacts;
        public void Delete(long id) { }
    }

    private static readonly IReadOnlyList<ComunaRoutingEntry> Contacts =
    [
        new ComunaRoutingEntry("Catemu", "rfloresc@municatemu.cl", "municatemu.cl")
    ];

    [Fact]
    public void ResolveByDomain_RecognizedComunaDomain_ReturnsContact()
    {
        var directory = new ComunaDirectory();

        var result = directory.ResolveByDomain("otropersona@municatemu.cl", "munivalpo.cl", Contacts);

        Assert.NotNull(result);
        Assert.Equal("Catemu", result!.Comuna);
    }

    [Fact]
    public void ResolveByDomain_OwnDomain_ReturnsNull()
    {
        var directory = new ComunaDirectory();

        var result = directory.ResolveByDomain("interno@munivalpo.cl", "munivalpo.cl", Contacts);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveByDomain_UnknownDomain_ReturnsNull()
    {
        var directory = new ComunaDirectory();

        var result = directory.ResolveByDomain("alguien@otrodominio.cl", "munivalpo.cl", Contacts);

        Assert.Null(result);
    }

    [Fact]
    public void ResolveByDomain_SharedDomainExactAddressMatch_ReturnsCorrectComuna()
    {
        IReadOnlyList<ComunaRoutingEntry> sharedDomainContacts =
        [
            new ComunaRoutingEntry("Constitucion", "jcds327@gmail.com", "gmail.com"),
            new ComunaRoutingEntry("Requinoa", "licenciasf8requinoa@gmail.com", "gmail.com")
        ];
        var directory = new ComunaDirectory();

        var result = directory.ResolveByDomain("jcds327@gmail.com", "munivalpo.cl", sharedDomainContacts);

        Assert.NotNull(result);
        Assert.Equal("Constitucion", result!.Comuna);
    }

    [Fact]
    public void ResolveByDomain_SharedDomainAddressNotRegistered_ReturnsNullInsteadOfGuessing()
    {
        IReadOnlyList<ComunaRoutingEntry> sharedDomainContacts =
        [
            new ComunaRoutingEntry("Constitucion", "jcds327@gmail.com", "gmail.com"),
            new ComunaRoutingEntry("Requinoa", "licenciasf8requinoa@gmail.com", "gmail.com")
        ];
        var directory = new ComunaDirectory();

        var result = directory.ResolveByDomain("alguien-nuevo@gmail.com", "munivalpo.cl", sharedDomainContacts);

        Assert.Null(result);
    }

    [Fact]
    public void LoadFromCsv_ValidFile_ParsesRows()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "Comuna,ContactEmail,Domain\nCatemu,rfloresc@municatemu.cl,municatemu.cl\n");
        var directory = new ComunaDirectory();

        var result = directory.LoadFromCsv(path);

        Assert.Single(result);
        Assert.Equal("Catemu", result[0].Comuna);
        File.Delete(path);
    }

    [Fact]
    public void LoadFromCsv_MalformedRow_IsSkipped()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "Comuna,ContactEmail,Domain\nFilaInvalida\nCatemu,rfloresc@municatemu.cl,municatemu.cl\n");
        var directory = new ComunaDirectory();

        var result = directory.LoadFromCsv(path);

        Assert.Single(result);
        File.Delete(path);
    }

    [Fact]
    public void LoadFromCsv_DifferentComunasShareDomain_KeepsBothRows()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path,
            "Comuna,ContactEmail,Domain\nConstitucion,jcds327@gmail.com,gmail.com\nRequinoa,licenciasf8requinoa@gmail.com,gmail.com\n");
        var directory = new ComunaDirectory();

        var result = directory.LoadFromCsv(path);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Comuna == "Constitucion");
        Assert.Contains(result, c => c.Comuna == "Requinoa");
        File.Delete(path);
    }

    [Fact]
    public void LoadFromCsv_SameComunaAndDomainRepeated_LastContactWins()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path,
            "Comuna,ContactEmail,Domain\nAlto Hospicio,nmolina@maho.cl,maho.cl\nAlto Hospicio,jorrego@maho.cl,maho.cl\n");
        var directory = new ComunaDirectory();

        var result = directory.LoadFromCsv(path);

        var contact = Assert.Single(result);
        Assert.Equal("jorrego@maho.cl", contact.ContactEmail);
        File.Delete(path);
    }

    [Fact]
    public void LoadFromCsv_MissingFile_ReturnsEmpty()
    {
        var directory = new ComunaDirectory();

        var result = directory.LoadFromCsv("no-existe.csv");

        Assert.Empty(result);
    }

    [Fact]
    public void UpdateContactEmail_ValidChange_PersistsAndKeepsOtherRows()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path,
            "Comuna,ContactEmail,Domain\nCatemu,viejo@municatemu.cl,municatemu.cl\nColina,luis@colina.cl,colina.cl\n");
        var directory = new ComunaDirectory();

        var updated = directory.UpdateContactEmail(path, "Catemu", "nuevo@municatemu.cl");

        Assert.True(updated);
        var reloaded = directory.LoadFromCsv(path);
        Assert.Equal("nuevo@municatemu.cl", reloaded.Single(c => c.Comuna == "Catemu").ContactEmail);
        Assert.Equal("luis@colina.cl", reloaded.Single(c => c.Comuna == "Colina").ContactEmail);
        File.Delete(path);
    }

    [Fact]
    public void UpdateContactEmail_UnknownComuna_ReturnsFalseWithoutModifying()
    {
        var path = Path.GetTempFileName();
        var original = "Comuna,ContactEmail,Domain\nCatemu,rfloresc@municatemu.cl,municatemu.cl\n";
        File.WriteAllText(path, original);
        var directory = new ComunaDirectory();

        var updated = directory.UpdateContactEmail(path, "NoExiste", "x@y.cl");

        Assert.False(updated);
        Assert.Equal(original, File.ReadAllText(path));
        File.Delete(path);
    }

    [Theory]
    [InlineData("sin-arroba")]
    [InlineData("dos@arrobas@x.cl")]
    [InlineData("con espacios@x.cl")]
    [InlineData("con,coma@x.cl")]
    [InlineData("sinpunto@dominio")]
    public void UpdateContactEmail_InvalidEmailShape_IsRejected(string invalidEmail)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "Comuna,ContactEmail,Domain\nCatemu,rfloresc@municatemu.cl,municatemu.cl\n");
        var directory = new ComunaDirectory();

        Assert.False(directory.UpdateContactEmail(path, "Catemu", invalidEmail));
        File.Delete(path);
    }

    [Fact]
    public void AddContact_ValidNewComuna_PersistsAndKeepsExistingRows()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "Comuna,ContactEmail,Domain\nCatemu,rfloresc@municatemu.cl,municatemu.cl\n");
        var directory = new ComunaDirectory();

        var added = directory.AddContact(path, "Villa Alemana", "contacto@villaalemana.cl", "villaalemana.cl");

        Assert.True(added);
        var reloaded = directory.LoadFromCsv(path);
        Assert.Equal(2, reloaded.Count);
        var newContact = reloaded.Single(c => c.Comuna == "Villa Alemana");
        Assert.Equal("contacto@villaalemana.cl", newContact.ContactEmail);
        Assert.Equal("villaalemana.cl", newContact.Domain);
        Assert.Equal("rfloresc@municatemu.cl", reloaded.Single(c => c.Comuna == "Catemu").ContactEmail);
        File.Delete(path);
    }

    [Fact]
    public void AddContact_MissingFile_CreatesFileWithNewRow()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        var directory = new ComunaDirectory();

        var added = directory.AddContact(path, "Limache", "contacto@limache.cl", "limache.cl");

        Assert.True(added);
        var reloaded = directory.LoadFromCsv(path);
        var contact = Assert.Single(reloaded);
        Assert.Equal("Limache", contact.Comuna);
        File.Delete(path);
    }

    [Theory]
    [InlineData("", "correo@dominio.cl", "dominio.cl")]
    [InlineData("Comuna", "correo-invalido", "dominio.cl")]
    [InlineData("Comuna", "correo@dominio.cl", "")]
    [InlineData("Comuna", "correo@dominio.cl", "sin-punto")]
    [InlineData("Comuna", "correo@dominio.cl", "con espacio.cl")]
    [InlineData("Comuna", "correo@dominio.cl", "con@arroba.cl")]
    public void AddContact_InvalidInput_IsRejectedWithoutModifyingFile(string comuna, string email, string domain)
    {
        var path = Path.GetTempFileName();
        var original = "Comuna,ContactEmail,Domain\nCatemu,rfloresc@municatemu.cl,municatemu.cl\n";
        File.WriteAllText(path, original);
        var directory = new ComunaDirectory();

        var added = directory.AddContact(path, comuna, email, domain);

        Assert.False(added);
        Assert.Equal(original, File.ReadAllText(path));
        File.Delete(path);
    }
}
