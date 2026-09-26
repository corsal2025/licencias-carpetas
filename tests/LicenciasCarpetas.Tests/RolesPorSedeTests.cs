using System.Reflection;
using System.Security.Claims;
using LicenciasCarpetas.Dashboard.Auth;
using LicenciasCarpetas.Dashboard.Pages;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Statistics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;

namespace LicenciasCarpetas.Tests;

/// <summary>Change "sgl-roles-por-sede": each user sees and operates only their offices.</summary>
public class RolesPorSedeTests
{
    // ---------- UserOffice / PermissionAudit / SecurityStamp ----------

    [Fact]
    public void First_schema_run_gives_existing_users_every_office_and_is_idempotent()
    {
        using var db = new SqliteTestDatabase();
        var (users, offices) = Auth(db, createOfficeSchema: false);
        var id = AddUser(users, "ana", UserRole.Administrativo);

        offices.EnsureSchema();
        offices.EnsureSchema();

        Assert.Equal(Enum.GetValues<Office>(), offices.For(id));
    }

    [Fact]
    public void A_removed_office_does_not_come_back_on_restart()
    {
        using var db = new SqliteTestDatabase();
        var (users, offices) = Auth(db);
        var id = AddUser(users, "ana", UserRole.Administrativo);
        offices.Replace(id, [Office.Placilla], "admin");

        offices.EnsureSchema();

        Assert.Equal([Office.Placilla], offices.For(id));
    }

    [Fact]
    public void Replace_audits_and_rotates_the_security_stamp()
    {
        using var db = new SqliteTestDatabase();
        var (users, offices) = Auth(db);
        var id = AddUser(users, "ana", UserRole.Coordinador);
        offices.Replace(id, [Office.Placilla, Office.MercadoPuerto], "admin");
        var stampBefore = users.FindById(id)!.SecurityStamp;

        offices.Replace(id, [Office.Placilla], "admin");

        Assert.NotEqual(stampBefore, users.FindById(id)!.SecurityStamp);
        var entry = offices.Audit()[0];
        Assert.Equal(("admin", "ana", "Sedes", "Placilla, Merc. Puerto", "Placilla"),
            (entry.ChangedBy, entry.TargetUsername, entry.Field, entry.Before, entry.After));
    }

    [Fact]
    public void Saving_the_same_offices_neither_audits_nor_logs_the_user_out()
    {
        using var db = new SqliteTestDatabase();
        var (users, offices) = Auth(db);
        var id = AddUser(users, "ana", UserRole.Coordinador);
        offices.Replace(id, [Office.Placilla], "admin");
        var stamp = users.FindById(id)!.SecurityStamp;
        var audits = offices.Audit().Count;

        offices.Replace(id, [Office.Placilla], "admin");

        Assert.Equal(stamp, users.FindById(id)!.SecurityStamp);
        Assert.Equal(audits, offices.Audit().Count);
    }

    [Fact]
    public void Every_user_has_a_security_stamp_and_deleting_removes_the_offices()
    {
        using var db = new SqliteTestDatabase();
        var (users, offices) = Auth(db);
        var id = AddUser(users, "ana", UserRole.Coordinador);
        offices.Replace(id, [Office.Placilla], "admin");

        Assert.False(string.IsNullOrEmpty(users.FindById(id)!.SecurityStamp));
        users.Delete("ana");

        Assert.Empty(offices.For(id));
    }

    // ---------- Claims ----------

    [Theory]
    [InlineData(UserRole.Administrador)]
    [InlineData(UserRole.Jefatura)]
    public void Management_roles_are_unrestricted_and_carry_no_office_claims(UserRole role)
    {
        var principal = ClaimsFactory.Create(User(role), [Office.Placilla]);

        Assert.Empty(principal.FindAll(OfficeScopeClaims.ClaimType));
        Assert.True(OfficeScopeClaims.From(principal).IsUnrestricted);
    }

