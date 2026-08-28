using Microsoft.AspNetCore.Authorization;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.F8.Domain;
using LicenciasCarpetas.F8.Matriz;
using LicenciasCarpetas.F8.Services;
using LicenciasCarpetas.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LicenciasCarpetas.Dashboard.Pages.F8;

[Authorize(Policy = "F8Access")]
public sealed class IndexModel(IUrgentRequestRepository repository, IEmailSender emailSender,
    IFolderCaseRepository cases, F8Options? options = null) : PageModel
{
    /// <summary>Solo los valores de EstadoCatalog.KnownEstados que tienen un equivalente directo y
    /// no ambiguo en FolderState — "PENDIENTE", "CARPETA SUBIDA" y "DENEGADA" no tienen una
    /// correspondencia clara (¿"CARPETA SUBIDA" es SubidaAConaset? ¿SubidaConF8? ¿SubidaConOficio?
    /// no hay forma de saberlo del texto solo) y se dejan sin propagar a propósito, en vez de
    /// adivinar y escribir un estado equivocado en Casos.</summary>
    private static readonly Dictionary<string, FolderState> EstadoToFolderState = new()
    {
        ["SUBIR CON F8"] = FolderState.SubidaConF8,
        ["CREAR CERTIFICADO"] = FolderState.CrearCertificado,
        ["PRIMERA LICENCIA"] = FolderState.PrimeraLicencia,
        ["CAMBIO DE DOMICILIO"] = FolderState.CambioDomicilio,
    };

    private const string EstadoActualSubida = "SUBIDA A CONASET";
    private const string EstadoActualCertificado = "CREAR CERTIFICADO";
    private const string CertificadoRecipient = "matias.villalobos@munivalpo.cl";

    [TempData]
    public string? SyncMessage { get; set; }

    [TempData]
    public string? CertificadoMessage { get; set; }

    public IReadOnlyList<UrgentRequest> Requests { get; private set; } = [];
    public IReadOnlyList<string> DuplicateRuts { get; private set; } = [];
    public string? Month { get; private set; }
    public string? Estado { get; private set; }
    public string? EstadoActual { get; private set; }
    public bool? Flagged { get; private set; }
    public string? Search { get; private set; }
    public string? Tab { get; private set; }
    public int FlaggedCount { get; private set; }

    // Shown during the first 10 days of the month as a nudge to run the previous month's
    // monthly print batch before it's forgotten — only fires if unprinted completed cases
    // from that month actually exist.
    public bool ShowMonthlyPrintReminder { get; private set; }
    public string PreviousMonthKey { get; private set; } = string.Empty;

    public void OnGet(string? month, string? estado, string? estadoActual, bool? flagged, string? search, string? tab = null)
    {
        Month = month;
        Estado = estado;
        EstadoActual = estadoActual;
        Flagged = flagged;
        Search = search;
        Tab = tab;

        var allMatching = repository.Query(new UrgentRequestFilter(month, estado, estadoActual, flagged), search);

        var filtered = tab switch
        {
            "Pendientes" => allMatching.Where(r => r.EstadoActual != EstadoActualSubida).ToList(),
            "Subidas" => allMatching.Where(r => r.EstadoActual == EstadoActualSubida).ToList(),
            "Certificados" => allMatching.Where(r => r.EstadoActual == EstadoActualCertificado).ToList(),
            _ => allMatching,
        };

        // Completed cases (SUBIDA A CONASET) drop to the bottom, everything still in progress
        // stays on top in the order it arrived — so newly loaded/pending cases are always the
        // first thing the operator sees instead of mixed in among finished ones. Within the
        // completed group, sort by FechaDeSubida so the monthly print workflow can walk them
        // in the order they were closed out.
        Requests = filtered
            .OrderBy(r => r.EstadoActual == EstadoActualSubida ? 1 : 0)
            .ThenBy(r => r.EstadoActual == EstadoActualSubida ? r.FechaDeSubida : null)
            .ThenBy(r => r.Id)
            .ToList();

        DuplicateRuts = repository.GetAll()
            .Where(r => !string.IsNullOrWhiteSpace(r.Rut))
            .GroupBy(r => r.Rut!)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        FlaggedCount = repository.GetFlagged().Count;

        var today = DateOnly.FromDateTime(DateTime.Today);
        PreviousMonthKey = ImpresionMensualModel.PreviousMonthKey(today);
        ShowMonthlyPrintReminder = today.Day <= 10 && repository.GetAll().Any(r =>
            r.EstadoActual == EstadoActualSubida &&
            r.ImpresoMensualAt is null &&
            r.FechaDeSubida is not null &&
            r.FechaDeSubida.Value.ToString("yyyy-MM") == PreviousMonthKey);
    }

    public IActionResult OnPostSetEstado(long id, string estado)
    {
        var request = repository.FindById(id);
        if (request is not null)
        {
            var normalized = EstadoCatalog.NormalizeForPersistence(estado, isEstado: true);
            var unknown = EstadoCatalog.IsUnknownEstado(estado);
            request.Estado = normalized;

            if (unknown)
            {
                request.NeedsReview = true;
                repository.Update(request);
                repository.AddFlag(id, "Estado", ImportFlag.ReasonCodes.UnknownEstado, estado);
            }
            else
            {
                request.NeedsReview = false;
                repository.Update(request);
                repository.ClearFlags(id);
            }

            PropagateToCasos(request, normalized);
        }
        return RedirectToPage();
    }

    /// <summary>Igual que el desplegable de Estado en "Solicitar Cambios de Domicilio" propaga a
    /// Casos cuando la solicitud tiene un caso de origen: acá no hay un id guardado que ligue esta
    /// fila de F8 con su FolderCase (esta pantalla también recibe filas del Excel de la matriz, que
    /// nunca tuvieron un caso de Casos), así que se busca por RUT — coincidencia exacta nada más,
    /// para no tocar el caso equivocado si el RUT está repetido (la propia pantalla de Casos ya
    /// sabe que eso pasa, ver DuplicateRuts).</summary>
    private void PropagateToCasos(UrgentRequest request, string? normalizedEstado)
    {
        if (normalizedEstado is null || !EstadoToFolderState.TryGetValue(normalizedEstado, out var folderState)
            || string.IsNullOrWhiteSpace(request.Rut))
        {
            return;
        }

        // UrgentRequest.Rut sale de F8.Domain.Rut.ToString() — SIN puntos ("18785387-7").
        // FolderCase.Rut se normaliza con RutValidator.NormalizeAndValidate — CON puntos
        // ("18.785.387-7"). Comparar los dos strings tal cual nunca matcheaba con datos reales
        // (siempre distinto formato); hay que pasar el de F8 por el mismo normalizador antes de
        // comparar.
        var normalizedRut = RutValidator.NormalizeAndValidate(request.Rut);
        if (normalizedRut is null)
        {
            return;
        }

        var matches = cases.QueryAll(new CaseFilter { Search = normalizedRut })
            .Where(c => c.Rut == normalizedRut);
        foreach (var folderCase in matches)
        {
            cases.UpdateEditableFields(folderCase.Id, folderCase.FullName, folderCase.Rut, folderCase.CitationDate,
                folderCase.FolderUploadedDate, folderCase.LastFolderDate, folderCase.LastFolderComuna,
                folderState, folderCase.FinalDecision, folderCase.MoralIdoneity,
                folderCase.AttentionNote, folderCase.NeedsReview, cambioDomicilioComuna: folderCase.CambioDomicilioComuna);
        }
    }

    public IActionResult OnPostSetEstadoActual(long id, string estadoActual)
    {
        var request = repository.FindById(id);
        if (request is not null)
        {
            var normalized = EstadoCatalog.NormalizeForPersistence(estadoActual, isEstado: false);
            var unknown = EstadoCatalog.IsUnknownEstadoActual(estadoActual);
            request.EstadoActual = normalized;
            if (normalized == EstadoActualSubida)
            {
                request.FechaDeSubida = DateOnly.FromDateTime(DateTime.Today);
                if (request.Marked)
                {
                    repository.SetMarked(id, false);
                    request.Marked = false;
                }
                if (request.PendienteCarpeta)
                {
                    repository.SetPendienteCarpeta(id, false);
                    request.PendienteCarpeta = false;
                }
            }
            else if (normalized != EstadoActualSubida && request.FechaDeSubida is not null)
            {
                request.FechaDeSubida = null;
            }

            if (unknown)
            {
                request.NeedsReview = true;
                repository.Update(request);
                repository.AddFlag(id, "EstadoActual", ImportFlag.ReasonCodes.UnknownEstadoActual, estadoActual);
            }
            else
            {
                request.NeedsReview = false;
                repository.Update(request);
                repository.ClearFlags(id);
            }
        }
        return RedirectToPage();
    }

    public IActionResult OnPostSetPersonData(long id, string nombreCompleto, string rut)
    {
        var request = repository.FindById(id);
        if (request is not null)
        {
            request.NombreCompleto = nombreCompleto;
            // Normalize however the operator typed it (with/without dots, with/without dash,
            // missing the leading zero) into the canonical stored form — display then adds
            // dots back via RutFormatter, so storage and formatting stay independent.
            request.Rut = Rut.TryParse(rut, out var parsed) ? parsed.ToString() : rut;
            repository.Update(request);
        }
        return RedirectToPage();
    }

    public IActionResult OnPostMarkUploaded(long id)
    {
        var request = repository.FindById(id);
        if (request is not null)
        {
            request.EstadoActual = EstadoActualSubida;
            request.FechaDeSubida = DateOnly.FromDateTime(DateTime.Today);
            repository.Update(request);

            // Marcar/Pendiente carpeta son flags de la cola de impresión para casos en curso.
            // Una vez subido el caso, ya no pertenece a esa cola.
            if (request.Marked)
            {
                repository.SetMarked(id, false);
            }
            if (request.PendienteCarpeta)
            {
                repository.SetPendienteCarpeta(id, false);
            }
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEnviarCorreoCertificado(long id)
    {
        var request = repository.FindById(id);
        if (request is not null)
        {
            var rutDisplay = RutFormatter.WithDots(request.Rut);
            var subject = $"Solicitud de certificado - {request.NombreCompleto}";
            var body = $"""
                Estimado Matías,

                Se solicita gestionar el certificado ante la Dirección para el siguiente contribuyente:

                Nombre: {request.NombreCompleto}
                RUT: {rutDisplay}

                Saludos,
                F8 Urgentes
                """;

            try
            {
                await emailSender.SendAsync(CertificadoRecipient, subject, body);
                CertificadoMessage = $"Correo enviado a {CertificadoRecipient} por {request.NombreCompleto}.";
            }
            catch (Exception ex)
            {
                CertificadoMessage = $"No se pudo enviar el correo: {ex.Message}";
            }
        }
        return RedirectToPage(new { tab = "Certificados" });
    }

    public IActionResult OnPostSincronizar()
    {
        var result = MatrizSyncService.Sync(ResolveMatrizPath(), repository);
        SyncMessage = result.NoOp
            ? "Sincronizacion: configura F8:MatrizExcelPath en appsettings para habilitarla."
            : result.Summary() + (result.Alerts.Count > 0 ? " Detalle: " + string.Join(" | ", result.Alerts) : string.Empty);
        return RedirectToPage();
    }

    private string? ResolveMatrizPath()
    {
        var matrizPath = options?.MatrizExcelPath;
        if (string.IsNullOrWhiteSpace(matrizPath))
        {
            return null;
        }
        return Path.IsPathRooted(matrizPath)
            ? matrizPath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, matrizPath));
    }

    // Width is computed once per render from the stored value's own length (not the live
    // input, which would grow with every keystroke and reflow the whole table while typing).
    // Falls back to the placeholder's length when empty, so blank cells still show the hint.
    public static int InputWidthCh(string? value, string placeholder, int min = 6, int max = 26)
    {
        // A blank cell only needs to hint at the format, not fit the whole placeholder — that
        // would make every empty column as wide as its longest instruction text. Filled cells
        // size to their real value so nothing typed in is ever clipped.
        var length = string.IsNullOrEmpty(value) ? Math.Min(placeholder.Length, min) : value.Length;
        // 1ch = width of the font's "0" glyph, but this table's text is bold + uppercase, whose
        // average letter is noticeably wider than a digit — a 1:1 char-to-ch mapping visibly
        // clips real names (confirmed: "LUIS RODRIGO VASQUEZ SALAZAR", 29 chars, still clipped
        // at 30ch). A 1.5x multiplier plus buffer covers that gap.
        return Math.Clamp((int)Math.Ceiling(length * 1.5) + 2, min, max);
    }

    public static string EstadoRowClass(string? estado) => estado switch
    {
        "SUBIR CON F8" => "estado-row-f8",
        "PRIMERA LICENCIA" => "estado-row-licencia",
        "CAMBIO DE DOMICILIO" => "estado-row-domicilio",
        "CREAR CERTIFICADO" => "estado-row-certificado",
        "CARPETA SUBIDA" => "estado-row-subida",
        "PENDIENTE" => "estado-row-pendiente",
        "DENEGADA" => "estado-row-denegada",
        _ => "",
    };

    public static string EstadoActualBadgeClass(string? estadoActual) => estadoActual switch
    {
        EstadoActualSubida => "badge-confirmed",
        "CREAR CERTIFICADO" => "badge-uploaded",
        "PENDIENTE" or null or "" => "badge-pending",
        _ => "badge-review",
    };

    public static string DeadlineChipText(DateOnly? fechaPeticion) => DeadlineChipText(DateOnly.FromDateTime(DateTime.Today), fechaPeticion);

    public static string DeadlineChipText(DateOnly today, DateOnly? fechaPeticion)
    {
        if (fechaPeticion is null)
        {
            return "—";
        }

        var deadline = DeadlineCalculator.AddBusinessDays(fechaPeticion.Value, 15);
        var remaining = DeadlineCalculator.BusinessDaysRemaining(today, deadline);
        return remaining >= 0 ? $"+{remaining}" : $"{remaining}";
    }

    public static string DeadlineChipClass(DateOnly? fechaPeticion) => DeadlineChipClass(DateOnly.FromDateTime(DateTime.Today), fechaPeticion);

    public static string DeadlineChipClass(DateOnly today, DateOnly? fechaPeticion)
    {
        if (fechaPeticion is null)
        {
            return "badge-review";
        }

        var deadline = DeadlineCalculator.AddBusinessDays(fechaPeticion.Value, 15);
        var remaining = DeadlineCalculator.BusinessDaysRemaining(today, deadline);
        return remaining >= 0 ? "badge-ok" : "badge-review";
    }

    public IActionResult OnPostSetFechaPenultimaCarpeta(long id, string? fecha)
    {
        var request = repository.FindById(id);
        if (request is null)
        {
            return RedirectToPage();
        }

        // The field displays/expects the long Spanish form ("15 de mayo de 2024"), but dd/MM/yyyy
        // and FolderDate's ISO/serial forms are still accepted for whatever an operator pastes in.
        if (SpanishDateFormatter.TryParseLongDate(fecha, out var longForm))
        {
            request.FechaPenultimaCarpeta = longForm;
            repository.Update(request);
        }
        else if (DateOnly.TryParseExact(fecha?.Trim(), "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out var typed))
        {
            request.FechaPenultimaCarpeta = typed;
            repository.Update(request);
        }
        else
        {
            var result = FolderDate.Parse(fecha);
            if (result.Outcome is FolderDateOutcome.Ok or FolderDateOutcome.SinCarpeta)
            {
                request.FechaPenultimaCarpeta = result.Value;
                repository.Update(request);
            }
        }

        return RedirectToPage();
    }

    public IActionResult OnPostSetCodigoF8(long id, string? codigoF8)
    {
        var request = repository.FindById(id);
        if (request is not null)
        {
            request.CodigoF8 = string.IsNullOrWhiteSpace(codigoF8) ? null : codigoF8.Trim();
            repository.Update(request);
        }
        return RedirectToPage();
    }

    public IActionResult OnPostToggleMarked(long id, string? markedValue)
    {
        repository.SetMarked(id, markedValue == "on");
        return RedirectToPage();
    }

    public IActionResult OnPostTogglePendienteCarpeta(long id, string? pendienteCarpetaValue)
    {
        repository.SetPendienteCarpeta(id, pendienteCarpetaValue == "on");
        return RedirectToPage();
    }

    public IActionResult OnPostDeleteCase(long id)
    {
        repository.Delete(id);
        return RedirectToPage();
    }

    public IActionResult OnPostAddManualCases(List<string> nombreCompleto, List<string> rut, List<string> estado)
    {
        var count = Math.Min(nombreCompleto.Count, Math.Min(rut.Count, estado.Count));
        for (var i = 0; i < count; i++)
        {
            var nombre = nombreCompleto[i];
            var rutValue = rut[i];
            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(rutValue))
            {
                continue;
            }

            var rawEstado = estado[i];
            var normalizedEstado = EstadoCatalog.NormalizeForPersistence(rawEstado, isEstado: true);
            var insertedId = repository.Insert(new UrgentRequest
            {
                NombreCompleto = nombre,
                Rut = rutValue,
                Estado = normalizedEstado,
                FechaPeticion = DateOnly.FromDateTime(DateTime.Today),
                Origin = "Web",
                CreatedAt = DateTimeOffset.UtcNow,
                NeedsReview = EstadoCatalog.IsUnknownEstado(rawEstado),
            });
            if (EstadoCatalog.IsUnknownEstado(rawEstado))
            {
                repository.AddFlag(insertedId, "Estado", ImportFlag.ReasonCodes.UnknownEstado, rawEstado);
            }
        }
        return RedirectToPage();
    }

}
