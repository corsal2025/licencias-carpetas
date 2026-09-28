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

## Slice 2 — `BusinessDayCalculator` + `KpiOptions` + feriados

Estado: **pendiente**. Ver `tasks.md` sección "Slice 2".

## Slice 3 — `SedeKpiService` + página + Excel + docs

Estado: **pendiente**. Ver `tasks.md` sección "Slice 3".
