# Tasks: integrar Cambio de Domicilio en licencias-carpetas

## Apply-progress (2026-08-31 — remediación post-verify)

Fixes aplicados sobre los hallazgos de `verify-report.md` (TDD estricto, suite 511 → 520 verde):

- **C1** — `appsettings.json` ahora incluye `CambioDomicilio:ReportCsvPath` (`data/reports/cambio-domicilio.csv`),
  resuelto contra `AppContext.BaseDirectory` en `Program.cs`. El reporte CSV por ciclo se escribe de verdad.
- **C2** — Decisión del usuario: `ComunaContact` es la única fuente editable; el CSV de ruteo es una
  proyección de solo lectura en un solo sentido. Se eliminó `SeedRepositoryFromDefault()` y el
  `DefaultSeedCsv` de 21 comunas ficticias de `ComunaDirectory.cs`. `EnsureSeed` ya no fabrica datos;
  si `ComunaContact` está vacío, el directorio queda ausente y el ciclo devuelve `SkippedNoDirectory`.
  Specs (`cambio-domicilio-routing/spec.md`) y `design.md` enmendados.
- **W1** — `CambioDomicilioSyncService` ya no hereda de `BackgroundService`; `ExecuteAsync` eliminado;
  `RunCycleAsync` es `public`; `SemaphoreSlim cycleGuard` sigue como campo `private readonly`.
- **W2** — `RunCycleAsync` devuelve `CambioDomicilioSyncResult(Outcome, Creados, Descartados, ParaRevision)`;
  `Index.cshtml.cs` muestra esos conteos en el mensaje de estado; el CSV refleja lo mismo.
- **W3** — directorio vacío/ilegible → `CambioDomicilioSyncOutcome.SkippedNoDirectory` con mensaje de
  error al operador, ya no `Completed`.
- **W4** — `ComunaDirectoryCsvPath`, `ReportCsvPath` y `SolicitarMatrizExcelPath` se resuelven contra
  `AppContext.BaseDirectory` en `Program.cs` (`CambioDomicilioPathResolver`) antes de registrar el singleton.
- **W5** — flujo *Solicitar* / `OutboundAddressChangeRequest` documentado como fase añadida (Fase 7) y
  en `design.md`; nota de coexistencia de esquema (tercer módulo) agregada.
- **W6** — test nuevo: la entrada de nav no aparece en el HTML servido a un usuario sin el claim
  `mod:cambio-domicilio` (`CambioDomicilioAccessTests`).
- SUGGESTIONs S1–S3 quedan abiertas (cosméticas / verificación de toast en servicio); S4 resuelto
  (criterios de éxito del proposal marcados).

## Review Workload Forecast

Basado en un conteo de líneas real de `outlook-comuna-router/src/CambioDeDomicilio/**/*.cs`
(4349 líneas totales en 49 archivos `.cs`), descontando lo explícitamente fuera de alcance
(`Dashboard/Auth/*`, `Login/Logout/ChangePassword/Forgot/ResetPassword`, `F8.cshtml.cs`,
`SectorF8.cshtml.cs`, `Program.cs` de la hermana = ~1035 líneas en 15 archivos):

| Concepto | Estimado |
|---|---|
| Código portado (Domain/Data/Ews/Extraction/Routing/Directories/Notifications/Reporting/Statistics + `CambioDomicilioOptions.cs`) | ~3314 líneas, ~35 archivos nuevos |
| Páginas Dashboard nuevas (6 × `.cshtml` + `.cshtml.cs`) | ~300 líneas, 12 archivos nuevos |
| Wiring host (`Program.cs`, `appsettings.json`, `_Layout.cshtml`) | ~50 líneas, 3 archivos modificados |
| Tests (unit + integración + E2E, TDD estricto) | ~900-1400 líneas, ~12-16 archivos nuevos |
| README.md | ~10 líneas, 1 archivo modificado |
| **Total estimado** | **~4600-5100 líneas cambiadas, ~63-67 archivos** |

- **Chained/stacked PRs recomendados: Sí.** El total estimado (~4600-5100 líneas) supera
  ampliamente el umbral de 400 líneas de este proyecto, incluso contando solo el código portado
  sin tests (~3314 líneas ya lo supera 8x).
- **Riesgo de presupuesto de 400 líneas: Alto.** Ninguna fase individual de este plan por sí sola
  se mantiene bajo 400 líneas salvo las fases de wiring/dashboard tardías; las fases de
  Data/Ews/Routing son las más pesadas.
