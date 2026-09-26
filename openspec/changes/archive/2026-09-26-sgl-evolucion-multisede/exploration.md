# Exploración: sgl-evolucion-multisede

Objetivo: reemplazo total del Excel "DETALLE CARPETAS DEPTO. LICENCIAS DE CONDUCIR 2026.xlsx".

## Estado actual
- .NET 10, Razor Pages, SQLite (Microsoft.Data.Sqlite, SQL crudo con repositorios), ClosedXML, xunit + coverlet (~538 tests).
- Módulos: Gestión de Licencias (`Domain/`, `Persistence/`), F8 Urgentes (`F8/`), Cambio de Domicilio (`CambioDomicilio/`, lector EWS contra Exchange on-prem).
- El diagrama `sistema-gestion-licencias/docs/arquitectura.html` asume Blazor/Clean Architecture: desactualizado, adaptar al stack real.

## Cobertura

| Requerimiento | Estado | Evidencia |
|---|---|---|
| Multi-sede (3 sedes) | Cubierto | `Domain/Office.cs` + `TryResolve` |
| Roles Admin/Supervisor/Funcionario | Parcial | `Dashboard/Auth/UserRole.cs`: Administrador, Jefatura, Coordinador, Administrativo |
| Roles por sede | Falta | Rol global por cuenta en `UserRepository`/`DashboardUser`; sin tabla Usuario×Sede×Rol |
| Auditoría | Cubierto | `Domain/CaseAuditEntry.cs` + `FolderCaseRepository.RecordAuditLog` |
| KPI por sede y tiempos | Parcial | `Statistics/StatisticsService.cs` (breakdowns, `ByOffice`, `AgingAlert`); falta tiempo promedio citación→decisión comparativo |
| Import Excel normalizado | Cubierto | `Import/ExcelWorkbookImporter.cs`, `WorkbookSanitizer`, alias en catálogos |
| Historial por persona | Parcial | `GlobalSearchService.cs` busca por RUT; falta vista ficha |
| Local → servidor municipal | Sin resolver | SQL específico de SQLite (`PRAGMA`, `AUTOINCREMENT`) |
| Branding "Creado por Raúl Salazar" | Falta | Sin coincidencias en `src/` |
| Diagrama vivo | Falta actualización | Describe stack distinto |

## Riesgos
1. Enums como INTEGER ordinal (`Office`, `FolderState`, `FinalDecision`, `MoralIdoneity`): valores nuevos SIEMPRE al final.
2. PII (RUT, nombre, email, celular) en texto plano en SQLite y backups (`Persistence/DatabaseBackup.cs`).
3. Dependencia EWS/Exchange on-prem.
4. Migrar de SQLite a motor servidor implica reescribir persistencia.
5. Roles por sede requieren tabla nueva y convivir con flags `CanAccessCambioDomicilio`/`CanAccessF8Urgentes`.
6. Re-importación: índice `IX_FolderCase_Natural` (Office+CitationDate+Rut) sugiere upsert; comportamiento de conflicto no confirmado.

## Opciones
1. **Extensión incremental (recomendada)**: tabla `UserOfficeRole`, vista KPI comparativa, ficha por RUT, branding en `_Layout.cshtml`, diagrama actualizado. Reutiliza ~80%. Esfuerzo medio.
2. Rediseño RBAC por claims: flexible, alto riesgo de romper `[Authorize]` existentes. Esfuerzo alto.
3. Excel paralelo + dashboard solo lectura: no cumple objetivo. Descartada.

## Cambios recomendados (cada uno ≤ 400 líneas)
- `sgl-branding-y-diagrama`
- `sgl-roles-por-sede`
- `sgl-kpi-dashboard-comparativo`
- `sgl-ficha-persona`
- (futuro) `sgl-servidor-municipal`
