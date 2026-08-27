using System.Diagnostics;
using LicenciasCarpetas.CambioDomicilio;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Directories;
using LicenciasCarpetas.CambioDomicilio.Ews;
using LicenciasCarpetas.CambioDomicilio.Notifications;
using LicenciasCarpetas.CambioDomicilio.Reporting;
using LicenciasCarpetas.CambioDomicilio.Routing;
using LicenciasCarpetas.CambioDomicilio.Statistics;
using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Auth;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.F8.Services;
using LicenciasCarpetas.Import;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;
using LicenciasCarpetas.Statistics;
using Microsoft.AspNetCore.Authentication.Cookies;

// ContentRootPath pinned to the exe's own folder (not the process's current directory) so every
// relative path in config (SqliteDbPath, ExportDirectory, workbook path) resolves the same way no
// matter how the app is launched — double-click, shortcut, or Task Scheduler.
var baseDir = AppContext.BaseDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = baseDir
});

builder.Configuration
    .SetBasePath(baseDir)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var options = builder.Configuration.GetSection(CarpetasOptions.SectionName).Get<CarpetasOptions>()
    ?? throw new InvalidOperationException($"Missing '{CarpetasOptions.SectionName}' configuration section.");

// No configured upload folder means a fresh install: create "Excels Licencias" on the operator's own
// Desktop so there is always a discoverable place to drop workbooks, no config edit required.
if (string.IsNullOrWhiteSpace(options.UploadDirectory))
{
    options.UploadDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "Excels Licencias");
}
Directory.CreateDirectory(options.UploadDirectory);

builder.Services.AddSingleton(options);

// SQLite resolves a relative path against the process's working directory, which changes with how
// the app was launched (shortcut, Task Scheduler, dotnet run). Pin it to the app folder instead so
// the same database is always the one opened.
var databasePath = Path.IsPathRooted(options.SqliteDbPath)
    ? options.SqliteDbPath
    : Path.Combine(AppContext.BaseDirectory, options.SqliteDbPath);
var connectionString = $"Data Source={databasePath}";
// Mismo mecanismo que el respaldo automático de arranque (más abajo) — expuesto como servicio para
// que la pantalla de Usuarios pueda pedir un respaldo bajo demanda antes de vaciar Casos.
builder.Services.AddSingleton(new DatabaseBackup(
    databasePath,
    Path.Combine(Path.GetDirectoryName(databasePath)!, "backups"),
    options.BackupsToKeep,
    options.SecondaryBackupDirectory));
builder.Services.AddSingleton<IFolderCaseRepository>(_ => new FolderCaseRepository(connectionString));
builder.Services.AddSingleton<IDailyCounterRepository>(_ => new DailyCounterRepository(connectionString));
builder.Services.AddSingleton<IComunaContactRepository>(_ => new ComunaContactRepository(connectionString));
builder.Services.AddSingleton<IUserRepository>(_ => new UserRepository(connectionString));
builder.Services.AddSingleton<ILoginService, LoginService>();
builder.Services.AddSingleton<UserProvisioning>();
builder.Services.AddSingleton<IExcelWorkbookImporter, ExcelWorkbookImporter>();
builder.Services.AddSingleton<IExcelCaseExporter, ExcelCaseExporter>();
builder.Services.AddSingleton<StatisticsService>();
builder.Services.AddSingleton<IGlobalSearchService>(_ => new GlobalSearchService(connectionString));

