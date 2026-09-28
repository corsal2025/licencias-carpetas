# Apply progress: sgl-kpi-fecha-decision-dias-habiles

Entrega: 3 slices encadenados, `stacked-to-main`.

## Slice 1 — `FinalDecisionAt`: esquema, backfill, 3 caminos de escritura (COMPLETO)

Estado: **hecho** (incluye fixes de revisión), sin commit (pendiente de
decisión del usuario/orquestador).

- `dotnet build`: 0 warnings, 0 errores.
- `dotnet test`: 621/621 (607 base + 14 nuevos), 0 fallos.
- Diff: `+417/-2` líneas en 3 archivos — dentro del presupuesto de 400.
- `data/carpetas.db` no se tocó.

Archivos modificados:
- `src/LicenciasCarpetas/Domain/FolderCase.cs` — propiedad `FinalDecisionAt`.
- `src/LicenciasCarpetas/Persistence/FolderCaseRepository.cs` — migración
  aditiva idempotente, backfill desde `CaseAuditLog`, regla de escritura
  (`ComputeFinalDecisionAtText`) aplicada en `Insert`, `Update` y
  `UpdateEditableFields`; `ReadTimestamp` defensivo (TryParse, nunca lanza) usado
  en `Map`; backfill normaliza `ChangedAt` (parse + reformat "O") y descarta
  filas con timestamp ilegible en vez de copiarlo verbatim.
- `tests/LicenciasCarpetas.Tests/FolderCaseRepositoryTests.cs` — 14 tests
  nuevos (idempotencia de esquema, backfill con/sin entrada de auditoría,
  backfill Denegado, backfill con `ChangedAt` ilegible, `Map` con
  `FinalDecisionAt` corrupto, las 3 reglas en los 3 caminos de escritura,
  regresión `ParaDenegar`).

### Fixes de revisión aplicados (mismo slice)

1. `Map`: `DateTimeOffset.Parse` reemplazado por `ReadTimestamp`
   (`TryParse` + `RoundtripKind`/`AssumeUniversal`), retorna `null` en vez de
   lanzar ante un valor corrupto — test agregado.
2. Verificado que `CaseAuditLog.ChangedAt` se escribe como
   `DateTimeOffset.UtcNow.ToString("O")` (ya UTC "O" garantizado); el backfill
   igual normaliza (parse + reformat) y descarta filas no parseables en vez de
   copiar el texto verbatim — test agregado.
3. Test de backfill agregado para el caso Denegado con entrada de auditoría
   (antes solo había cobertura para Otorgado).

Detalles y decisiones técnicas: ver `sdd/sgl-kpi-fecha-decision-dias-habiles/apply-progress`
en engram (proyecto `licencias-carpetas`).

## Slice 2 — `BusinessDayCalculator` + `KpiOptions` + feriados (COMPLETO)

Estado: **hecho**, sin commit (pendiente de decisión del usuario/orquestador).
Slice 1 ya está en main (commit `43d63eb`).

- `dotnet build`: 0 warnings, 0 errores.
- `dotnet test`: 630/630 (621 base tras slice 1 en main + 9 nuevos), 0 fallos.
- Diff: 77 líneas modificadas (`Program.cs`, ambos `appsettings*.json`) + 183
  líneas en 3 archivos nuevos (`BusinessDayCalculator.cs`, `KpiOptions.cs`,
  `BusinessDayCalculatorTests.cs`) = ~260 líneas — dentro del presupuesto de 400.
- `data/carpetas.db` no se tocó.

Archivos nuevos/modificados:
- `src/LicenciasCarpetas/Statistics/BusinessDayCalculator.cs` — portado del
  patch de referencia sin cambios de fondo.
- `src/LicenciasCarpetas/Statistics/KpiOptions.cs` — portado del patch de
  referencia; doc-comment agregado sobre el 21-jun pendiente de decreto.
- `src/LicenciasCarpetas/Program.cs` — DI de `KpiOptions` (leída de la sección
  `Kpi`) y `IBusinessDayCalculator`.
- `src/LicenciasCarpetas/appsettings.json` / `appsettings.Example.json` —
  sección `Kpi.Holidays` 2026-2027 con `"_comment"` explicando el 21-jun
  pendiente de decreto (Día Nacional de los Pueblos Indígenas).
- `tests/LicenciasCarpetas.Tests/BusinessDayCalculatorTests.cs` — 9 tests:
  lunes-viernes sin feriados, feriado configurado descontado, fin de semana no
  cuenta, mismo día = 0, rango invertido = 0, feriados configurados/no
  configurados por año, año sin feriados cae a regla lunes-viernes, feriado con
  formato no parseable se ignora.

Detalles y decisiones técnicas: ver `sdd/sgl-kpi-fecha-decision-dias-habiles/apply-progress`
en engram (proyecto `licencias-carpetas`).

## Slice 3 — `SedeKpiService` + página + Excel + docs (COMPLETO)

Estado: **hecho**, sin commit (pendiente de decisión del usuario/orquestador).
Slice 2 ya está en main (commit `1342c78`).

- `dotnet build`: 0 warnings, 0 errores.
- `dotnet test`: 633/633 (630 base tras slice 2 en main + 3 nuevos), 0 fallos.
- Diff: `+167/-46` líneas (213 netas) en 6 archivos, todos modificados (sin
  archivos nuevos) — dentro del presupuesto de 400.
- `data/carpetas.db` no se tocó.

Archivos modificados:
- `src/LicenciasCarpetas/Statistics/SedeKpiService.cs` — `Load` lee
  `FinalDecisionAt` directo (ya no subquery de `CaseAuditLog`), con parseo
  defensivo; constructor recibe `IBusinessDayCalculator`; `Compute` usa
  `ToChileDate` (America/Santiago, fallback Pacific SA Standard Time) +
  `BusinessDaysBetween`; `SedeKpi.PercentWithDecisionDate` (cobertura);
  `SedeKpiReport.HolidaysConfiguredForPeriod` (aviso por año faltante).
- `src/LicenciasCarpetas/Program.cs` — DI de `SedeKpiService` ahora pasa
  `IBusinessDayCalculator`.
- `src/LicenciasCarpetas/Dashboard/Pages/Estadisticas/Comparativo.cshtml` —
  etiquetas "días hábiles", columna de cobertura, aviso de feriados no
  configurados.
- `src/LicenciasCarpetas/Reporting/SedeKpiExcelExporter.cs` — columna "%
  cobertura fecha decisión" y aviso de feriados en la hoja Resumen.
- `tests/LicenciasCarpetas.Tests/SedeKpiServiceTests.cs` — test de días vía
  auditoría reescrito para usar `FinalDecisionAt` directo (en días hábiles, no
  calendario); 3 tests nuevos: conversión a zona horaria de Chile, alcance de
  oficina vacío, aviso de feriados no configurados por año.
- `docs/arquitectura.html` — `VERSION` v0.6.0 → v0.7.0, `CHANGELOG`,
  `INFO.KPI`, modelo de datos (`FinalDecisionAt` en `CARPETA`).

**Las 3 slices están completas.** Detalles y decisiones técnicas: ver
`sdd/sgl-kpi-fecha-decision-dias-habiles/apply-progress` en engram (proyecto
`licencias-carpetas`). Próximo paso recomendado: `sdd-verify`.
