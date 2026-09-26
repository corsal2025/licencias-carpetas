# Spec: sede-kpi-dashboard

## ADDED Requirements

### Requirement: Periodo
El sistema MUST calcular los KPI sobre los casos no eliminados cuya fecha de citación cae en el periodo (mes, semestre o año; rango inclusivo).

#### Scenario: Semestre
- GIVEN periodo = 2° semestre 2026
- THEN el rango es 01-07-2026 a 31-12-2026

### Requirement: Decisiones por sede
Por cada sede del alcance y para el total, el sistema MUST informar: casos, Otorgado, Denegado, En curso (cualquier otra decisión), Sin decisión (nula) y Pendiente (= En curso + Sin decisión), más el desglose por estado de carpeta.

#### Scenario: Conteo
- GIVEN en Av. Argentina, marzo: 1 otorgado, 1 denegado, 1 espera examen, 1 sin decisión
- THEN Pendiente = 2 y % sin decisión = 25%

### Requirement: Tiempo citación → decisión
La fecha de decisión MUST tomarse del último `CaseAuditLog` con campo "Decisión final" y valor Otorgado/Denegado. El sistema MUST informar promedio, mediana, N usado y los decididos sin fecha (% sobre decididos).

#### Scenario: Caso importado sin auditoría
- GIVEN un caso otorgado sin historial
- THEN no entra en promedio/mediana y cuenta en "decididos sin fecha"

### Requirement: Backlog
Los casos no decididos (ni Otorgado ni Denegado) MUST clasificarse con la regla `FolderCase.AgingFor` (<7 verde, 7–14 amarillo, ≥15 rojo; subidos, Primera licencia y No existe carpeta fuera).

### Requirement: Tendencia
El sistema MUST entregar, por mes del periodo y sede, casos citados y otorgados.

### Requirement: Alcance y filtro de sede
El servicio MUST recibir un `OfficeScope`; el filtro de sede de la pantalla MUST estrechar el alcance, nunca ampliarlo.

### Requirement: Exportación
El sistema MUST exportar a Excel (ClosedXML) las hojas Resumen, Estados y Tendencia con los mismos números de la pantalla.

#### Impacto Excel
Archivo nuevo de solo salida; no afecta el libro importado ni el export de Casos.

### Requirement: Sin dependencias externas
Los gráficos MUST renderizarse en el servidor (HTML/CSS), sin CDN ni JavaScript nuevo.