    [Fact]
    public void Restricted_roles_get_one_claim_per_office()
    {
        var principal = ClaimsFactory.Create(User(UserRole.Administrativo), [Office.MercadoPuerto, Office.AvenidaArgentina]);

        Assert.Equal(["AvenidaArgentina", "MercadoPuerto"], principal.FindAll("office").Select(c => c.Value));
        var scope = OfficeScopeClaims.From(principal);
        Assert.True(scope.Allows(Office.AvenidaArgentina));
        Assert.False(scope.Allows(Office.Placilla));
    }

    [Fact]
    public void A_restricted_user_without_offices_sees_nothing()
    {
        var scope = OfficeScopeClaims.From(ClaimsFactory.Create(User(UserRole.Coordinador), []));

        Assert.NotNull(scope.AllowedOffices);
        Assert.Empty(scope.AllowedOffices!);
    }

    [Fact]
    public void Narrowing_never_widens_the_scope()
    {
        var scope = new OfficeScope([Office.Placilla]);

        Assert.Empty(scope.Narrow(Office.MercadoPuerto).AllowedOffices!);
        Assert.Equal([Office.Placilla], scope.Narrow(Office.Placilla).AllowedOffices!);
        Assert.Same(scope, scope.Narrow(null));
    }

    // ---------- Session revalidation ----------

    [Fact]
    public void A_session_is_rebuilt_when_the_stamp_changes_and_dropped_when_the_user_is_deleted()
    {
        using var db = new SqliteTestDatabase();
        var (users, offices) = Auth(db);
        var id = AddUser(users, "ana", UserRole.Coordinador);
        offices.Replace(id, [Office.Placilla, Office.MercadoPuerto], "admin");
        var revalidator = new SessionRevalidator(users, offices);
        var session = ClaimsFactory.Create(users.FindById(id)!, offices.For(id));

        Assert.Equal(SessionCheck.Valid, revalidator.Check(session).Result);

        offices.Replace(id, [Office.Placilla], "admin");
        var (result, rebuilt) = revalidator.Check(session);
        Assert.Equal(SessionCheck.Replaced, result);
        Assert.Equal(["Placilla"], rebuilt!.FindAll("office").Select(c => c.Value));

        users.Delete("ana");
        Assert.Equal(SessionCheck.Rejected, revalidator.Check(rebuilt).Result);
    }

    // ---------- Scoped case repository ----------

    [Fact]
    public void Lists_counts_bin_and_sector_only_show_the_scope()
    {
        using var db = new SqliteTestDatabase();
        var placilla = AddCase(db, Office.Placilla);
        AddCase(db, Office.MercadoPuerto);
        var trashedPlacilla = AddCase(db, Office.Placilla);
        var trashedPuerto = AddCase(db, Office.MercadoPuerto);
        db.Cases.Delete(trashedPlacilla);
        db.Cases.Delete(trashedPuerto);
        var cases = db.ScopedCases(new OfficeScope([Office.Placilla]));

        Assert.Equal([placilla], cases.QueryAll(new CaseFilter()).Select(c => c.Id));
        Assert.Equal(1, cases.Count(new CaseFilter()));
        Assert.Equal(0, cases.Count(new CaseFilter { Office = Office.MercadoPuerto }));
        Assert.Equal([trashedPlacilla], cases.Deleted().Select(c => c.Id));
        Assert.Equal(1, cases.CountDeleted());
        Assert.All(cases.ForSector(FolderSector.Oficina43, onlyMarked: false, includePrinted: true), c => Assert.Equal(Office.Placilla, c.Office));
        Assert.Equal(2, db.ScopedCases().Count(new CaseFilter()));
    }

    [Fact]
    public void An_empty_scope_returns_nothing_without_errors()
    {
        using var db = new SqliteTestDatabase();
        AddCase(db, Office.Placilla);
        var cases = db.ScopedCases(new OfficeScope([]));

        Assert.Empty(cases.QueryAll(new CaseFilter()));
        Assert.Empty(cases.DailyAttendance(2026, 3));
        Assert.Equal(0, cases.CountNeedingReview());
    }

