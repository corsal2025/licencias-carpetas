# Tareas: fecha de decisión final y días hábiles en KPI por sede

Modo: TDD estricto (RED → GREEN → REFACTOR). Test runner: `dotnet test`.
Nunca tocar `data/carpetas.db`; respaldo con timestamp antes de correr la app
manualmente en `Documents/backups-licencias/`.

Decisión de entrega: **3 slices encadenados, `stacked-to-main`** (cada PR se
integra a main en orden; el siguiente slice parte de main ya con el anterior
mergeado).

## Slice 1 — `FinalDecisionAt`: esquema, backfill, 3 caminos de escritura + tests (COMPLETO)

- [x] 1.1 Test: `EnsureSchema` agrega `FinalDecisionAt` de forma idempotente sobre
      una base ya existente (patrón `AddColumnIfMissing`).
- [x] 1.2 Test: backfill llena `FinalDecisionAt` desde la última fila de
      `CaseAuditLog` (`FieldName = 'Decisión final'`, `NewValue` en Otorgado/Denegado)
      solo cuando `FinalDecision` actual es Otorgado/Denegado y la columna está NULL;
      si no hay entrada de auditoría, queda NULL.
- [x] 1.3 Implementar `AddColumnIfMissing(connection, "FinalDecisionAt")` +
      método `BackfillFinalDecisionAt` (mismo patrón que `BackfillFullNameSort`,
      corrido después de crear `CaseAuditLog`).
- [x] 1.4 Test: `UpdateEditableFields` — cambio a Otorgado/Denegado fija
      `FinalDecisionAt` a "ahora"; cambio a cualquier otro valor lo limpia; sin
      cambio de `FinalDecision`, el valor previo se conserva.
- [x] 1.5 Test: `Insert` (nuevo caso ya viene con decisión Otorgado/Denegado) fija
      `FinalDecisionAt`; si no, queda NULL.
- [x] 1.6 Test: `Update`/`Upsert` (import de planilla) — mismas tres reglas que 1.4,
      comparando contra el valor existente en la fila antes de sobrescribir
      (`SelectCurrentDecision`).
- [x] 1.7 Implementar en `UpdateEditableFields`, `Insert`, `Update`: helper
      compartido `ComputeFinalDecisionAtText(previous, previousAtText, next, now)`
      aplicado en los 3 caminos.
- [x] 1.8 Test de regresión: `ParaDenegar` se cuenta como pendiente/en curso (no
      Otorgado/Denegado) y no dispara `FinalDecisionAt` en `Upsert`.
- [x] 1.9 `dotnet build` sin warnings nuevos.
- [x] 1.10 `dotnet test` completo — 618/618 (607 base + 11 nuevos), 0 fallos.
- [x] 1.11 Guardar apply-progress (openspec + engram, topic_key
      `sdd/sgl-kpi-fecha-decision-dias-habiles/apply-progress`).

### Fixes de revisión (mismo slice, sin commit)

- [x] 1.12 Test: `Map` no lanza con un valor corrupto en `FinalDecisionAt` — se lee
      como "sin fecha".
- [x] 1.13 Implementar `ReadTimestamp` (TryParse con `RoundtripKind` +
      `AssumeUniversal`, retorna null en vez de lanzar) y usarlo en `Map`.
- [x] 1.14 Verificado cómo se escribe `CaseAuditLog.ChangedAt`
      (`RecordAuditLog` → `DateTimeOffset.UtcNow.ToString("O")`, ya es UTC "O"
      garantizado) — igual se normaliza (parse + reformat) durante el backfill
      en vez de copiar el texto verbatim, y se descarta la fila si no parsea.
- [x] 1.15 Test: backfill con `ChangedAt` ilegible no rompe `EnsureSchema` y deja
      la fila sin fecha.
- [x] 1.16 Test: backfill de un caso Denegado con entrada de auditoría.

