# Diseño: Roles por sede (sgl-roles-por-sede)

## Enfoque técnico
Opción B de la propuesta: el rol global se mantiene (`DashboardUser.Role`, TEXT) y se agrega la tabla `UserOffice` con las sedes permitidas. En cada request se arma un `IOfficeScope` (scoped) a partir de los claims `office:<n>`. Las páginas NO usan `IFolderCaseRepository` directamente: pasan por la fachada scoped `IScopedCaseStore`, que agrega el filtro de sede a toda consulta y rechaza toda mutación por Id fuera del alcance. Los permisos se aplican al instante gracias a un `SecurityStamp` validado en `OnValidatePrincipal`.

## Decisiones de arquitectura
| Decisión | Alternativa rechazada | Motivo |
|---|---|---|
| Fachada scoped `IScopedCaseStore` sobre el repositorio singleton | Decorador scoped registrado como `IFolderCaseRepository` | El importador, el respaldo y los servicios en segundo plano (singleton) dependen del repositorio; convertirlo en scoped rompe la validación de DI. La fachada deja el repositorio intacto para los procesos del sistema |
| `CaseFilter.AllowedOffices` (`IReadOnlyCollection<Office>?`, null = todas) se traduce a `Office IN (...)` en el `BuildWhere` existente | Filtrar en memoria en cada página | Un solo punto en SQL; paginación y conteos correctos |
| Mutación por Id: `FindById` → `scope.Allows(case.Office)`; si no, `NotFound()` (404) | 403 | No revela la existencia de casos de otra sede |
| `SecurityStamp` en `DashboardUser` + `OnValidatePrincipal` que lee al usuario por Id en cada request; si el sello cambió, se **reconstruye** el principal (rol, módulos, sedes) con `ReplacePrincipal` + `ShouldRenew` | Cerrar la sesión / esperar al siguiente login | Cambio inmediato sin echar al usuario; cuesta 1 SELECT por PK en SQLite local (despreciable). Usuario borrado → `RejectPrincipal` + `SignOut` |
| `ClaimsFactory` único (usado por Login y por la revalidación) | Construir claims en `Login.cshtml.cs` como hoy | Evita que el login y la revalidación diverjan |
| Sin restricción: `Administrador` y `Jefatura` (`IOfficeScope.IsUnrestricted`) | Guardar filas en `UserOffice` para ellos | La regla vive en el rol; las filas se conservan pero se ignoran |
| `UserOffice.Office` INT ordinal; `Office` solo agrega valores al final | TEXT | Igual que `FolderCase.Office` (INTEGER) |
| Estadísticas: `StatisticsService` recibe `IReadOnlyCollection<Office>?`; los métodos del repositorio (`DailyAttendance`, `*Breakdown`) aceptan la lista | Un servicio por sede | Mínimo cambio; `Office?` actual pasa a intersectarse con el alcance |
| `GlobalSearchService.Search(query, allowedOffices, limit)`: la rama de casos filtra por sede; F8/CambioDomicilio/Certificados sin filtro (decisión del usuario) | Ocultar la búsqueda | Cumple el alcance acordado |
| Guardarraíl de arquitectura: test por reflexión que falla si un `PageModel` inyecta `IFolderCaseRepository` (lista blanca: `Importar`) | Revisión manual | Previene la filtración por "un endpoint olvidado" |

## Interfaces
```csharp
public interface IOfficeScope {
    bool IsUnrestricted { get; }
    IReadOnlyCollection<Office> Offices { get; }   // vacío solo si IsUnrestricted
    bool Allows(Office office);
    IReadOnlyCollection<Office>? AsFilter();       // null = sin restricción
}
public interface IScopedCaseStore {                // scoped; envuelve IFolderCaseRepository
    IReadOnlyList<FolderCase> Query(CaseFilter f, int skip, int take); // + QueryAll, Count, DuplicateRuts, ForSector, Papelera...
    FolderCase? FindAllowed(long id);              // null si no existe o fuera de alcance
    bool TryMutate(long id, Action<IFolderCaseRepository> action);
}
public interface IUserOfficeRepository {
    void EnsureSchema();                           // incluye migración de usuarios existentes
    IReadOnlyList<Office> For(long userId);
    void Replace(long userId, IReadOnlyCollection<Office> offices, string changedBy); // tx + PermissionAudit + nuevo SecurityStamp
}
```

