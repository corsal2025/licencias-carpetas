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

## Slice 2 — `BusinessDayCalculator` + `KpiOptions` + feriados (pendiente)

- [ ] 2.1 Test: días hábiles lunes-viernes sin feriados configurados.
- [ ] 2.2 Test: feriados configurados se descuentan; fin de semana nunca cuenta.
- [ ] 2.3 Test: `HolidaysConfigured(year)` refleja si hay feriados cargados para
      ese año.
- [ ] 2.4 Portar `Statistics/BusinessDayCalculator.cs` y `Statistics/KpiOptions.cs`
      del patch de referencia (namespace y estilo ya coinciden con main).
- [ ] 2.5 Agregar `Kpi:Holidays` (2026-2027) a `appsettings.json` y
      `appsettings.Example.json`, con comentario sobre 21-jun pendiente de decreto.
- [ ] 2.6 Registrar `KpiOptions` y `IBusinessDayCalculator` en `Program.cs`.
- [ ] 2.7 `dotnet build` + `dotnet test`, reportar conteo.
- [ ] 2.8 Actualizar apply-progress (merge, no sobrescribir slice 1).

## Slice 3 — `SedeKpiService` + página + Excel + docs (pendiente)

- [ ] 3.1 Test: días citación→decisión usan `FinalDecisionAt` (no ya el subquery de
      auditoría) convertido a fecha en `America/Santiago` (fallback
      `"Pacific SA Standard Time"`), contados en días hábiles vía
      `BusinessDayCalculator`.
- [ ] 3.2 Test: `PercentWithDecisionDate` = % de Otorgado+Denegado con
      `FinalDecisionAt` no nulo.
- [ ] 3.3 Test explícito: alcance de oficina vacío (`OfficeScope` sin sedes)
      devuelve reporte con cero filas en todos los bloques.
- [ ] 3.4 Implementar cambios en `SedeKpiService` (`Load`, `Compute`,
      `SedeKpi.PercentWithDecisionDate`), inyectando `IBusinessDayCalculator`.
- [ ] 3.5 Actualizar `Comparativo.cshtml`/`.cshtml.cs`: mostrar % de cobertura y
      advertencia cuando `HolidaysConfigured(year)` es false para el año del
      período.
- [ ] 3.6 Actualizar `SedeKpiExcelExporter` con columna de cobertura consistente
      con la página.
- [ ] 3.7 Actualizar `docs/arquitectura.html` (VERSION, DATA, CHANGELOG) con el
      resumen del cambio completo.
- [ ] 3.8 `dotnet build` + `dotnet test`, reportar conteo final.
- [ ] 3.9 Actualizar apply-progress (merge final, marcar change lista para
      `sdd-verify`).

## Estimación de líneas por slice

| Slice | Líneas aprox. | Real |
|---|---|---|
| 1 — FinalDecisionAt (schema+writes+tests) | ~260 | **+306/-2** |
| 2 — BusinessDayCalculator/KpiOptions/appsettings/DI/tests | ~190 | pendiente |
| 3 — SedeKpiService/página/Excel/docs/tests | ~200 | pendiente |

Cada slice se mantiene bajo el presupuesto de 400 líneas por PR.