// Módulo F8 Urgentes: vive en la misma carpetas.db (tabla propia, UrgentRequest) y detrás del
// mismo login — ya no es una app aparte. Solo trae sus propias rutas de Excel de config.
var f8Options = builder.Configuration.GetSection(F8Options.SectionName).Get<F8Options>() ?? new F8Options();
builder.Services.AddSingleton(f8Options);
builder.Services.AddSingleton<IUrgentRequestRepository>(_ => new UrgentRequestRepository(connectionString));
// Sin .ValidateOnStart(): las credenciales SMTP son opcionales — solo hacen falta si el
// operador realmente manda un correo desde F8, no en cada arranque del servidor.
builder.Services.AddOptions<SmtpOptions>().Bind(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();

// Módulo Cambio de Domicilio: mismo trato — misma carpetas.db (tablas PersonRequest,
// DeletedSourceMessage, DiscardedEmail), mismo login. Sin CambioDomicilio:Ews configurado, EwsClient
// recién falla cuando alguien de verdad usa "Sincronizar ahora", no al arrancar (ver EwsClient.cs).
var cambioDomicilioOptions = builder.Configuration.GetSection(CambioDomicilioOptions.SectionName)
    .Get<CambioDomicilioOptions>() ?? new CambioDomicilioOptions();
builder.Services.AddSingleton(cambioDomicilioOptions);
builder.Services.AddSingleton<ICambioDomicilioRequestRepository>(_ => new CambioDomicilioRequestRepository(connectionString));
builder.Services.AddSingleton<IOutboundAddressChangeRequestRepository>(_ => new OutboundAddressChangeRequestRepository(connectionString));
builder.Services.AddSingleton<LicenciasCarpetas.CambioDomicilio.Solicitar.OutboundRequestSender>();
builder.Services.AddSingleton<LicenciasCarpetas.CambioDomicilio.Data.IDiscardedEmailRepository>(
    _ => new LicenciasCarpetas.CambioDomicilio.Data.DiscardedEmailRepository(connectionString));
builder.Services.AddSingleton<IComunaDirectory>(sp => new ComunaDirectory(sp.GetRequiredService<IComunaContactRepository>()));
builder.Services.AddSingleton<IEwsClient, EwsClient>();
builder.Services.AddSingleton<EwsEmailReader>();
builder.Services.AddSingleton<LicenciasCarpetas.CambioDomicilio.Ews.IEmailReader>(sp => sp.GetRequiredService<EwsEmailReader>());
builder.Services.AddSingleton<IEmailMover>(sp => sp.GetRequiredService<EwsEmailReader>());
builder.Services.AddSingleton<LicenciasCarpetas.CambioDomicilio.Ews.IMailSender, EwsMailSender>();
builder.Services.AddSingleton<ICsvReportWriter, CsvReportWriter>();
builder.Services.AddSingleton<INotificationChannel, WindowsToastNotificationChannel>();
builder.Services.AddSingleton<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddSingleton<AddressChangeRoutingService>();
builder.Services.AddSingleton<CambioDomicilioSyncService>();
builder.Services.AddSingleton<CambioDomicilioStatisticsService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookieOptions =>
    {
        cookieOptions.LoginPath = "/Login";
        // Sin página de "acceso denegado" propia: un rol sin permiso para la pantalla que pidió
        // por URL directa vuelve a Casos, que todos los roles pueden ver.
        cookieOptions.AccessDeniedPath = "/Index";
        cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        cookieOptions.ExpireTimeSpan = TimeSpan.FromHours(8);
        cookieOptions.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(authorizationOptions =>
{
    // Mismos claims que Login.cshtml.cs calcula (incondicional salvo Administrativo, donde se
    // decide por persona) — el nav ya los usa para ocultar los links, esto es lo que de verdad
    // bloquea la pantalla si alguien entra por URL directa sin el módulo habilitado.
    authorizationOptions.AddPolicy("CambioDomicilioAccess",
        policy => policy.RequireClaim("mod:cambio-domicilio", "true"));
    authorizationOptions.AddPolicy("F8Access",
        policy => policy.RequireClaim("mod:f8-urgentes", "true"));
});
builder.Services.AddRazorPages(razorOptions => razorOptions.RootDirectory = "/Dashboard/Pages");

var app = builder.Build();

EnsureDataDirectory(databasePath);

// Copy before the schema migrations touch anything, so the copy is the last known-good state.
var backupPath = app.Services.GetRequiredService<DatabaseBackup>().Run(DateTimeOffset.Now);
if (backupPath is not null)
{
    Console.WriteLine($"Respaldo: {backupPath}");
}

EnsureSchemas(app.Services);

// Password recovery: there is no mail transport here, so recovery is an operation run on the
// machine itself (see the /ForgotPassword screen, which points the operator at this command).
if (args.Contains("--reset-password") || args.Contains("--list-users"))
{
    var users = app.Services.GetRequiredService<IUserRepository>();

    if (args.Contains("--list-users"))
    {
        var names = users.AllUsernames();
        Console.WriteLine(names.Count == 0
            ? "No hay usuarios creados. Cree uno con: --add-user <usuario>"
            : $"Usuarios: {string.Join(", ", names)}");
        return;
    }

    var targetIndex = Array.IndexOf(args, "--reset-password") + 1;
    if (targetIndex >= args.Length || args[targetIndex].StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Falta el usuario: --reset-password <usuario>");
        Environment.ExitCode = 1;
        return;
    }

    var targetUsername = args[targetIndex];
    if (users.FindByUsername(targetUsername) is not { } targetUser)
    {
        Console.Error.WriteLine($"No existe el usuario '{targetUsername}'. Vea la lista con --list-users.");
        Environment.ExitCode = 1;
        return;
    }

    var newPassword = Environment.GetEnvironmentVariable("LC_ADMIN_PASSWORD");
    if (string.IsNullOrEmpty(newPassword))
    {
        Console.Write("Contraseña nueva: ");
        newPassword = ReadPasswordMasked();
    }

    if (newPassword.Length < 8)
    {
        Console.Error.WriteLine("La contraseña debe tener al menos 8 caracteres. No se cambió nada.");
        Environment.ExitCode = 1;
        return;
    }

    var reset = PasswordHasher.Hash(newPassword);
    users.ResetPassword(targetUser.Id, reset.Hash, reset.Salt, reset.Iterations);
    Console.WriteLine($"Contraseña de '{targetUsername}' restablecida. La cuenta quedó desbloqueada.");
    return;
}

// One-time admin CLI commands, run instead of starting the host.
if (args.Contains("--add-user") || args.Contains("--remove-user"))
{
    var users = app.Services.GetRequiredService<IUserRepository>();

    if (args.Contains("--add-user"))
    {
        var username = args[Array.IndexOf(args, "--add-user") + 1];
        var password = Environment.GetEnvironmentVariable("LC_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Contraseña: ");
            password = ReadPasswordMasked();
        }

        // Same rules as the dashboard — the console used to accept passwords the screens refused.
        var result = app.Services.GetRequiredService<UserProvisioning>().Create(username, password, password);
        if (result != ProvisioningResult.Created)
        {
            Console.Error.WriteLine(UserProvisioning.Describe(result));
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Usuario '{username.Trim().ToLowerInvariant()}' creado.");
    }
    else
    {
        var username = args[Array.IndexOf(args, "--remove-user") + 1];
        users.Delete(username);
        Console.WriteLine($"Usuario '{username}' eliminado.");
    }

    return;
}

// Bulk import from the command line — the same code path the /Importar screen uses.
if (args.Contains("--import"))
{
    var pathIndex = Array.IndexOf(args, "--import") + 1;
    var workbookPath = pathIndex < args.Length && !args[pathIndex].StartsWith("--", StringComparison.Ordinal)
        ? args[pathIndex]
        : options.DefaultWorkbookPath;

    if (string.IsNullOrWhiteSpace(workbookPath))
    {
        Console.Error.WriteLine("Falta la ruta del Excel: --import \"<ruta>\" (o configure Carpetas:DefaultWorkbookPath).");
        Environment.ExitCode = 1;
        return;
    }

    var importer = app.Services.GetRequiredService<IExcelWorkbookImporter>();
    var summary = importer.Import(workbookPath);

    Console.WriteLine($"Hojas leídas:        {summary.SheetsRead}");
    Console.WriteLine($"Filas leídas:        {summary.RowsRead}");
    Console.WriteLine($"Casos nuevos:        {summary.CasesInserted}");
    Console.WriteLine($"Casos actualizados:  {summary.CasesUpdated}");
    Console.WriteLine($"Requieren revisión:  {summary.CasesNeedingReview}");
    Console.WriteLine($"Días con contadores: {summary.CountersImported}");
    Console.WriteLine($"Correos de comunas:  {summary.ContactsImported}");
    foreach (var warning in summary.Warnings)
    {
        Console.WriteLine($"AVISO: {warning}");
    }

    return;
}

// Solo abre el navegador automáticamente si se especifica --open-browser en la línea de comandos
if (args.Contains("--open-browser"))
{
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(1500);
            var url = "https://localhost:5011";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"No se pudo abrir el navegador automáticamente: {ex.Message}");
        }
    });
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.MapGet("/api/global-search", (string? q, IGlobalSearchService searchService) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.Ok(Array.Empty<GlobalSearchResult>());
    }
    var results = searchService.Search(q, limit: 15);
    return Results.Ok(results);
}).RequireAuthorization();