## SQL (EnsureSchema idempotente)
```sql
CREATE TABLE IF NOT EXISTS UserOffice (
  UserId INTEGER NOT NULL REFERENCES DashboardUser(Id) ON DELETE CASCADE,
  Office INTEGER NOT NULL, PRIMARY KEY (UserId, Office));
CREATE TABLE IF NOT EXISTS PermissionAudit (
  Id INTEGER PRIMARY KEY AUTOINCREMENT, ChangedAt TEXT NOT NULL,
  ChangedBy TEXT NOT NULL, TargetUserId INTEGER NOT NULL, TargetUsername TEXT NOT NULL,
  Field TEXT NOT NULL, Before TEXT NULL, After TEXT NULL);
-- AddColumnIfMissing(DashboardUser, "SecurityStamp", "TEXT NULL"); luego UPDATE ... WHERE SecurityStamp IS NULL
-- Migración: solo usuarios que aún no tienen ninguna fila
INSERT OR IGNORE INTO UserOffice (UserId, Office)
SELECT u.Id, o.v FROM DashboardUser u CROSS JOIN (SELECT 0 v UNION ALL SELECT 1 UNION ALL SELECT 2) o
WHERE NOT EXISTS (SELECT 1 FROM UserOffice x WHERE x.UserId = u.Id);
```
Nota: el `NOT EXISTS` evita que un reinicio devuelva sedes quitadas a propósito. El borrado de usuario debe eliminar filas explícitamente (SQLite no aplica FK sin `PRAGMA foreign_keys=ON`).

## Flujo de datos
```
Request ─→ Cookie ─→ OnValidatePrincipal ─(stamp distinto)→ UserRepository + UserOfficeRepository
                          │                                  └→ ClaimsFactory → ReplacePrincipal
                          ▼
                  IOfficeScope (claims) ─→ IScopedCaseStore ─→ CaseFilter.AllowedOffices ─→ SQLite
PageModel ───────────────────────────────┘        └─ Id fuera de alcance → 404
```
Secuencia de exportación Excel: `Index.OnGetExport` → `IScopedCaseStore.QueryAll(filtro+alcance)` → `SELECT ... WHERE Office IN (...)` → ClosedXML → archivo. El libro exportado solo contiene sedes permitidas; el formato no cambia.

Secuencia de cambio de permisos: `Usuarios.OnPost` (Administrador) → `IUserOfficeRepository.Replace` (tx: DELETE+INSERT `UserOffice`, INSERT `PermissionAudit`, UPDATE `SecurityStamp`) → siguiente request del afectado → principal reconstruido.

## Cambios por archivo y slices (stacked-to-main, commits directos)
| Slice | Archivos | Rollback |
|---|---|---|
| 1. Esquema + sesión + auditoría + Usuarios | Crear `Dashboard/Auth/{UserOfficeRepository,OfficeScope,ClaimsFactory,PermissionAudit}.cs`; modificar `UserRepository.cs` (SecurityStamp, borrado de filas), `Login.cshtml.cs`, `Program.cs` (DI, `OnValidatePrincipal`, `EnsureSchemas`), `Usuarios.cshtml(.cs)` (casillas; validar ≥1 sede si Coordinador/Administrativo) | Revertir commits; tablas aditivas quedan inertes (o `DROP TABLE UserOffice, PermissionAudit`). Sin efecto visible: nadie filtra todavía |
| 2. Control de casos | `CaseFilter.cs` (AllowedOffices), `FolderCaseRepository.cs` (`BuildWhere`, Papelera), crear `Persistence/ScopedCaseStore.cs`; `Index`, `Papelera`, `Sector`, `Comunas`, bitácora de auditoría del caso, exportación; test guardarraíl | Revertir slice 2 → vista global otra vez; slice 1 sigue válido |
| 3. Estadísticas, búsqueda y docs | `StatisticsService.cs`, métodos de agregación del repositorio, `Estadisticas`, `GlobalSearchService.cs` + endpoint `/api/global-search`; `docs/arquitectura.html` (VERSION, DATA: UserOffice/PermissionAudit/SecurityStamp, CHANGELOG) | Revertir slice 3; las estadísticas vuelven a ser globales |

Rutas de Razor Pages: no cambian; solo cambia la respuesta (404 o listas filtradas). `Importar` sigue restringido por rol.

## Estrategia de pruebas (TDD estricto, xunit + `SqliteTestDatabase`)
| Capa | Qué | Cómo |
|---|---|---|
| Unidad | `OfficeScope` por rol; `ClaimsFactory`; validación ≥1 sede | Principals construidos en el test |
| Repositorio | `AllowedOffices` en Query/Count/Duplicados/ForSector/Papelera/agregados; migración idempotente (2 ejecuciones, sede quitada no vuelve); auditoría + stamp en `Replace` | SQLite temporal |
| Handler | Cada handler de Index/Papelera/Sector/Comunas/Estadisticas/Export/búsqueda: usuario Placilla no ve ni muta AvenidaArgentina (404) | PageModel con scope falso |
| Integración | Cambio de sedes con sesión abierta se aplica en la siguiente request | `WebApplicationFactory` existente |
| Arquitectura | Ningún PageModel inyecta `IFolderCaseRepository` (salvo lista blanca) | Reflexión |

## Migración / despliegue
Respaldo automático al iniciar (ya existe) → `EnsureSchema` crea tablas, agrega `SecurityStamp` y asigna las 3 sedes a quien no tenga filas. Los usuarios existentes no notan cambios.

## Preguntas abiertas
- [ ] ¿Jefatura puede editar sedes de otros o solo el Administrador? (se asume solo Administrador, como la gestión de roles hoy).
- [ ] ¿La bitácora de auditoría del caso se lee vía `FolderCaseRepository` o un repositorio propio? Confirmar en tasks; en cualquier caso pasa por `FindAllowed`.