- **Decisión necesaria antes de apply: Sí.** El orquestador debe preguntar cómo dividir
  (`delivery_strategy: ask-on-risk` ya seleccionado por el usuario) — este documento no decide el
  split, solo lo dimensiona con datos reales.

Nota: las fases más abajo ya están secuenciadas como slices verticales pequeños (Domain → Data →
Extraction/Directories/Routing → Ews/Notifications → Dashboard/wiring → E2E), lo que las hace aptas
para convertirse directamente en PRs encadenados si el usuario así lo decide.

---

## Phase 1 — Domain (nothing else compiles without this)

- [x] 1.1 Create `src/LicenciasCarpetas/CambioDomicilio/Domain/PersonRequest.cs` (port `RequestStatus`, `CaseDestination`, `FolderSector*` from sibling `Domain/PersonRequest.cs`, namespace `CambioDomicilio.Domain`) + `tests/LicenciasCarpetas.Tests/CambioDomicilio/Domain/PersonRequestTests.cs`
- [x] 1.2 Create `src/LicenciasCarpetas/CambioDomicilio/Domain/DiscardedEmail.cs` (port from sibling `Domain/DiscardedEmail.cs`) + test file if sibling has one to port
- [x] 1.3 Create `src/LicenciasCarpetas/CambioDomicilio/Domain/ComunaRoutingEntry.cs` (port sibling `Domain/ComunaContact.cs` renamed per design.md, includes `IncomingEmail`) + `tests/LicenciasCarpetas.Tests/CambioDomicilio/Domain/ComunaRoutingEntryTests.cs`
- [x] 1.4 Create `src/LicenciasCarpetas/CambioDomicilio/Domain/EmailShapeValidator.cs` (port from sibling) + `tests/LicenciasCarpetas.Tests/CambioDomicilio/Domain/EmailShapeValidatorTests.cs`
- [x] 1.5 Create `src/LicenciasCarpetas/CambioDomicilio/Domain/DeadlineCalculator.cs` (port from sibling, note namespace-only separation from `F8.Domain.DeadlineCalculator` per design.md) + port sibling's existing `DeadlineCalculator` tests to `tests/LicenciasCarpetas.Tests/CambioDomicilio/Domain/DeadlineCalculatorTests.cs`
- [x] 1.6 Create `src/LicenciasCarpetas/CambioDomicilio/Domain/SpanishDate.cs` (port from sibling) + test file if sibling has one
- [x] 1.7 Create `src/LicenciasCarpetas/CambioDomicilio/CambioDomicilioOptions.cs` (port sibling `Configuration/RouterOptions.cs`, apply the 3-key removal + all-optional-properties changes from design.md's config mapping table)

## Phase 2 — Data (repositories against real SQLite, mirroring existing repo test pattern)

- [x] 2.1 Read `tests/LicenciasCarpetas.Tests/` for the existing SQLite-file-backed repo test pattern (e.g. `FolderCaseRepository` tests) to confirm setup/teardown convention before writing new tests
- [x] 2.2 Create `src/LicenciasCarpetas/CambioDomicilio/Data/ICambioDomicilioRequestRepository.cs` + `CambioDomicilioRequestRepository.cs` (port sibling `Persistence/PersonRequestRepository.cs`, renamed per design.md, `EnsureSchema()` creates `PersonRequest` + `DeletedSourceMessage` tables) + `tests/LicenciasCarpetas.Tests/CambioDomicilio/Data/CambioDomicilioRequestRepositoryTests.cs` (real temp SQLite file; assert coexistence with `FolderCase`/`UrgentRequest` tables per dashboard spec's Schema Coexistence requirement)
- [x] 2.3 Create `src/LicenciasCarpetas/CambioDomicilio/Data/IDiscardedEmailRepository.cs` + `DiscardedEmailRepository.cs` (port sibling `Persistence/DiscardedEmailRepository.cs`, `EnsureSchema()` creates `DiscardedEmail` table) + `tests/LicenciasCarpetas.Tests/CambioDomicilio/Data/DiscardedEmailRepositoryTests.cs` (real temp SQLite file)

## Phase 3 — Extraction, Directories, Routing

- [x] 3.1 Create `src/LicenciasCarpetas/CambioDomicilio/Extraction/RutValidator.cs` (port from sibling) + port sibling's existing test file to `tests/LicenciasCarpetas.Tests/CambioDomicilio/Extraction/RutValidatorTests.cs`
- [x] 3.2 Create `src/LicenciasCarpetas/CambioDomicilio/Extraction/PersonDataExtractor.cs` (port from sibling) + port sibling's existing test file to `tests/LicenciasCarpetas.Tests/CambioDomicilio/Extraction/PersonDataExtractorTests.cs`
- [x] 3.3 Create `src/LicenciasCarpetas/CambioDomicilio/Directories/IComunaDirectory.cs` + `ComunaDirectory.cs` (port from sibling, CSV format untouched) + port sibling's existing `ResolveByDomain` test coverage to `tests/LicenciasCarpetas.Tests/CambioDomicilio/Directories/ComunaDirectoryTests.cs` (exact match, single-owner domain fallback, ambiguous-owner unresolved scenarios from routing spec)
- [x] 3.4 Create `src/LicenciasCarpetas/CambioDomicilio/Routing/AddressChangeRoutingService.cs` (port from sibling) + `tests/LicenciasCarpetas.Tests/CambioDomicilio/Routing/AddressChangeRoutingServiceTests.cs`
- [x] 3.5 Create `src/LicenciasCarpetas/CambioDomicilio/Routing/CambioDomicilioSyncService.cs` (port sibling `RouterWorker.cs`, converted from `BackgroundService` to plain singleton per design.md — `RunCycleAsync` public, `SemaphoreSlim cycleGuard` as private readonly field) + `tests/LicenciasCarpetas.Tests/CambioDomicilio/Routing/CambioDomicilioSyncServiceTests.cs` (cover overlap-prevention scenario from routing spec using fake dependencies) — *remediado post-verify (W1/W2/W3): singleton plano sin `BackgroundService`, `RunCycleAsync` público devolviendo `CambioDomicilioSyncResult` con conteos, outcome `SkippedNoDirectory` para directorio vacío*

## Phase 4 — Ews, Notifications, Reporting, Statistics

- [x] 4.1 Create `src/LicenciasCarpetas/CambioDomicilio/Ews/{IEwsClient.cs,EwsClient.cs}` (port from sibling)
- [x] 4.2 Create `src/LicenciasCarpetas/CambioDomicilio/Ews/{IEmailReader.cs,IEmailMover.cs,EwsEmailReader.cs}` (port sibling `Mail/IEmailReader.cs` + `Ews/EwsEmailReader.cs`, folded per design.md) + `tests/LicenciasCarpetas.Tests/CambioDomicilio/Ews/EwsEmailReaderTests.cs` if sibling has coverage to port
- [x] 4.3 Create `src/LicenciasCarpetas/CambioDomicilio/Ews/{IMailSender.cs,EwsMailSender.cs}` (port sibling `Mail/IMailSender.cs` + `Ews/EwsMailSender.cs`, folded per design.md)
- [x] 4.4 Create `src/LicenciasCarpetas/CambioDomicilio/Ews/{EwsFolderRef.cs,EwsMessages.cs,EwsResponseParser.cs}` (port from sibling) + port sibling's existing `EwsResponseParser` tests if any
- [x] 4.5 Create `src/LicenciasCarpetas/CambioDomicilio/Notifications/{INotificationChannel.cs,EmailNotificationChannel.cs,EmailTemplates.cs,WindowsToastNotificationChannel.cs}` (port from sibling as-is)
- [x] 4.6 Create `src/LicenciasCarpetas/CambioDomicilio/Reporting/{ICsvReportWriter.cs,CsvReportWriter.cs}` (port from sibling) + `tests/LicenciasCarpetas.Tests/CambioDomicilio/Reporting/CsvReportWriterTests.cs` (cycle reporting accuracy scenario from routing spec)
- [x] 4.7 Create `src/LicenciasCarpetas/CambioDomicilio/Statistics/CambioDomicilioStatisticsService.cs` (port sibling `Statistics/StatisticsService.cs`, renamed per design.md)

## Phase 5 — Dashboard pages + host wiring

- [x] 5.1 Create `src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/{Index.cshtml,Index.cshtml.cs}` (port sibling `Dashboard/Pages/Index.cshtml.cs`, `OnPostSyncNowAsync` per design.md's exact snippet, `[Authorize(Policy = "CambioDomicilioAccess")]`)
- [x] 5.2 Create `src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/{Discarded.cshtml,Discarded.cshtml.cs}` (port from sibling)
- [x] 5.3 Create `src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/{Comunas.cshtml,Comunas.cshtml.cs}` (port from sibling)
- [x] 5.4 Create `src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/{Estadisticas.cshtml,Estadisticas.cshtml.cs}` (port from sibling)
- [x] 5.5 Create `src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/{Certificado.cshtml,Certificado.cshtml.cs}` (port from sibling)
- [x] 5.6 Create `src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/{Sector.cshtml,Sector.cshtml.cs}` (port from sibling)
- [x] 5.7 Edit `src/LicenciasCarpetas/Program.cs`: add DI registration block (options binding + all services from design.md's DI block, after the F8 block ~line 62) and add the two `EnsureSchema()` calls to `EnsureSchemas()` right after `IUrgentRequestRepository.EnsureSchema()` (~line 258)
- [x] 5.8 Edit `appsettings.json`: add `CambioDomicilio:` section per design.md's config table (excluding `Ews:Username`/`Ews:Password`) — *remediado post-verify (C1): agregada la clave `ReportCsvPath`, resuelta contra `AppContext.BaseDirectory` en `Program.cs` (W4) junto con `ComunaDirectoryCsvPath` y `SolicitarMatrizExcelPath` vía `CambioDomicilioPathResolver`*
- [x] 5.9 Edit `src/LicenciasCarpetas/Dashboard/Pages/Shared/_Layout.cshtml`: replace external link at line 78 with `asp-page="/CambioDomicilio/Index"`, gated to `CambioDomicilioAccess`-authorized users only, and update the stale comment on lines 72-75

## Phase 6 — End-to-end smoke pass

- [x] 6.1 Add `tests/LicenciasCarpetas.Tests/CambioDomicilio/CambioDomicilioAccessTests.cs` using `WebApplicationFactory<Program>`: authorized user reaches `/CambioDomicilio/Index` (200), unauthorized user gets 403/redirect without unhandled exception (dashboard spec Access Control requirement)
- [x] 6.2 Add `tests/LicenciasCarpetas.Tests/CambioDomicilio/CambioDomicilioStartupTests.cs`: app starts normally with no `CambioDomicilio:` section configured, other modules unaffected (routing spec's Configuration Section requirement)
- [x] 6.3 Add `tests/LicenciasCarpetas.Tests/CambioDomicilio/OnPostSyncNowTests.cs`: `IndexModel.OnPostSyncNowAsync` end-to-end with a fake `IEmailReader`/`IEwsClient`, asserting create/discard/unresolved counts match (routing spec's Cycle Reporting scenario)
- [x] 6.4 Update `README.md` module list/structure section to mention `CambioDomicilio`

## Phase 7 — Solicitar / OutboundAddressChangeRequest (fase añadida, documentada post-verify — W5)

Flujo inverso "pedirle carpetas a otras comunas", portado del mismo `outlook-comuna-router`. No
estaba en el proposal original; se implementó durante el apply y se documenta aquí y en `design.md`
("Fase añadida: Solicitar / OutboundAddressChangeRequest" + nota de coexistencia de esquema).

- [x] 7.1 `src/LicenciasCarpetas/CambioDomicilio/Domain/OutboundAddressChangeRequest.cs` (+ `OutboundAddressChangeAttachment`, estado `Borrador`)
- [x] 7.2 `src/LicenciasCarpetas/CambioDomicilio/Data/{IOutboundAddressChangeRequestRepository,OutboundAddressChangeRequestRepository}.cs` (tablas `OutboundAddressChangeRequest` + `OutboundAddressChangeAttachment` vía `EnsureSchema()`) + tests de repo real contra SQLite temporal
- [x] 7.3 `src/LicenciasCarpetas/CambioDomicilio/Solicitar/{OutboundRequestSender,SolicitarMatrizSyncService}.cs` (envío EWS + carga desde Excel opcional `CambioDomicilio:SolicitarMatrizExcelPath`)
- [x] 7.4 `src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/Solicitar/*.cshtml(.cs)` con `[Authorize(Policy = "CambioDomicilioAccess")]` + tests de page model
- [x] 7.5 `Program.cs`: DI de los repos/servicios de Solicitar y `EnsureSchema()` de sus tablas dentro de `EnsureSchemas()`
- [x] 7.6 `_Layout.cshtml`: segunda entrada de nav (`📤 Solicitar Cambios de Domicilio`) gateada por `puedeCambioDomicilio`
- [x] 7.7 Nota de coexistencia de esquema: `carpetas.db` aloja ahora tres módulos; nombres de tabla verificados sin colisión

## Definition of Done

- [x] `dotnet build -c Release` passes with zero errors/warnings introduced by this change
- [x] `dotnet test -c Release` passes, including all new `CambioDomicilio` test files
