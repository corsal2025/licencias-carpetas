# Tareas: Roles por sede (sgl-roles-por-sede)

Estrategia de entrega: `ask-on-risk` → el usuario eligió PRs encadenados.
Estrategia de encadenamiento: `stacked-to-main` — cada slice se commitea directamente a `main`, en orden; no se abren Pull Requests. Cada slice debe quedar verde (build + tests) antes de pasar al siguiente.

Decisión vinculante de esta fase (cierra la pregunta abierta del diseño):
**Solo el rol Administrador puede asignar o editar las sedes de un usuario.** Se aplica en servidor —el handler de `Usuarios.OnPost` MUST verificar `User.IsInRole("Administrador")` antes de llamar a `IUserOfficeRepository.Replace`, devolviendo 403 si no— y queda auditado en `PermissionAudit` igual que cualquier otro cambio de permisos. Jefatura conserva su alcance sin restricción de sede para *ver* datos, pero no puede *modificar* sedes de terceros.

Convención de numeración: `Slice.Grupo.Tarea`. Cada tarea de código sigue TDD estricto: primero el test (rojo), luego la implementación mínima (verde), luego refactor si aplica. `dotnet test` referenciado en cada tarea de test es el mismo `test_command` de `openspec/config.yaml`.

---

## Slice 1 — Esquema + sesión + auditoría + pantalla Usuarios

Requisitos de spec cubiertos: "Modelo de sedes permitidas por usuario", "Claims de sede emitidos al iniciar sesión", "Aplicación inmediata de cambios de permisos", "Usuario sin sedes asignadas" (validación de alta), "Auditoría de cambios de permisos", "Migración de usuarios existentes".

### 1.1 Esquema y migración (secuencial — base de lo demás)
- [x] 1.1.1 (TDD) Test: `UserOfficeRepository.EnsureSchema()` crea `UserOffice` y `PermissionAudit` de forma idempotente (2 ejecuciones no fallan, no duplica filas) — `SqliteTestDatabase`
- [x] 1.1.2 (TDD) Test: migración asigna las 3 sedes a un usuario existente sin filas en `UserOffice`, y NO revive una sede quitada a propósito (usuario con 1 fila existente conserva solo esa)
- [x] 1.1.3 (TDD) Test: `EnsureSchema` agrega columna `SecurityStamp TEXT NULL` a `DashboardUser` si falta, y la rellena para filas existentes
- [x] 1.1.4 Implementar `Dashboard/Auth/UserOfficeRepository.cs` (`EnsureSchema`, `For(userId)`, `Replace(userId, offices, changedBy)` con transacción DELETE+INSERT `UserOffice` + INSERT `PermissionAudit` + UPDATE `SecurityStamp`) hasta que 1.1.1–1.1.3 pasen
- [x] 1.1.5 Marcar tarea de migración de esquema: ejecutar contra copia de la base de producción en entorno local antes de commitear (ver nota de respaldo abajo)

### 1.2 Alcance de sedes (`IOfficeScope`) y claims (secuencial, depende de 1.1)
- [x] 1.2.1 (TDD) Test: `OfficeScope` para Administrador/Jefatura → `IsUnrestricted = true`, `AsFilter() == null`
- [x] 1.2.2 (TDD) Test: `OfficeScope` para Coordinador/Administrativo con 2 sedes → `Allows` solo esas 2, `AsFilter()` las devuelve
- [x] 1.2.3 (TDD) Test: `OfficeScope` con 0 sedes (no Administrador/Jefatura) → `Allows` siempre false, `AsFilter()` devuelve colección vacía (no null)
- [x] 1.2.4 Implementar `Dashboard/Auth/OfficeScope.cs` (`IOfficeScope`) hasta que 1.2.1–1.2.3 pasen
- [x] 1.2.5 (TDD) Test: `ClaimsFactory` emite `office:<n>` por cada sede para un Administrativo limitado; no emite `office:*` para Administrador
- [x] 1.2.6 Implementar `Dashboard/Auth/ClaimsFactory.cs`; usarlo desde `Login.cshtml.cs` (reemplaza la construcción de claims actual)