    [Fact]
    public void Cases_of_other_offices_cannot_be_read_or_changed_by_id()
    {
        using var db = new SqliteTestDatabase();
        var puerto = AddCase(db, Office.MercadoPuerto);
        var cases = db.ScopedCases(new OfficeScope([Office.Placilla]));

        Assert.Null(cases.FindById(puerto));
        Assert.Throws<CaseOutOfScopeException>(() => cases.SetMarked(puerto, true));
        Assert.Throws<CaseOutOfScopeException>(() => cases.Delete(puerto));
        Assert.Throws<CaseOutOfScopeException>(() => cases.GetAuditLog(puerto));
        Assert.Throws<CaseOutOfScopeException>(() => cases.UpdateObservations(puerto, "x"));
        Assert.Throws<CaseOutOfScopeException>(() => cases.Insert(new FolderCase { FullName = "X", Office = Office.MercadoPuerto }));
        Assert.Throws<CaseOutOfScopeException>(() => cases.DeleteAllPermanently());
        Assert.False(db.Cases.FindById(puerto)!.Marked);
        Assert.Null(db.Cases.FindById(puerto)!.DeletedAt);
    }

    [Fact]
    public void Printing_a_sector_list_only_marks_cases_in_scope()
    {
        using var db = new SqliteTestDatabase();
        var placilla = AddCase(db, Office.Placilla);
        var puerto = AddCase(db, Office.MercadoPuerto);

        db.ScopedCases(new OfficeScope([Office.Placilla])).MarkSectorPrinted([placilla, puerto]);

        Assert.NotNull(db.Cases.FindById(placilla)!.SectorPrintedAt);
        Assert.Null(db.Cases.FindById(puerto)!.SectorPrintedAt);
    }

    // ---------- Statistics and search ----------

    [Fact]
    public void Statistics_only_count_the_scope()
    {
        using var db = new SqliteTestDatabase();
        AddCase(db, Office.Placilla);
        AddCase(db, Office.MercadoPuerto);
        AddCase(db, Office.MercadoPuerto);

        var stats = new StatisticsService(db.Cases, db.Counters).ForMonth(2026, 3, new OfficeScope([Office.MercadoPuerto]));

        Assert.All(stats.Days.SelectMany(d => d.ByOffice.Keys), office => Assert.Equal(Office.MercadoPuerto, office));
        Assert.Equal(2, stats.FolderStates.Sum(s => s.Count));
    }

    [Fact]
    public void Global_search_filters_cases_by_scope_but_not_other_modules()
    {
        using var db = new SqliteTestDatabase();
        AddCase(db, Office.Placilla, "ANA PLACILLA");
        AddCase(db, Office.MercadoPuerto, "ANA PUERTO");
        var f8 = new LicenciasCarpetas.F8.Data.UrgentRequestRepository(db.ConnectionString);
        f8.EnsureSchema();
        f8.Insert(new LicenciasCarpetas.F8.Domain.UrgentRequest { NombreCompleto = "ANA F8", Rut = "1-9", Origin = "Manual" });
        var search = new GlobalSearchService(db.ConnectionString);

        var restricted = search.Search("ANA", scope: new OfficeScope([Office.Placilla]));
        var none = search.Search("ANA", scope: new OfficeScope([]));

        Assert.Equal(["ANA PLACILLA", "ANA F8"], restricted.Select(r => r.Title));
        Assert.Equal(["ANA F8"], none.Select(r => r.Title));
    }

    // ---------- Usuarios screen ----------

    [Fact]
    public void A_restricted_role_cannot_be_saved_without_offices()
    {
        using var db = new SqliteTestDatabase();
        var (model, users, _) = Usuarios(db);

        model.OnPostCreate("ana", "clave-segura", "clave-segura", UserRole.Coordinador, sedes: []);

        Assert.Null(users.FindByUsername("ana"));
        Assert.Equal(UsuariosModel.MissingOfficeMessage, model.TempData["Message"]);
    }

