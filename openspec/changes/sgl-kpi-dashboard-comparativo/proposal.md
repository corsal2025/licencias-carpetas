# Propuesta: Dashboard KPI comparativo por sede

## Intención

Hoy `/Estadisticas` muestra un mes sin comparar sedes, sin tiempos citación→decisión y sin backlog envejecido. Jefatura necesita ver en una sola pantalla cómo rinden Av. Argentina, Placilla y Merc. Puerto, filtrar por periodo y exportar a Excel.

## Alcance

### Dentro
- Servicio `SedeKpiService` (en `Statistics/`) que reutiliza los breakdowns existentes del repositorio (`FolderStateBreakdown`, `FinalDecisionBreakdown` ya aceptan `Office?`) y agrega consultas por rango de fechas.
- Página delgada `/Estadisticas/Comparativo` + enlace en el menú.
- KPIs por sede: Otorgado / Denegado / Pendiente, desglose por estado de carpeta, promedio y mediana de días citación→decisión, backlog en verde/amarillo/rojo (reutiliza `AgingAlert`: <7, 7–14, ≥15 días), tendencia mensual.
- Filtros: periodo (mes / semestre / año) y sede (todas o una).
- Export Excel con ClosedXML (una hoja por bloque).
- Indicador de calidad de datos: % de casos sin decisión final y % sin fecha de decisión confiable.
- Parámetro `OfficeScope` (lista de sedes permitidas) en el servicio, hoy siempre "todas".
- Actualizar `docs/arquitectura.html` (DATA + CHANGELOG + VERSION).

### Fuera
- Roles por sede (`sgl-roles-por-sede`, pausado) y endpoint API: solo se deja el servicio listo.
- Columna nueva `FinalDecisionDate` (ver Enfoque; opcional si se aprueba).
- Migrar Chart.js CDN de la pantalla actual.

## Capacidades

### Nuevas
- `sede-kpi-dashboard`: KPIs comparativos por sede, filtros, calidad de datos y export Excel.

### Modificadas
- Ninguna.

## Enfoque

- **Agrupación propuesta**: Otorgado; Denegado; **En curso** = ParaDenegar, EsperaExamen, ClasePendiente, ExamenMedico/Teorico/Practico, SinSgl; **Sin decisión** = null. "Pendiente" = En curso + Sin decisión.
- **Fecha de decisión**: NO existe columna. Fuente: último `CaseAuditEntry` con `FieldName = "Decisión final"` y valor Otorgado/Denegado. Limitación: casos importados desde el libro no tienen auditoría, quedan fuera del cálculo de tiempos y se informan como "sin fecha". Alternativa (fuera de alcance): agregar `FinalDecisionDate` con migración.
- **Gráficos**: SVG/CSS renderizado en servidor, como `F8/Estadisticas` y `CambioDomicilio/Estadisticas`. Sin CDN, sin JS nuevo.
- Cálculo 100% en el servicio (TDD), página solo presenta.

## Áreas afectadas

| Área | Impacto | Descripción |
|------|---------|-------------|
| `Statistics/SedeKpiService.cs` | Nuevo | Cálculo KPI |
| `Persistence/FolderCaseRepository.cs` | Modificado | Consultas por rango + fechas de decisión desde auditoría |
| `Dashboard/Pages/Estadisticas/Comparativo.cshtml(.cs)` | Nuevo | Vista |
| `Reporting/` | Nuevo | Export KPI |
| `_Layout.cshtml`, `wwwroot/css/dashboard.css` | Modificado | Menú, estilos |
| `docs/arquitectura.html` | Modificado | DATA/CHANGELOG |

## Riesgos

| Riesgo | Prob. | Mitigación |
|--------|-------|------------|
| Tiempos sesgados por falta de auditoría | Alta | Mostrar N usado y % excluido |
| Muchos casos sin decisión | Alta | KPI de calidad visible |
| Rendimiento de rango anual | Baja | Agregación SQL, no en memoria |

## Rollback

Sin cambios de esquema. Revertir commits; eliminar página y enlace de menú.

## Tamaño estimado

~650–750 líneas (servicio+tests ~350, repo ~80, página+SVG ~200, Excel ~80, docs ~30). **>400: se propone dividir**: PR1 servicio+repo+tests; PR2 página+filtros+gráficos; PR3 Excel+docs.

## Criterios de éxito

- [ ] Las 3 sedes comparadas en una pantalla con filtros funcionando.
- [ ] Promedio y mediana muestran N y % excluidos.
- [ ] Excel coincide con la pantalla.
- [ ] Funciona sin internet.
- [ ] `dotnet test` en verde.