### 1.3 Revalidación de sesión (secuencial, depende de 1.2)
- [x] 1.3.1 (TDD) Test de integración (`WebApplicationFactory`): con sesión abierta, si `Replace` cambia el `SecurityStamp`, la siguiente request reconstruye el principal con las nuevas sedes (sin requerir logout)
- [x] 1.3.2 (TDD) Test: si el usuario fue borrado, `OnValidatePrincipal` rechaza el principal y fuerza `SignOut`
- [x] 1.3.3 Implementar `OnValidatePrincipal` en `Program.cs` (lee `DashboardUser` + `UserOfficeRepository` por Id, compara `SecurityStamp`, `ReplacePrincipal`/`ShouldRenew` vía `ClaimsFactory`) hasta que 1.3.1–1.3.2 pasen
- [x] 1.3.4 Registrar DI: `IUserOfficeRepository`, `IOfficeScope` (scoped, por request), llamar `EnsureSchema()` junto a los `EnsureSchema` existentes en `Program.cs`

### 1.4 Pantalla Usuarios (secuencial, depende de 1.1–1.3)
- [x] 1.4.1 (TDD) Test: `Usuarios.OnPost` con actor sin rol Administrador → 403, `Replace` NO se invoca
- [x] 1.4.2 (TDD) Test: `Usuarios.OnPost` con rol Coordinador/Administrativo sin ninguna sede marcada → rechaza el guardado con mensaje de validación (según spec: MUST rechazar o SHOULD advertir; se implementa como rechazo explícito, más estricto y menos ambiguo)
- [x] 1.4.3 (TDD) Test: `Usuarios.OnPost` válido (actor Administrador, ≥1 sede) → llama `Replace` y el resultado queda reflejado en `UserOffice` y `PermissionAudit`
- [x] 1.4.4 Implementar casillas de sedes en `Usuarios.cshtml` + guard de rol y validación en `Usuarios.cshtml.cs` hasta que 1.4.1–1.4.3 pasen

### Verificación de slice 1
- [x] `dotnet build`
- [x] `dotnet test` (unidad: 1.1.1–1.1.3, 1.2.1–1.2.3, 1.2.5; integración: 1.3.1–1.3.2; handler: 1.4.1–1.4.3) — todo en verde
- [x] Confirmar manualmente que un usuario existente no pierde acceso tras la migración (login con cuenta de prueba)

### Rollback slice 1
Revertir los commits del slice (stacked-to-main: `git revert` de cada commit en orden inverso, o reset si aún no hay slices posteriores encima). Las tablas `UserOffice` y `PermissionAudit` son aditivas y quedan inertes si se revierte el código; opcionalmente `DROP TABLE UserOffice, PermissionAudit`. Sin efecto visible para el resto del sistema porque nadie filtra todavía por sede.

### Review Workload Forecast — Slice 1
- Archivos tocados: ~7 (`UserOfficeRepository.cs`, `OfficeScope.cs`, `ClaimsFactory.cs`, `PermissionAudit` tipo, `UserRepository.cs`, `Login.cshtml.cs`, `Program.cs`, `Usuarios.cshtml(.cs)`) + tests nuevos
- Líneas estimadas: ~250 (incluye tests) — dentro del presupuesto de 400 líneas
- Riesgo de PR de 400 líneas: Bajo (no aplica de todos modos: stacked-to-main sin PR, cada commit revisable de forma individual)
- Decisión requerida antes de aplicar: No — la propuesta ya definió el corte en 3 slices y el usuario ya eligió stacked-to-main

---

## Slice 2 — Control de sede en casos (listado, mutaciones, Papelera, exportación)

Requisitos de spec cubiertos: "Filtro por sede en listado y mutaciones de casos" (incluye 404 en acceso directo por URL), "Filtro por sede en exportación" (Excel), "Usuario sin sedes asignadas" (listados vacíos), guardarraíl de arquitectura.

Depende de slice 1 (necesita `IOfficeScope` ya emitido en claims).

### 2.1 `CaseFilter` y filtro SQL (secuencial — base de la fachada)
- [x] 2.1.1 (TDD) Test: `FolderCaseRepository` con `CaseFilter.AllowedOffices = [Placilla]` devuelve solo casos de Placilla en `Query`, `Count`, `DuplicateRuts`, `ForSector`, Papelera
- [x] 2.1.2 (TDD) Test: `CaseFilter.AllowedOffices = null` (sin restricción) devuelve todas las sedes, igual que el comportamiento actual
- [x] 2.1.3 (TDD) Test: `CaseFilter.AllowedOffices = []` (colección vacía, usuario sin sedes) devuelve 0 filas sin excepción
- [x] 2.1.4 Agregar `AllowedOffices` a `CaseFilter.cs` y extender `BuildWhere` en `FolderCaseRepository.cs` (`Office IN (...)`) hasta que 2.1.1–2.1.3 pasen