Archivos tocados en slice 1: `src/LicenciasCarpetas/Domain/FolderCase.cs`,
`src/LicenciasCarpetas/Persistence/FolderCaseRepository.cs`,
`tests/LicenciasCarpetas.Tests/FolderCaseRepositoryTests.cs`.
Diff real (tras fixes de revisión): +417/-2 líneas (dentro de presupuesto de
400). `dotnet build`: 0 warnings, 0 errores. `dotnet test`: 621/621 (607 base +
14 nuevos), 0 fallos. No se tocó `data/carpetas.db`. No se hizo commit
(pendiente de decisión del usuario).

## Slice 2 — `BusinessDayCalculator` + `KpiOptions` + feriados (COMPLETO)

Nota: slice 1 fue committeado y pusheado a main (`43d63eb`) antes de empezar
este slice.

- [x] 2.1 Test: días hábiles lunes-viernes sin feriados configurados.
- [x] 2.2 Test: feriados configurados se descuentan; fin de semana nunca cuenta.
- [x] 2.3 Test: `HolidaysConfigured(year)` refleja si hay feriados cargados para
      ese año.
- [x] 2.3b Test adicional: mismo día → 0 días hábiles.
- [x] 2.3c Test adicional: rango invertido (`from` posterior a `to`) → 0 días
      hábiles, sin lanzar.
- [x] 2.3d Test adicional: año sin feriados configurados (pero con feriados
      cargados para otros años) → cae a regla lunes-viernes y
      `HolidaysConfigured(year)` es false para ese año.
- [x] 2.3e Test adicional: feriado con formato no parseable se ignora en vez de
      lanzar (defensivo, igual que el resto del módulo).
- [x] 2.4 Portado `Statistics/BusinessDayCalculator.cs` y `Statistics/KpiOptions.cs`
      del patch de referencia (namespace y estilo ya coincidían con main, sin
      cambios de fondo).
- [x] 2.5 Agregado `Kpi:Holidays` (2026-2027) a `appsettings.json` y
      `appsettings.Example.json`, con `"_comment"` explicando que el 21-jun
      (Día Nacional de los Pueblos Indígenas) queda pendiente de confirmación
      por decreto anual para 2026 y 2027.
- [x] 2.6 Registrado `KpiOptions` (singleton, leído de la sección `Kpi`) y
      `IBusinessDayCalculator` (`BusinessDayCalculator`) en `Program.cs`.
- [x] 2.7 `dotnet build`: 0 warnings, 0 errores. `dotnet test`: 630/630 (621
      base tras slice 1 en main + 9 nuevos), 0 fallos.
- [x] 2.8 Actualizar apply-progress (merge, no sobrescribir slice 1).

Archivos tocados en slice 2:
- `src/LicenciasCarpetas/Statistics/BusinessDayCalculator.cs` (nuevo, 57 líneas)
- `src/LicenciasCarpetas/Statistics/KpiOptions.cs` (nuevo, 20 líneas)
- `src/LicenciasCarpetas/Program.cs` (+3 líneas, registro DI)
- `src/LicenciasCarpetas/appsettings.json` (+37 líneas, sección `Kpi`)
- `src/LicenciasCarpetas/appsettings.Example.json` (+37 líneas, sección `Kpi`)
- `tests/LicenciasCarpetas.Tests/BusinessDayCalculatorTests.cs` (nuevo, 106
  líneas, 9 tests)

Diff real: 77 líneas modificadas (Program.cs + ambos appsettings) + 183 líneas
en 3 archivos nuevos = ~260 líneas — dentro de presupuesto de 400. No se tocó
`data/carpetas.db`. No se hizo commit (pendiente de decisión del usuario).

## Slice 3 — `SedeKpiService` + página + Excel + docs (COMPLETO)

Nota: slice 2 fue committeado y pusheado a main (`1342c78`) antes de empezar
este slice.

- [x] 3.1 Test: días citación→decisión usan `FinalDecisionAt` (no ya el subquery de
      auditoría) convertido a fecha en `America/Santiago` (fallback
      `"Pacific SA Standard Time"`), contados en días hábiles vía
      `BusinessDayCalculator`.
