# Tasks: sgl-kpi-dashboard-comparativo

## 1. Servicio (TDD)
- [x] 1.1 Tests `SedeKpiServiceTests`: periodos, conteos por sede, papelera, tiempos desde auditoría, backlog, alcance, tendencia, estados
- [x] 1.2 `FolderCase.AgingFor` (regla con "hoy" inyectable; `AgingAlert` la usa)
- [x] 1.3 `Statistics/SedeKpiService.cs` y registro DI

## 2. Excel
- [x] 2.1 Test `SedeKpiExcelExporterTests`
- [x] 2.2 `Reporting/SedeKpiExcelExporter.cs`

## 3. Página
- [x] 3.1 Tests de integración: render de las 3 sedes, export, acceso denegado a Administrativo
- [x] 3.2 `Dashboard/Pages/Estadisticas/Comparativo.cshtml(.cs)` + pestaña

## 4. Docs y verificación
- [x] 4.1 `docs/arquitectura.html` v0.4.0
- [x] 4.2 `dotnet test`