### 2.2 Fachada `IScopedCaseStore` (secuencial, depende de 2.1)
- [x] 2.2.1 (TDD) Test: `ScopedCaseStore.Query`/`QueryAll`/`Count` aplican `scope.AsFilter()` automáticamente sin que el caller pase `AllowedOffices` explícito
- [x] 2.2.2 (TDD) Test: `FindAllowed(id)` devuelve `null` cuando el caso existe pero su `Office` no está en el alcance (simula el 404)
- [x] 2.2.3 (TDD) Test: `FindAllowed(id)` devuelve el caso cuando está dentro del alcance
- [x] 2.2.4 (TDD) Test: `TryMutate(id, action)` no ejecuta `action` y devuelve `false` si el caso está fuera de alcance; lo ejecuta y devuelve `true` si está dentro
- [x] 2.2.5 Implementar `Persistence/ScopedCaseStore.cs` (scoped en DI, envuelve `IFolderCaseRepository`) hasta que 2.2.1–2.2.4 pasen

### 2.3 Handlers de páginas (pueden avanzar en paralelo entre sí una vez completo 2.2; cada uno es independiente)
- [x] 2.3.1 (TDD) Test handler `Index`: usuario limitado a Placilla no ve casos de otra sede en el listado ni en la paginación/conteo
- [x] 2.3.2 Migrar `Index.cshtml.cs` de `IFolderCaseRepository` a `IScopedCaseStore` hasta que 2.3.1 pase
- [x] 2.3.3 (TDD) Test handler: editar/eliminar/restaurar un caso de sede no permitida por URL directa → 404
- [x] 2.3.4 Migrar handlers de edición/eliminación/restauración (usan `FindAllowed`/`TryMutate`) hasta que 2.3.3 pase
- [x] 2.3.5 (TDD) Test handler `Papelera`: usuario limitado a Placilla solo ve eliminados de Placilla
- [x] 2.3.6 Migrar `Papelera.cshtml.cs` a `IScopedCaseStore` hasta que 2.3.5 pase
- [x] 2.3.7 (TDD) Test handler `Sector`: filtra por sede permitida
- [x] 2.3.8 Migrar `Sector.cshtml.cs` hasta que 2.3.7 pase
- [x] 2.3.9 (TDD) Test handler `Comunas`: filtra por sede permitida
- [x] 2.3.10 Migrar `Comunas.cshtml.cs` hasta que 2.3.9 pase
- [x] 2.3.11 (TDD) Test: bitácora de auditoría de un caso solo es visible si el caso está dentro del alcance del usuario (pasa por `FindAllowed`, según nota del diseño)
- [x] 2.3.12 Migrar el handler de bitácora del caso hasta que 2.3.11 pase

### 2.4 Exportación Excel (secuencial, depende de 2.2 y 2.3.2)
- [x] 2.4.1 (TDD) Test: `Index.OnGetExport` con usuario limitado a Placilla genera un libro que contiene únicamente casos de Placilla
- [x] 2.4.2 Migrar `OnGetExport` a `IScopedCaseStore.QueryAll(filtro+alcance)` hasta que 2.4.1 pase; confirmar que el formato del libro no cambia (ClosedXML)

### 2.5 Guardarraíl de arquitectura (secuencial — cierre del slice)
- [x] 2.5.1 (TDD) Test por reflexión: falla si algún `PageModel` inyecta `IFolderCaseRepository` directamente, salvo la lista blanca (`Importar`)
- [x] 2.5.2 Ejecutar el test contra el estado final del slice y corregir cualquier `PageModel` que aún inyecte el repositorio sin pasar por la fachada

### Verificación de slice 2
- [x] `dotnet build`
- [x] `dotnet test` (2.1.x, 2.2.x, 2.3.x, 2.4.1, 2.5.1) — todo en verde
- [x] Confirmar manualmente: usuario Placilla no ve ni puede mutar casos de otra sede vía UI y vía URL directa

