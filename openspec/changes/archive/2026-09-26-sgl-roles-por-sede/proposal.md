# Propuesta: Roles por sede (sgl-roles-por-sede)

## Intención
Hoy el rol es global (`DashboardUser.Role`) y cualquier cuenta ve los casos de las 3 sedes (`Office`: AvenidaArgentina, Placilla, MercadoPuerto). Se necesita que cada funcionario vea y opere SOLO sus sedes, con control en servidor (no solo ocultando elementos de la UI).

## Alcance
### Incluye
- Tabla `UserOffice(UserId, Office INT)`: lista de sedes permitidas por usuario, con el rol global de siempre (opción B).
- Claim `office:<n>` emitido en `LoginService`; `Administrador` sin restricción.
- Filtro en servidor: lista de casos (`Index`), agregar/editar/eliminar/restaurar (rechazo si la sede no está permitida), `Papelera`, `Estadisticas`/`StatisticsService`, exportación Excel, `/api/global-search`, `Sector`, `Comunas`, bitácora de auditoría del caso.
- Pantalla `Usuarios`: casillas de sedes por usuario.
- Tabla `PermissionAudit` (quién, a quién, antes/después, fecha).
- Migración: los usuarios existentes quedan con las 3 sedes (el comportamiento actual no cambia).
- `docs/arquitectura.html`: VERSION, DATA, CHANGELOG.

### Excluye
- Renombrar roles a Admin/Supervisor/Funcionario (se mantienen los nombres; como mucho, alias de visualización).
- F8, Cambio de Domicilio y Certificados: no tienen `Office` en su modelo; siguen controlados por módulo (flags actuales).
- Importar Excel: sigue limitado por rol (el libro trae varias sedes).
- Rol distinto por sede.

## Capacidades
### Nuevas
- `office-scoped-access`: sedes permitidas por usuario, control en servidor, auditoría de permisos.
### Modificadas
- None (no hay specs de auth en `openspec/specs/`; confirmar en sdd-spec).

## Enfoque
| Opción | Ventaja | Desventaja |
|---|---|---|
| A. `UserOfficeRole` (usuario×sede×rol) | Máxima flexibilidad | Los claims de rol dejan de ser únicos; hay que rehacer las políticas y `IsInRole`; más de 700 líneas |
| **B. `UserOffice` + rol global** (recomendada) | Mínimo cambio, las políticas actuales siguen iguales | No permite ser Jefatura en una sede y Administrativo en otra |

B cubre la necesidad real (restringir por sede). A se puede sumar después sin romper B. Enums guardados como INT y solo se agregan valores al final; `EnsureSchema` idempotente con SQL directo; los claims se leen al iniciar sesión (los cambios valen en el siguiente login, o se agrega un sello de seguridad).

## Áreas afectadas
| Área | Impacto |
|---|---|
| `Dashboard/Auth/*` | Modificada |
| `Persistence/FolderCaseRepository` | Filtro por sede |
| `Statistics/StatisticsService.cs`, `GlobalSearchService` | Filtro por sede |
| `Dashboard/Pages/{Index,Papelera,Estadisticas,Usuarios,Sector,Comunas}` | Modificadas |
| `docs/arquitectura.html` | VERSION/CHANGELOG |

## Estimación y entrega
Aprox. 550–700 líneas incluyendo tests: **supera 400**. Se propone dividir en PRs encadenados:
1. Esquema, migración, repositorio, claims, auditoría y pantalla de Usuarios (~250).
2. Control en casos: lista, mutaciones, Papelera y exportación (~250).
3. Estadísticas, búsqueda, Sector/Comunas y docs (~200).

## Riesgos
| Riesgo | Prob. | Mitigación |
|---|---|---|
| Filtración por un endpoint olvidado | Media | Filtro central en el repositorio + tests por handler |
| Un cookie de 30 días conserva permisos viejos | Media | Sello de seguridad / revalidar en `OnValidatePrincipal` |
| Usuario sin sedes | Baja | Validación: al menos 1 sede si no es Administrador |

## Plan de reversión
Respaldo automático al arrancar; las tablas nuevas son aditivas (`DROP TABLE UserOffice, PermissionAudit`); revertir los PRs devuelve la vista global. No se altera `DashboardUser`.

## Criterios de éxito
- [ ] Un usuario limitado a Placilla recibe 403/vacío al pedir casos de otra sede por URL o handler.
- [ ] Estadísticas, búsqueda y exportación respetan las sedes.
- [ ] Los usuarios existentes no notan cambios tras migrar.
- [ ] Cada cambio de permisos queda en `PermissionAudit`.
