# Verify Report: cambio-domicilio-integracion (re-verificacion post-remediacion)

**Fecha:** 2026-08-31
**Estado:** pass — 0 CRITICAL, 0 WARNING, 2 SUGGESTION
**Build:** `dotnet build -c Release` -> 0 errores, 0 advertencias
**Tests:** `dotnet test -c Release` -> 520 superados, 0 con error, 0 omitidos (antes: 511)
**Reemplaza:** el verify previo (2 CRITICAL + 6 WARNING). Los 8 desvios estan cerrados en HEAD.

## Veredicto

La remediacion sostiene. Cada uno de los 8 hallazgos tiene fix en codigo Y cobertura de test
(los 9 tests nuevos explican el salto 511 -> 520). No quedan bloqueantes: **listo para sdd-archive**.

## Verificacion item por item

### C1 — Reporte CSV por ciclo — CERRADO

`appsettings.json:31` publica `"ReportCsvPath": "data/reports/cambio-domicilio.csv"`.
`Program.cs:99-100` la resuelve via `CambioDomicilioPathResolver.ResolveAgainstBaseDirectory(...,
AppContext.BaseDirectory)` antes de registrar las options, asi que
`CambioDomicilioSyncService.cs:103-106` ya escribe el reporte en toda ejecucion real.

### C2 — Acoplamiento con ComunaContact — CERRADO

- `SeedRepositoryFromDefault()` y las 21 comunas ficticias: **eliminados** del arbol
  (`rg SeedRepositoryFromDefault src/` sin resultados).
- `ComunaDirectory.EnsureSeed` (ComunaDirectory.cs:44-70) es estrictamente unidireccional
  ComunaContact -> CSV: si el repo es null o `All().Count == 0` retorna sin escribir nada. Nunca
  hace Upsert contra `ComunaContact`.
- Specs/design reescritos sin contradiccion: el requisito de la spec de routing ahora es
  "Routing Directory Is a One-Directional Projection of ComunaContact" (spec.md:115-132), con los
  escenarios *ComunaContact is never written by the routing module* y *Empty source produces no
  directory, not fake data*. Ya no existe el "MUST NOT merge" que contradecia el codigo.
- Tests: `ComunaDirectoryTests.EnsureSeed_MissingCsv_ProjectsRowsFromComunaContactTableOneDirectionally`
  y `EnsureSeed_EmptyComunaContactTable_DoesNotFabricateADirectory`.

### W1 — SyncService ya no es BackgroundService — CERRADO

`CambioDomicilioSyncService.cs:38` es `sealed class` sin herencia; no hay `ExecuteAsync`.
`RunCycleAsync` es **public** (linea 50). El `SemaphoreSlim cycleGuard` (linea 46) sigue intacto y
el registro singleton sigue siendo lo que hace real el guard de solape (documentado en el XML doc).

### W2 — Conteos en la UI — CERRADO

Nuevo record `CambioDomicilioSyncResult(Outcome, Creados, Descartados, ParaRevision)` (lineas 24-28).
Se calcula por diff de ids antes/despues del ciclo y `DiscardedCount()` (lineas 69-101).
`Index.cshtml.cs:310-312` muestra "Sincronizacion completada: N caso(s) creado(s), N para revision,
N correo(s) descartado(s)". Test: `CambioDomicilioSyncServiceTests` asserta 1/1/1.

### W3 — Directorio vacio — CERRADO

Nuevo miembro `CambioDomicilioSyncOutcome.SkippedNoDirectory` (linea 17); `RunCycleAsync:61-67` lo
retorna con `LogCritical` cuando `contacts.Count == 0`. `Index.cshtml.cs:313-315` lo mapea a mensaje
de error (`MessageIsError = true`) apuntando a la clave de config. Test en linea 113.

### W4 — Resolucion contra BaseDirectory — CERRADO

`CambioDomicilioPathResolver` nuevo; `Program.cs:97-102` resuelve las tres rutas
(`ComunaDirectoryCsvPath`, `ReportCsvPath`, `SolicitarMatrizExcelPath`) **una sola vez** al arranque
mutando el objeto de options. Todos los consumidores (AddressChangeRoutingService, SyncService,
Comunas.cshtml.cs, EnsureSeed) leen ya el valor absoluto — la fix centralizada es mejor que parchear
cada call site.

### W5 — Scope de Solicitar documentado — CERRADO

`design.md:191-209` agrega "Fase anadida: Solicitar / OutboundAddressChangeRequest" con tabla de
componentes y config; `design.md:62-65` agrega la nota de coexistencia de esquema del tercer modulo
(sin colision de nombres en `carpetas.db`). `tasks.md` Phase 7 (7.1-7.6) enumera el trabajo.

### W6 — Test de nav — CERRADO

`CambioDomicilioAccessTests.The_nav_hides_the_cambio_domicilio_entry_from_a_user_without_the_claim`
compara el HTML de `/Inicio` para un usuario con y sin el claim, con control positivo
(`Assert.Contains` en el autorizado) para que el test no pueda pasar de forma vacia.

## SUGGESTION (no bloquean archive)

- **S1** — `proposal.md:60` conserva la frase "Cero fusion" en la seccion de preguntas abiertas.
  Sigue siendo cierta a nivel de tablas (`ComunaContact` y el CSV siguen separados, sin merge de
  esquemas), pero se lee ambigua frente al modelo de proyeccion ya especificado. Una nota de una
  linea la alinearia; el criterio de exito en `proposal.md:85` ya documenta la remediacion.
- **S2** — Persisten S2/S3 del reporte anterior (falta test de routing para RUT con digito
  verificador invalido; Open Questions de design.md sin cerrar, incluida `ToastNotificationsEnabled`
  corriendo como servicio). Cosmeticos/deuda menor.

## Trazabilidad

| Fase | Estado |
|---|---|
| 1-2 Domain/Data | Completo, testeado. |
| 3 Extraction/Directories/Routing | Completo. 3.5 ahora si cumple design (W1). |
| 4 Ews/Notifications/Reporting/Statistics | Completo; reporting ahora efectivo en runtime (C1). |
| 5 Paginas/Program/appsettings/Layout | Completo; 5.8 cierra con ReportCsvPath. |
| 6 E2E + README | Completo. |
| 7 Solicitar (fase anadida) | Documentada y marcada (W5). |
| DoD | Build limpio; 520/520 tests. |

## Recomendacion

**sdd-archive.** Sin CRITICAL ni WARNING pendientes.