### Rollback slice 2
Revertir los commits del slice 2 (orden inverso, `git revert`). La vista vuelve a ser global (sin filtro por sede) para listado, mutaciones, Papelera y exportación. El slice 1 sigue válido e intacto: esquema, claims y pantalla de Usuarios no se ven afectados.

### Review Workload Forecast — Slice 2
- Archivos tocados: ~9 (`CaseFilter.cs`, `FolderCaseRepository.cs`, `ScopedCaseStore.cs` nuevo, `Index.cshtml.cs`, `Papelera.cshtml.cs`, `Sector.cshtml.cs`, `Comunas.cshtml.cs`, handler de bitácora, test de reflexión) + tests
- Líneas estimadas: ~250 (incluye tests) — dentro del presupuesto de 400 líneas
- Riesgo de PR de 400 líneas: Bajo (sin PR; stacked-to-main). Riesgo real es de alcance: es el slice con más superficie de pruebas por handler
- Decisión requerida antes de aplicar: No — mismo corte ya acordado en la propuesta

---

## Slice 3 — Estadísticas, búsqueda global, catálogos y documentación

Requisitos de spec cubiertos: "Filtro por sede en exportación, búsqueda global, estadísticas y catálogos" (estadísticas y búsqueda; Sector/Comunas ya cubiertos en slice 2), "Módulos no sede-scoped se mantienen sin cambios" (verificación negativa de F8/CambioDomicilio/Certificados/Importar).

Depende de slice 1 y slice 2 (usa `IOfficeScope` y el patrón de filtro ya validado).

### 3.1 Estadísticas (secuencial)
- [x] 3.1.1 (TDD) Test: `StatisticsService` con `IReadOnlyCollection<Office>?` limitado a MercadoPuerto devuelve indicadores/gráficos solo de esa sede
- [x] 3.1.2 (TDD) Test: métodos de agregación del repositorio (`DailyAttendance`, `*Breakdown`) intersectan el `Office?` actual con el alcance del usuario, sin duplicar parámetros
- [x] 3.1.3 Implementar cambios en `StatisticsService.cs` y métodos de agregación del repositorio hasta que 3.1.1–3.1.2 pasen
- [x] 3.1.4 (TDD) Test handler `Estadisticas`: usuario limitado a una sede solo ve esa sede en la página
- [x] 3.1.5 Migrar `Estadisticas.cshtml.cs` para pasar el alcance del usuario a `StatisticsService` hasta que 3.1.4 pase

### 3.2 Búsqueda global (secuencial, puede correr en paralelo con 3.1 — no comparte archivos)
- [x] 3.2.1 (TDD) Test: `GlobalSearchService.Search(query, allowedOffices, limit)` con alcance limitado excluye casos de otras sedes en la rama de casos
- [x] 3.2.2 (TDD) Test: la rama de casos con `allowedOffices = []` (sin sedes) devuelve 0 resultados de casos sin error
- [x] 3.2.3 (TDD) Test: las ramas F8/CambioDomicilio/Certificados de `GlobalSearchService` NO aplican filtro de sede (regresión explícita del alcance excluido)
- [x] 3.2.4 Implementar el parámetro `allowedOffices` en `GlobalSearchService.cs` y propagarlo desde el endpoint `/api/global-search` hasta que 3.2.1–3.2.3 pasen

### 3.3 Verificación negativa de módulos no sede-scoped (secuencial, cierre funcional)
- [x] 3.3.1 (TDD) Test: acceso a F8 con permiso de módulo habilitado no aplica ninguna restricción de sede, para un usuario limitado a 1 sede
- [x] 3.3.2 (TDD) Test: importación de Excel con registros de varias sedes se permite completa (sin filtrar filas) para un usuario con permiso de importación
- [x] 3.3.3 Confirmar (sin cambios de código esperados) que 3.3.1–3.3.2 ya pasan con el estado actual; si fallan, es señal de fuga de alcance y se corrige antes de cerrar el slice

### 3.4 Documentación (secuencial, depende de 3.1–3.3 completos)
- [x] 3.4.1 Actualizar `docs/arquitectura.html`: VERSION, sección DATA (`UserOffice`, `PermissionAudit`, `SecurityStamp`), CHANGELOG con el resumen de los 3 slices y la decisión "solo Administrador asigna sedes"