    [Fact]
    public void Creating_and_editing_a_user_stores_and_audits_the_offices()
    {
        using var db = new SqliteTestDatabase();
        var (model, users, offices) = Usuarios(db);

        model.OnPostCreate("ana", "clave-segura", "clave-segura", UserRole.Administrativo, sedes: [Office.Placilla]);
        var id = users.FindByUsername("ana")!.Id;
        Assert.Equal([Office.Placilla], offices.For(id));

        model.OnPostUpdateRole("ana", UserRole.Coordinador, sedes: [Office.Placilla, Office.MercadoPuerto]);

        Assert.Equal([Office.Placilla, Office.MercadoPuerto], offices.For(id));
        Assert.Equal(UserRole.Coordinador, users.FindById(id)!.Role);
        Assert.Contains(offices.Audit(), a => a.Field == "Rol y módulos" && a.ChangedBy == "admin" && a.After == "Coordinador");
        Assert.Contains(offices.Audit(), a => a.Field == "Sedes" && a.After == "Placilla, Merc. Puerto");
    }

    [Fact]
    public void Only_administrators_can_open_the_users_screen()
    {
        var authorize = typeof(UsuariosModel).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();

        Assert.Equal("Administrador", authorize!.Roles);
    }

    // ---------- Architecture guardrail ----------

    /// <summary>Screens read cases through <see cref="IScopedCaseRepository"/>. Only modules that
    /// the spec leaves out of per-office scoping (F8, Cambio de Domicilio, Certificados) and the
    /// administrator-only Usuarios screen may take the unrestricted repository.</summary>
    [Fact]
    public void No_page_model_injects_the_unrestricted_case_repository()
    {
        string[] allowed =
        [
            "UsuariosModel",
            "LicenciasCarpetas.Dashboard.Pages.F8.IndexModel",
            "LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.IndexModel",
            "LicenciasCarpetas.Dashboard.Pages.CambioDomicilio.Solicitar.IndexModel",
            "LicenciasCarpetas.Dashboard.Pages.Certificados.IndexModel",
            "LicenciasCarpetas.Dashboard.Pages.Certificados.EmitirModel"
        ];

        var offenders = typeof(Program).Assembly.GetTypes()
            .Where(type => typeof(PageModel).IsAssignableFrom(type))
            .Where(type => type.GetConstructors().Any(ctor => ctor.GetParameters().Any(p => p.ParameterType == typeof(IFolderCaseRepository))))
            .Where(type => !allowed.Contains(type.Name) && !allowed.Contains(type.FullName))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    // ---------- helpers ----------

    private static (UserRepository Users, UserOfficeRepository Offices) Auth(SqliteTestDatabase db, bool createOfficeSchema = true)
    {
        var users = new UserRepository(db.ConnectionString);
        users.EnsureSchema();
        var offices = new UserOfficeRepository(db.ConnectionString);
        if (createOfficeSchema)
        {
            offices.EnsureSchema();
        }

        return (users, offices);
    }

    private static long AddUser(UserRepository users, string name, UserRole role)
    {
        new UserProvisioning(users).Create(name, "clave-segura", "clave-segura", role);
        return users.FindByUsername(name)!.Id;
    }

    private static DashboardUser User(UserRole role) => new()
    {
        Id = 7, Username = "ana", PasswordHash = "h", PasswordSalt = "s", Role = role, SecurityStamp = "x"
    };

    private static long AddCase(SqliteTestDatabase db, Office office, string name = "PERSONA") => db.Cases.Insert(new FolderCase
    {
        FullName = name,
        Rut = "11.111.111-1",
        Office = office,
        CitationDate = new DateOnly(2026, 3, 10),
        LastFolderDate = new DateOnly(2024, 1, 1),
        FolderState = FolderState.SeEncuentraEnOficina43
    });

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }

    private static (UsuariosModel Model, UserRepository Users, UserOfficeRepository Offices) Usuarios(SqliteTestDatabase db)
    {
        var (users, offices) = Auth(db);
        var backup = new DatabaseBackup(db.Path, Path.Combine(Path.GetTempPath(), $"lc-bk-{Guid.NewGuid():N}"), keep: 1);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "Administrador")], "Test"))
        };
        var model = new UsuariosModel(users, new UserProvisioning(users), db.Cases, backup, offices)
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new InMemoryTempDataProvider())
        };
        return (model, users, offices);
    }
}