- [x] 3.1b Test: `FinalDecisionAt` que cruza la medianoche UTC se sigue contando
      por la fecha de Chile, no la de UTC.
- [x] 3.2 Test: `PercentWithDecisionDate` = % de Otorgado+Denegado con
      `FinalDecisionAt` no nulo (agregado al test de la muestra de días y
      verificado explícitamente: 75% con 1 de 4 decididos sin fecha).
- [x] 3.3 Test explícito: alcance de oficina vacío (`OfficeScope` sin sedes)
      devuelve reporte con cero filas en todos los bloques (`Offices` vacío,
      `Total` en cero, `AverageDays` null, `Trend` vacío).
- [x] 3.3b Test: el reporte avisa (`HolidaysConfiguredForPeriod = false`)
      cuando el período cae en un año sin feriados configurados, y `true`
      cuando el año sí los tiene.
- [x] 3.4 Implementado en `SedeKpiService`: `Load` ahora lee `f.FinalDecisionAt`
      directo (ya no el subquery de `CaseAuditLog`), con parseo defensivo
      (`TryParse` + `RoundtripKind`/`AssumeUniversal`, nunca lanza);
      `IBusinessDayCalculator` inyectado por constructor; `Compute` calcula
      días hábiles vía `ToChileDate` + `businessDays.BusinessDaysBetween`;
      `SedeKpi.PercentWithDecisionDate` agregado (propiedad derivada, sin
      almacenamiento extra); `SedeKpiReport.HolidaysConfiguredForPeriod`
      agregado (calculado en `Build` sobre todos los años del período).
- [x] 3.5 Actualizado `Comparativo.cshtml`: etiquetas "días hábiles", columna de
      cobertura de fecha de decisión, aviso visible cuando
      `HolidaysConfiguredForPeriod` es false.
- [x] 3.6 Actualizado `SedeKpiExcelExporter`: columna "% cobertura fecha
      decisión" y aviso de feriados no configurados en la hoja Resumen,
      consistente con la página.
- [x] 3.7 Actualizado `docs/arquitectura.html`: `VERSION` v0.6.0 → v0.7.0,
      entrada en `CHANGELOG`, `INFO.KPI` y el modelo de datos (`FinalDecisionAt`
      en `CARPETA`).
- [x] 3.8 `dotnet build`: 0 warnings, 0 errores. `dotnet test`: 633/633 (630
      base tras slice 2 en main + 3 nuevos), 0 fallos.
- [x] 3.9 Actualizar apply-progress (merge final, marcar change lista para
      `sdd-verify`).

Archivos tocados en slice 3 (todos modificados, sin archivos nuevos):
- `src/LicenciasCarpetas/Statistics/SedeKpiService.cs`
- `src/LicenciasCarpetas/Program.cs`
- `src/LicenciasCarpetas/Dashboard/Pages/Estadisticas/Comparativo.cshtml`
- `src/LicenciasCarpetas/Reporting/SedeKpiExcelExporter.cs`
- `tests/LicenciasCarpetas.Tests/SedeKpiServiceTests.cs`
- `docs/arquitectura.html`

Diff real: `+167/-46` líneas (213 netas) en 6 archivos — dentro de presupuesto
de 400. No se tocó `data/carpetas.db`. No se hizo commit (pendiente de decisión
del usuario).

**Las 3 slices del change `sgl-kpi-fecha-decision-dias-habiles` están
completas.** Próximo paso recomendado: `sdd-verify`.

## Estimación de líneas por slice

| Slice | Líneas aprox. | Real |
|---|---|---|
| 1 — FinalDecisionAt (schema+writes+tests) | ~260 | **+306/-2** |
| 2 — BusinessDayCalculator/KpiOptions/appsettings/DI/tests | ~190 | pendiente |
| 3 — SedeKpiService/página/Excel/docs/tests | ~200 | pendiente |

Cada slice se mantiene bajo el presupuesto de 400 líneas por PR.