app.Run();

static void EnsureDataDirectory(string sqliteDbPath)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(sqliteDbPath));
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }
}

static void EnsureSchemas(IServiceProvider services)
{
    services.GetRequiredService<IFolderCaseRepository>().EnsureSchema();
    services.GetRequiredService<IDailyCounterRepository>().EnsureSchema();
    var comunaContacts = services.GetRequiredService<IComunaContactRepository>();
    comunaContacts.EnsureSchema();
    comunaContacts.EnsureSeed();
    services.GetRequiredService<IUserRepository>().EnsureSchema();
    services.GetRequiredService<IUrgentRequestRepository>().EnsureSchema();
    services.GetRequiredService<ICambioDomicilioRequestRepository>().EnsureSchema();
    services.GetRequiredService<IOutboundAddressChangeRequestRepository>().EnsureSchema();
    services.GetRequiredService<LicenciasCarpetas.CambioDomicilio.Data.IDiscardedEmailRepository>().EnsureSchema();
    var cdOptions = services.GetRequiredService<CambioDomicilioOptions>();
    if (!string.IsNullOrWhiteSpace(cdOptions.ComunaDirectoryCsvPath))
    {
        services.GetRequiredService<IComunaDirectory>().EnsureSeed(cdOptions.ComunaDirectoryCsvPath);
    }
}

static string ReadPasswordMasked()
{
    if (Console.IsInputRedirected)
    {
        return Console.ReadLine() ?? string.Empty;
    }

    var password = new System.Text.StringBuilder();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password.Remove(password.Length - 1, 1);
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
        }
    }
    Console.WriteLine();
    return password.ToString();
}

/// <summary>Exposed so the test project can reference the web application assembly.</summary>
public partial class Program;