### Verificación de slice 3
- [x] `dotnet build`
- [x] `dotnet test` (3.1.x, 3.2.x, 3.3.x, suite completa del proyecto para detectar regresiones de los slices previos) — todo en verde

### Rollback slice 3
Revertir los commits del slice 3 (orden inverso, `git revert`). Estadísticas y búsqueda vuelven a ser globales; `docs/arquitectura.html` revierte a la versión previa. Slices 1 y 2 siguen válidos e intactos.

### Review Workload Forecast — Slice 3
- Archivos tocados: ~6 (`StatisticsService.cs`, métodos de agregación del repositorio, `Estadisticas.cshtml.cs`, `GlobalSearchService.cs`, endpoint `/api/global-search`, `docs/arquitectura.html`) + tests
- Líneas estimadas: ~200 (incluye tests) — dentro del presupuesto de 400 líneas
- Riesgo de PR de 400 líneas: Bajo (sin PR; stacked-to-main)
- Decisión requerida antes de aplicar: No

---

## Notas transversales para sdd-apply

- Respaldo automático de la base de datos ya existe al iniciar la aplicación; no se requiere tarea adicional de backup, pero cada slice con cambios de esquema (solo slice 1) MUST verificarse contra una copia de la base real antes de commitear a `main`.
- El corte de slices y su orden replica exactamente el de `design.md` (`## Cambios por archivo y slices`); no reordenar ni fusionar slices sin volver a esta fase.
- Toda tarea marcada "(TDD)" MUST escribirse y ejecutarse en rojo antes de tocar el código de producción correspondiente (Strict TDD Mode activo en `openspec/config.yaml`).
- El guardarraíl de reflexión (2.5.1) debe re-ejecutarse también al final del slice 3 para confirmar que ningún handler nuevo introdujo una fuga.

---

## Registro de aplicación (2026-09-26)

Los tres slices se aplicaron en la rama `claude/sharp-faraday-hbhnvn` (no directo a `main`); `dotnet test`: 601/601.

### Desviaciones respecto de `design.md` (y por qué)
| Diseño | Implementado | Motivo |
|---|---|---|
| Fachada `IScopedCaseStore` con API propia (`FindAllowed`, `TryMutate`) | `IScopedCaseRepository : IFolderCaseRepository`, decorador por request (`Persistence/ScopedCaseRepository.cs`) | Las páginas solo cambian el tipo inyectado; cada lectura aplica `AllowedOffices` y cada mutación por Id pasa por un único guard. El singleton sin filtro sigue para importador y servicios |
| 404 devuelto por cada handler | `CaseOutOfScopeException` → middleware → 404 | Un solo punto; ningún handler puede olvidarlo |
| `IOfficeScope` (interfaz, `Dashboard/Auth`) | `OfficeScope` (record, `Domain/`) registrado scoped, leído de los claims por `OfficeScopeClaims` | Es el mismo tipo que usan la ficha de persona y el comparativo KPI |
| `OnValidatePrincipal` inline | `SessionRevalidator` (testeable) llamado desde `OnValidatePrincipal` | Pruebas unitarias sin cookie real |
| Migración con `NOT EXISTS` en cada arranque | Asignación de las 3 sedes **solo cuando se crea la tabla** `UserOffice` | Un usuario al que se le quitaron sedes nunca las recupera por reiniciar |
| Filtro de sede en `Comunas` | Sin cambios | `ComunaContact` es un directorio nacional sin sede |
| Contadores manuales escaneadas/subidas por sede | Siguen siendo del departamento completo | La tabla `DailyCounter` no tiene sede |

### Verificación manual (1.1.5 y confirmaciones de slice)
Contra una copia de `seed/carpetas.db` (110 casos, 2 usuarios), con la app en Release y Playwright:
- Arranque: respaldo previo generado; `UserOffice` creado con las 3 sedes para `raul` y `admin`; `SecurityStamp` asignado a ambos.
- Usuario Administrativo limitado a Placilla: ve 0 casos (todos son de Av. Argentina); la bitácora del caso 111 responde **404**.
- El administrador le cambia la sede a Av. Argentina: en la **misma sesión**, sin volver a entrar, ve 100 casos (primera página) y la bitácora del caso 111 responde **200**.
- `PermissionAudit` registra cada cambio; la pantalla Usuarios lo muestra.
