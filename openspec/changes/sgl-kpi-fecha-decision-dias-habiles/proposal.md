# Propuesta: fecha de decisión final y días hábiles en KPI por sede

## Contexto

El dashboard comparativo (`sgl-kpi-dashboard-comparativo`, ya en main) calcula "días
citación → decisión" en días corridos, buscando la última entrada de auditoría
"Decisión final" con valor Otorgado/Denegado. No hay columna propia de fecha de
decisión, no se descuentan feriados chilenos y no hay indicador de cobertura de la
métrica. Existe un patch de referencia
(`backups-licencias/patch-kpi-entrega1/0001-...patch`) construido contra el código
previo al dashboard actual: se reutiliza su diseño (`BusinessDayCalculator`,
`KpiOptions`, columna `FinalDecisionAt`) pero se adapta a los tipos y servicios ya
existentes en main, sin duplicar `SedeKpiService` ni crear un segundo dashboard.

## Cambios propuestos

1. **Columna `FinalDecisionAt`** (`FolderCase`, TEXT ISO-8601 UTC, nullable):
   - Se fija a `DateTimeOffset.UtcNow` cuando `FinalDecision` cambia a
     Otorgado o Denegado.
   - Se limpia a `NULL` cuando cambia a cualquier otro valor (o se borra).
   - No se toca si el valor de `FinalDecision` no cambia.
   - Aplica en los tres caminos de escritura: `UpdateEditableFields` (pantalla de
     edición), `Insert`/`Update` (import/upsert de planilla).
   - Migración aditiva idempotente en `EnsureSchema` (mismo patrón
     `AddColumnIfMissing` ya usado) + backfill desde la última fila de
     `CaseAuditLog` con `FieldName = 'Decisión final'` y `NewValue` en
     (Otorgado, Denegado).

2. **`BusinessDayCalculator` + `KpiOptions`** (`Statistics/`, portados del patch):
   días hábiles lunes-viernes menos feriados chilenos configurables por
   `Kpi:Holidays` en `appsettings`. Sin feriados cargados para el año consultado,
   cae a lunes-viernes y la página muestra una advertencia visible. Se confirmó que
   `F8.Domain.DeadlineCalculator` y `CambioDomicilio.Domain.DeadlineCalculator` NO
   consideran feriados (comentario explícito "Chilean public holidays are NOT
   excluded"), así que no hay lógica de feriados que reutilizar de ahí — no hay
   duplicación.

3. **`SedeKpiService`**: reemplaza el cálculo de días desde el subquery de
   auditoría por `FinalDecisionAt` (convertido a fecha en zona horaria
   `America/Santiago`, con fallback a `"Pacific SA Standard Time"` en Windows) y
   `BusinessDayCalculator.BusinessDaysBetween`. Se agrega `PercentWithDecisionDate`
   (cobertura: % de casos Otorgado/Denegado con `FinalDecisionAt` no nulo) a
   `SedeKpi`. Alcance de oficina vacío (`OfficeScope` sin sedes) debe seguir
   devolviendo cero filas — se agrega un test que lo cubre explícitamente.

4. **Verificación de agrupación `ParaDenegar`**: revisado el código actual —
   `EnCurso` ya se calcula como "decisión no nula y distinta de
   Otorgado/Denegado", por lo que `ParaDenegar` ya cae en `EnCurso` (pendiente).
   No requiere cambio de lógica; se agrega un test de regresión explícito para
   dejarlo documentado y blindado contra futuros cambios de la enumeración.

5. **Página `Estadisticas/Comparativo`**: muestra el % de cobertura (casos con
   fecha de decisión) y la advertencia de feriados no configurados cuando
   corresponda.

6. **Excel (`SedeKpiExcelExporter`)**: agrega la columna de cobertura para que
   coincida con la página.

7. **Docs**: actualiza `docs/arquitectura.html` (VERSION, DATA, CHANGELOG).

## Fuera de alcance

- No se crea un segundo servicio de KPI ni una segunda página.
- No se migra el archivo `data/carpetas.db` de producción (se respalda antes de
  cualquier prueba manual; las pruebas automatizadas usan bases temporales).
- No se decreta el feriado 21-jun (Pueblos Indígenas) — se carga en
  `appsettings` marcado con comentario "pendiente confirmación por decreto".

## Riesgo de tamaño

Estimación ~550-650 líneas modificadas/agregadas (schema+backfill, calculador de
días hábiles, cambios en 3 caminos de escritura del repositorio, servicio KPI,
página, Excel, docs, y suite de tests con TDD estricto). Supera el presupuesto de
400 líneas por PR — se reporta a decisión del orquestador (`ask-on-risk`) antes de
implementar.
