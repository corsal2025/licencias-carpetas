using System.Security.Claims;
using LicenciasCarpetas.CambioDomicilio.Data;
using LicenciasCarpetas.CambioDomicilio.Solicitar;
using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Domain;
using LicenciasCarpetas.F8.Data;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OutboundAddressChangeRequest = LicenciasCarpetas.CambioDomicilio.Domain.OutboundAddressChangeRequest;
using OutboundRequestStatus = LicenciasCarpetas.CambioDomicilio.Domain.OutboundRequestStatus;
using UrgentRequest = LicenciasCarpetas.F8.Domain.UrgentRequest;

namespace LicenciasCarpetas.Dashboard.Pages;

[Authorize]
public class IndexModel(IFolderCaseRepository cases, IExcelCaseExporter exporter, CarpetasOptions options,
    IOutboundAddressChangeRequestRepository outboundRequests, OutboundRequestSender sender,
    IUrgentRequestRepository urgentRequests) : PageModel
{
    // Autoguardado dispara OnPostSave en cada cambio de celda — dos filas distintas con el mismo
    // RUT (el propio DuplicateRuts de esta pantalla dice que eso pasa) puestas en "NO EXISTE
    // CARPETA" casi al mismo tiempo podían pasar las dos el FindByRut antes de que la primera
    // terminara de insertar, y crear dos filas en F8 para la misma persona. Un solo semáforo
    // estático alcanza: la app corre en un único proceso Kestrel, y el bloque es rápido (una
    // consulta + un insert en SQLite).
    private static readonly SemaphoreSlim UrgentRequestGuard = new(1, 1);

    public IReadOnlyList<FolderCase> Cases { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int NeedsReviewCount { get; private set; }
    public int PageNumber { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;
    public IReadOnlyList<int> Years { get; private set; } = [];

    /// <summary>RUTs repeated inside the current filter, highlighted like the workbook's COUNTIF.</summary>
    public HashSet<string> DuplicateRuts { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Office? Office { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public FolderState? Estado { get; set; }

    [BindProperty(SupportsGet = true)]
    public FinalDecision? Decision { get; set; }

    [BindProperty(SupportsGet = true)]
    public FolderSector? Sector { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool NeedsReview { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OtherComuna { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Overdue { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int RequestedPage { get; set; } = 1;

    /// <summary>Rows per page chosen by the operator. Anything outside <see cref="PageSizes"/>
    /// falls back to the configured default — a hand-edited URL cannot ask for 20.000 rows at once.</summary>
    [BindProperty(SupportsGet = true, Name = "size")]
    public int PageSize { get; set; }

    public static IReadOnlyList<int> PageSizes { get; } = [50, 100, 200];

    [BindProperty(SupportsGet = true)]
    public CaseSort Sort { get; set; } = CaseSort.CitationDate;

    [BindProperty(SupportsGet = true, Name = "desc")]
    public bool Descending { get; set; }

    public string? Message { get; set; }
    public bool MessageIsError { get; set; }

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnGetExport()
    {
        var filter = BuildFilter();
        // Everything the filter matches, not just the page on screen and with no row cap: a year of
        // one office is over 15.000 rows, and a silently truncated report is a wrong report.
        var exportable = cases.QueryAll(filter);
        var bytes = exporter.Export(exportable, "Casos");
        var fileName = $"casos-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public IActionResult OnGetAuditLog(long id)
    {
        var logs = cases.GetAuditLog(id);
        return new JsonResult(logs.Select(l => new
        {
            id = l.Id,
            caseId = l.FolderCaseId,
            changedBy = l.ChangedBy,
            changedAt = l.ChangedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
            fieldName = l.FieldName,
            oldValue = l.OldValue,
            newValue = l.NewValue
        }));
    }

    public IActionResult OnPostSave(long id, string? nombre, string? rut, string? citacion, string? subida,
        string? ultimaCarpeta, FolderState? estado, FinalDecision? decision, MoralIdoneity? idoneidad, string? atencion,
        string? penultima = null, string? codigoF8 = null, LicenceClass[]? licencias = null, string? observaciones = null,
        string? folioLicencia = null, string? comuna = null, string? licenciasSerializadas = null)
    {
        // Autoguardado: cada cambio en una celda (elegir un estado, tipear un nombre, tildar una
        // licencia) manda este mismo form entero por fetch() en vez de esperar a que se apriete
        // "Guardar" — ver autoSaveCaseForm en Index.cshtml. Se decide UNA sola vez, al principio, y
        // toda salida del handler (incluida "el caso ya no existe") pasa por SaveResult — antes esta
        // rama en particular quedaba antes del chequeo del header y siempre redirigía, así que un
        // fetch() contra un caso borrado por otro operador recibía HTML de vuelta en vez de JSON,
        // el response.json() del cliente reventaba, y la fila quedaba marcada con un mensaje
        // genérico de "sin conexión" que no era la causa real.
        var isAjax = string.Equals(HttpContext?.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal);
        IActionResult SaveResult(string message, bool isError) =>
            isAjax ? new JsonResult(new { ok = true, isError, message }) : RedirectWithMessage(message, isError);

        var existing = cases.FindById(id);
        if (existing is null)
        {
            return SaveResult("El caso ya no existe.", isError: true);
        }

        var citationDate = ParseDate(citacion);
        var uploadedDate = ParseDate(subida);

        // Marcar un estado de subida es el acto de subirla: la fecha es hoy, y escribirla a mano es
        // una oportunidad de equivocarse o de olvidarla. Una fecha ya escrita no se toca.
        if (estado is { } newState && IsUploadState(newState) && uploadedDate is null)
        {
            uploadedDate = DateOnly.FromDateTime(DateTime.Today);
        }

        // Same rule as the workbook: a date means the folder is here, free text means it is in
        // another comuna and has to be requested from it.
        var lastFolderDate = ParseDate(ultimaCarpeta);
        var lastFolderComuna = lastFolderDate is null && !string.IsNullOrWhiteSpace(ultimaCarpeta)
            ? ultimaCarpeta.Trim()
            : null;

        var normalizedRut = RutValidator.NormalizeAndValidate(rut);
        var fullName = TextNormalizer.DisplayUpper(nombre);
        var needsReview = fullName is null || normalizedRut is null || citationDate is null;

        cases.UpdateEditableFields(id, fullName, normalizedRut ?? rut?.Trim(), citationDate, uploadedDate,
            lastFolderDate, lastFolderComuna, estado, decision, idoneidad,
            string.IsNullOrWhiteSpace(atencion) ? null : atencion.Trim(), needsReview,
            editedBy: User?.Identity?.Name,
            cambioDomicilioComuna: string.IsNullOrWhiteSpace(comuna) ? null : comuna.Trim().ToUpperInvariant());

        var serializedLicencias = licenciasSerializadas is not null
            ? (string.IsNullOrWhiteSpace(licenciasSerializadas) ? null : licenciasSerializadas.Trim())
            : LicenceClassCatalog.Serialize(licencias ?? []);

        // Los campos que el Excel no trae van por separado, para que una reimportación no los pise.
        cases.UpdateCaseDetails(id, codigoF8, ParseDate(penultima),
            serializedLicencias, folioLicencia, editedBy: User?.Identity?.Name);
        cases.UpdateObservations(id, observaciones, editedBy: User?.Identity?.Name);

        // Elegir "NO EXISTE CARPETA" en Casos crea automáticamente la fila en F8 Urgentes — mismo
        // dato que hoy carga el Excel de la matriz (MatrizSyncService), pero disparado por el
        // desplegable en vez de por Sincronizar. El caso sigue apareciendo en Casos también: esto
        // no es un traspaso (a diferencia de CambioDomicilio.OnPostTransferToF8), solo una copia.
        // Mientras el caso siga en NO EXISTE CARPETA, Código F8 y Fecha penúltima carpeta se
        // siguen copiando a la fila de F8 en cada autoguardado posterior (no solo al crearla) —
        // si el operador los completa después de elegir el estado, F8 tiene que verlos también.
        // El chequeo+insert/update va bajo el semáforo: sin él, dos filas con el mismo RUT (ver
        // DuplicateRuts más arriba) guardadas casi al mismo tiempo podían pasar las dos el
        // FindByRut antes de que la primera terminara de insertar.
        if (estado == FolderState.NoExisteCarpeta && normalizedRut is not null)
        {
            // UrgentRequest.Rut se guarda SIN puntos (F8.Domain.Rut.ToString()) en todos los demás
            // caminos que lo crean (edición manual, importación de la matriz) — guardarlo acá con
            // puntos (el formato de Casos) haría que esta fila nunca se encuentre por RUT desde F8
            // ni se detecte como duplicada de una ya existente por matriz.
            var f8Rut = LicenciasCarpetas.F8.Domain.Rut.TryParse(normalizedRut, out var parsedF8Rut)
                ? parsedF8Rut.ToString()
                : normalizedRut;
            var penultimaDate = ParseDate(penultima);

            UrgentRequestGuard.Wait();
            try
            {
                var existingUrgentRequest = urgentRequests.FindByRut(f8Rut);
                if (existingUrgentRequest is null)
                {
                    urgentRequests.Insert(new UrgentRequest
                    {
                        NombreCompleto = fullName,
                        Rut = f8Rut,
                        RutRaw = rut?.Trim(),
                        FechaPeticion = citationDate,
                        FechaUltimaCarpeta = lastFolderDate,
                        CodigoF8 = codigoF8,
                        FechaPenultimaCarpeta = penultimaDate,
                        Estado = FolderStateCatalog.Display(estado.Value),
                        Origin = "Casos",
                        // Mismo criterio que needsReview de más abajo (nombre o RUT o fecha de
                        // citación faltantes) — sin esto una fila incompleta se creaba en F8 sin
                        // el resaltado row-needs-review que el operador usa para detectarlas.
                        NeedsReview = needsReview,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
                else if (existingUrgentRequest.CodigoF8 != codigoF8 || existingUrgentRequest.FechaPenultimaCarpeta != penultimaDate)
                {
                    existingUrgentRequest.CodigoF8 = codigoF8;
                    existingUrgentRequest.FechaPenultimaCarpeta = penultimaDate;
                    urgentRequests.Update(existingUrgentRequest);
                }
            }
            finally
            {
                UrgentRequestGuard.Release();
            }
        }

        // Elegir "CAMBIO DE DOMICILIO SOLICITADO" en Casos crea/sincroniza automáticamente la fila
        // en el módulo "Solicitar Cambios de Domicilio" (OutboundAddressChangeRequest), de modo que
        // los datos de la persona (Nombre, RUT, Comuna) aparezcan inmediatamente en esa lista de trabajo.
        if (estado == FolderState.CambioDomicilioSolicitado && normalizedRut is not null)
        {
            var existingOutboundList = outboundRequests.FindBySourceFolderCaseId(id);
            var existingOutbound = existingOutboundList.FirstOrDefault();
            var destComuna = string.IsNullOrWhiteSpace(comuna) ? (existing.CambioDomicilioComuna ?? existing.LastFolderComuna ?? string.Empty) : comuna.Trim();
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) is { } uid && long.TryParse(uid, out var u) ? u : 1L;

            if (existingOutbound is null)
            {
                outboundRequests.Insert(new OutboundAddressChangeRequest
                {
                    FullName = fullName ?? existing.FullName ?? string.Empty,
                    Rut = normalizedRut,
                    DestinationComuna = destComuna,
                    CreatedByUserId = userId,
                    SourceFolderCaseId = id,
                    Status = OutboundRequestStatus.Borrador,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                var changed = false;
                if (!string.IsNullOrWhiteSpace(destComuna) && existingOutbound.DestinationComuna != destComuna)
                {
                    existingOutbound.DestinationComuna = destComuna;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(fullName) && existingOutbound.FullName != fullName)
                {
                    existingOutbound.FullName = fullName;
                    changed = true;
                }
                if (existingOutbound.Rut != normalizedRut)
                {
                    existingOutbound.Rut = normalizedRut;
                    changed = true;
                }
                if (changed)
                {
                    outboundRequests.Update(existingOutbound);
                }
            }
        }

        var invalidRut = normalizedRut is null && !string.IsNullOrWhiteSpace(rut);
        var message = invalidRut
            ? $"Guardado, pero el RUT '{rut}' no tiene dígito verificador válido — el caso queda en revisión."
            : "Caso guardado.";

        return SaveResult(message, isError: invalidRut);
    }

    public IActionResult OnPostBulkDelete(long[] selectedIds) => OnPostDeleteSeleccionados(selectedIds);

    /// <summary>Adds a citation row by hand, the way a new line is typed into the agenda sheet.</summary>
    public IActionResult OnPostAdd(string? nombre, string? rut, string? citacion, Office office,
        string? ultimaCarpeta, FolderState? estado, FinalDecision? decision, MoralIdoneity? idoneidad, string? atencion,
        string? penultima = null, string? codigoF8 = null)
    {
        var fullName = TextNormalizer.DisplayUpper(nombre);
        if (fullName is null)
        {
            return RedirectWithMessage("Falta el nombre completo — no se agregó el caso.", isError: true);
        }

        var citationDate = ParseDate(citacion);
        var lastFolderDate = ParseDate(ultimaCarpeta);
        var normalizedRut = RutValidator.NormalizeAndValidate(rut);
        var attention = string.IsNullOrWhiteSpace(atencion) ? null : atencion.Trim();

        var folderCase = new FolderCase
        {
            FullName = fullName,
            Rut = normalizedRut ?? (string.IsNullOrWhiteSpace(rut) ? null : rut.Trim()),
            CitationDate = citationDate,
            Office = office,
            LastFolderDate = lastFolderDate,
            LastFolderComuna = lastFolderDate is null && !string.IsNullOrWhiteSpace(ultimaCarpeta)
                ? ultimaCarpeta.Trim()
                : null,
            FolderState = estado,
            FinalDecision = decision,
            MoralIdoneity = idoneidad,
            AttentionNote = attention,
            Attended = attention is not null,
            PenultimateFolderDate = ParseDate(penultima),
            CodigoF8 = string.IsNullOrWhiteSpace(codigoF8) ? null : codigoF8.Trim(),
            NeedsReview = normalizedRut is null || citationDate is null,
            // Un caso creado ya subido lleva la fecha del día, igual que al editarlo.
            FolderUploadedDate = estado is { } state && IsUploadState(state)
                ? DateOnly.FromDateTime(DateTime.Today)
                : null
        };

        cases.Insert(folderCase);

        var warning = normalizedRut is null && !string.IsNullOrWhiteSpace(rut)
            ? $" El RUT '{rut}' no tiene dígito verificador válido, el caso queda en revisión."
            : string.Empty;
        return RedirectWithMessage($"Caso agregado.{warning}", isError: warning.Length > 0);
    }

    public IActionResult OnPostToggleMarked(long id, bool markedValue)
    {
        cases.SetMarked(id, markedValue);
        return new EmptyResult();
    }

    public IActionResult OnPostDelete(long id)
    {
        cases.Delete(id);
        return RedirectWithMessage("Caso movido a la Papelera. Se puede restaurar desde ahí.");
    }

    public IActionResult OnPostDeleteSeleccionados(long[] selectedIds)
    {
        foreach (var id in selectedIds)
        {
            cases.Delete(id);
        }

        return RedirectWithMessage(selectedIds.Length == 1
            ? "1 caso movido a la Papelera. Se puede restaurar desde ahí."
            : $"{selectedIds.Length} casos movidos a la Papelera. Se pueden restaurar desde ahí.");
    }

    /// <summary>One-click "Solicitar": creates an outbound Cambio de Domicilio request from the
    /// case's own data (name, RUT, comuna already typed in this row) and sends it right away — same
    /// contact lookup, subject/body shape and send/MarkSent sequence as
    /// Solicitar/IndexModel.OnPostSolicitar, minus Street/Number/Unit, which Casos never has.</summary>
    public async Task<IActionResult> OnPostSolicitarCambioDomicilio(long id, string? comuna = null, string? estado = null)
    {
        // Manda un correo real a otra comuna — el mismo gate que el resto de Cambio de Domicilio,
        // aunque Casos en sí quede abierto a todos los roles. El botón ya se oculta sin este claim
        // (Index.cshtml), pero eso por sí solo no basta: cualquiera podría postear el handler.
        if (!User.HasClaim("mod:cambio-domicilio", "true"))
        {
            return RedirectWithMessage("No tiene acceso al módulo Cambio de Domicilio.", isError: true);
        }

        var folderCase = cases.FindById(id);
        if (folderCase is null)
        {
            return RedirectWithMessage("El caso ya no existe.", isError: true);
        }

        // Estado carpeta y Comuna viajan con el form de "Guardar" de la fila, no con el de
        // "Solicitar" — si el operador recién los eligió/tipeó y no guardó antes, folderCase todavía
        // tiene los valores viejos de la base. Se acepta lo que hay en pantalla en ese momento
        // (posteado por este mismo form vía JS, ver prepararSolicitar en Index.cshtml) y se
        // persiste al mismo tiempo, para que un solo clic en "Solicitar" alcance sin pasar antes
        // por "Guardar".
        var effectiveState = Enum.TryParse<FolderState>(estado, out var parsedState) ? parsedState : folderCase.FolderState;
        if (effectiveState != FolderState.CambioDomicilioSolicitado)
        {
            return RedirectWithMessage("El caso no está marcado como 'Cambio de domicilio solicitado'.", isError: true);
        }

        var effectiveComuna = string.IsNullOrWhiteSpace(comuna) ? folderCase.CambioDomicilioComuna : comuna.Trim();
        if (string.IsNullOrWhiteSpace(effectiveComuna))
        {
            return RedirectWithMessage("Ingrese la comuna antes de solicitar.", isError: true);
        }
        if (effectiveComuna != folderCase.CambioDomicilioComuna || effectiveState != folderCase.FolderState)
        {
            cases.UpdateEditableFields(folderCase.Id, folderCase.FullName, folderCase.Rut, folderCase.CitationDate,
                folderCase.FolderUploadedDate, folderCase.LastFolderDate, folderCase.LastFolderComuna,
                effectiveState, folderCase.FinalDecision, folderCase.MoralIdoneity,
                folderCase.AttentionNote, folderCase.NeedsReview, cambioDomicilioComuna: effectiveComuna);
            folderCase.CambioDomicilioComuna = effectiveComuna;
            folderCase.FolderState = effectiveState;
        }

        if (string.IsNullOrWhiteSpace(folderCase.FullName) || string.IsNullOrWhiteSpace(folderCase.Rut))
        {
            return RedirectWithMessage("Complete nombre y RUT del caso antes de solicitar.", isError: true);
        }

        var existingOutboundList = outboundRequests.FindBySourceFolderCaseId(folderCase.Id).ToList();
        var alreadyRequested = existingOutboundList.Any(r => r.Status != OutboundRequestStatus.Borrador);
        if (alreadyRequested)
        {
            return RedirectWithMessage("Ya se solicitó un cambio de domicilio para este caso.", isError: true);
        }

        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var request = existingOutboundList.FirstOrDefault(r => r.Status == OutboundRequestStatus.Borrador);
        if (request is null)
        {
            var requestId = outboundRequests.Insert(new OutboundAddressChangeRequest
            {
                FullName = folderCase.FullName,
                Rut = folderCase.Rut,
                DestinationComuna = folderCase.CambioDomicilioComuna,
                CreatedByUserId = userId,
                SourceFolderCaseId = folderCase.Id
            });
            request = outboundRequests.FindById(requestId)!;
        }
        else
        {
            request.FullName = folderCase.FullName;
            request.Rut = folderCase.Rut;
            request.DestinationComuna = folderCase.CambioDomicilioComuna;
            request.CreatedByUserId = userId;
            outboundRequests.Update(request);
        }

        var result = await sender.SendAsync(request, attachments: [], userId);

        return result.Outcome switch
        {
            OutboundSendOutcome.NoContact => RedirectWithMessage(
                $"No hay correo de contacto registrado para '{result.DestinationComuna}'. Agréguelo en Comunas antes de solicitar.",
                isError: true),
            OutboundSendOutcome.SendFailed => RedirectWithMessage(
                "No se pudo enviar el correo (revise la configuración SMTP o la conexión). La solicitud quedó como Borrador — puede reintentar desde Solicitar Cambios de Domicilio.",
                isError: true),
            OutboundSendOutcome.Sent => RedirectWithMessage($"Solicitud creada y correo enviado a {result.DestinationComuna}."),
            _ => RedirectWithMessage("El correo se envió, pero la solicitud ya figuraba como enviada (posiblemente por otra pestaña/operador).")
        };
    }

    private void Load()
    {
        var filter = BuildFilter();
        if (!PageSizes.Contains(PageSize))
        {
            PageSize = Math.Max(options.PageSize, 10);
        }
        var pageSize = PageSize;

        TotalCount = cases.Count(filter);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)pageSize));
        PageNumber = Math.Clamp(RequestedPage <= 0 ? 1 : RequestedPage, 1, TotalPages);
        Cases = cases.Query(filter, (PageNumber - 1) * pageSize, pageSize);
        NeedsReviewCount = cases.CountNeedingReview();
        Years = cases.DistinctYears();
        DuplicateRuts = [.. cases.DuplicateRuts(filter)];

        // TempData sólo existe dentro de una petición; fuera de ella (por ejemplo en las pruebas de
        // la página) es null y leerla reventaría antes de llegar a lo que se está probando.
        if (TempData?["Message"] is string message)
        {
            Message = message;
            MessageIsError = TempData["MessageIsError"] is true;
        }
    }

    private CaseFilter BuildFilter() => new()
    {
        Office = Office,
        Year = Year,
        Month = Month,
        FolderState = Estado,
        FinalDecision = Decision,
        Sector = Sector,
        OnlyNeedsReview = NeedsReview,
        OnlyOtherComuna = OtherComuna,
        OnlyOverdue = Overdue,
        Search = Search,
        Sort = Sort,
        Descending = Descending
    };

    /// <summary>Route values for a sortable column header: clicking the active column flips the
    /// direction, clicking another one starts it in its own natural order. Paging resets to 1,
    /// since page 7 of the old ordering means nothing in the new one.</summary>
    public Dictionary<string, string> SortRouteValues(CaseSort column)
    {
        var values = RouteValues(1);
        values["sort"] = column.ToString();
        values["desc"] = (Sort == column && !Descending).ToString();
        return values;
    }

    /// <summary>Arrow shown next to the active column header.</summary>
    public string SortIndicator(CaseSort column)
        => Sort != column ? string.Empty : Descending ? " ▼" : " ▲";

    private IActionResult RedirectWithMessage(string message, bool isError = false)
    {
        // Fuera de una petición HTTP (pruebas de la página) no hay TempData que escribir.
        if (TempData is not null)
        {
            TempData["Message"] = message;
            TempData["MessageIsError"] = isError;
        }
        return RedirectToPage(new
        {
            office = Office,
            year = Year,
            month = Month,
            estado = Estado,
            decision = Decision,
            sector = Sector,
            needsReview = NeedsReview,
            otherComuna = OtherComuna,
            overdue = Overdue,
            search = Search,
            p = RequestedPage,
            size = PageSize,
            sort = Sort,
            desc = Descending
        });
    }

    /// <summary>Current filters as route values, so paging links keep the view the operator set up.</summary>
    public Dictionary<string, string> RouteValues(int page)
    {
        var values = new Dictionary<string, string>
        {
            ["needsReview"] = NeedsReview.ToString(),
            ["otherComuna"] = OtherComuna.ToString(),
            ["overdue"] = Overdue.ToString(),
            ["p"] = page.ToString()
        };

        if (Office is { } office) values["office"] = office.ToString();
        if (Year is { } year) values["year"] = year.ToString();
        if (Month is { } month) values["month"] = month.ToString();
        if (Estado is { } estado) values["estado"] = estado.ToString();
        if (Decision is { } decision) values["decision"] = decision.ToString();
        if (Sector is { } sector) values["sector"] = sector.ToString();
        if (!string.IsNullOrWhiteSpace(Search)) values["search"] = Search;

        values["sort"] = Sort.ToString();
        values["desc"] = Descending.ToString();
        values["size"] = (PageSizes.Contains(PageSize) ? PageSize : 0).ToString();

        return values;
    }

    /// <summary>Los cinco estados que significan "la carpeta ya se subió", en cualquiera de sus vías.</summary>
    private static bool IsUploadState(FolderState state) => state is FolderState.SubidaAConaset
        or FolderState.SubidaConF8
        or FolderState.SubidaConOficio
        or FolderState.CambioDomicilioSubidoAConaset
        or FolderState.CambioDomicilioSubidoConCorreo;

    /// <summary>Accepts what the operator is used to typing: "15-03-2024", "15/03/2024", "15 marzo 2024".</summary>
    internal static DateOnly? ParseDate(string? text)
        => SpanishDate.TryParse(text, out var date) ? date : null;

    /// <summary>"15/marzo/2024" — spelling the month out removes the day/month ambiguity of a plain
    /// numeric date, same reasoning as the printed sector list.</summary>
    public static string FormatDate(DateOnly? date)
        => date is { } value ? $"{value.Day}/{SpanishDate.MonthName(value.Month)}/{value.Year}" : string.Empty;
}
