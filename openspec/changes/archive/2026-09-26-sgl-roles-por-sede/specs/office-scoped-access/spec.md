# Office-Scoped Access Specification

## Purpose

Cada usuario tiene un rol global (Administrador/Jefatura/Coordinador/Administrativo) y una lista de sedes permitidas (`UserOffice`). Administrador y Jefatura ven todas las sedes sin restricción. Coordinador y Administrativo quedan limitados a sus sedes asignadas, con control aplicado en el servidor en cada punto de acceso a datos por sede.

## Requirements

### Requirement: Modelo de sedes permitidas por usuario

El sistema MUST almacenar, por usuario, la lista de sedes permitidas en la tabla `UserOffice(UserId, Office)`, manteniendo el rol global existente sin introducir rol por sede.

#### Scenario: Usuario con sedes asignadas

- GIVEN un usuario Coordinador con `UserOffice` = {Placilla}
- WHEN se consulta sus sedes permitidas
- THEN el sistema devuelve únicamente Placilla

#### Scenario: Administrador o Jefatura sin restricción

- GIVEN un usuario con rol Administrador o Jefatura
- WHEN se consulta sus sedes permitidas
- THEN el sistema trata al usuario como autorizado para las 3 sedes, sin depender de filas en `UserOffice`

### Requirement: Claims de sede emitidos al iniciar sesión

El sistema MUST emitir un claim `office:<n>` por cada sede permitida durante `LoginService`, y MUST NOT emitir restricción de sede para Administrador o Jefatura.

#### Scenario: Login de usuario limitado

- GIVEN un Administrativo con sedes {AvenidaArgentina, MercadoPuerto}
- WHEN inicia sesión
- THEN la cookie de sesión contiene los claims `office:AvenidaArgentina` y `office:MercadoPuerto`

#### Scenario: Login de Administrador

- GIVEN un usuario con rol Administrador
- WHEN inicia sesión
- THEN el sistema no emite claims `office:*` y las políticas de autorización lo tratan como acceso total

### Requirement: Aplicación inmediata de cambios de permisos

El sistema MUST invalidar la sesión activa (sello de seguridad / `OnValidatePrincipal`) cuando se modifican las sedes o el rol de un usuario, de modo que el cambio surta efecto sin esperar a que expire la cookie de 30 días.

#### Scenario: Se quita una sede a un usuario con sesión activa

- GIVEN un Coordinador con sesión activa y acceso previo a Placilla y MercadoPuerto
- WHEN un Administrador le retira MercadoPuerto
- THEN la siguiente solicitud del usuario a un recurso de MercadoPuerto es rechazada, sin necesidad de que el usuario cierre sesión manualmente

### Requirement: Filtro por sede en listado y mutaciones de casos

El sistema MUST filtrar por las sedes permitidas del usuario autenticado en: listado de casos (`Index`), alta, edición, eliminación y restauración de casos, y Papelera. El sistema MUST rechazar intentos de operar sobre casos de una sede no permitida, incluso por acceso directo a URL.

#### Scenario: Listado respeta sedes permitidas

- GIVEN un Administrativo limitado a Placilla
- WHEN solicita el listado de casos
- THEN solo recibe casos cuya sede es Placilla

#### Scenario: Acceso directo por URL a caso de otra sede

- GIVEN un Coordinador limitado a AvenidaArgentina
- WHEN accede por URL directa a un caso de MercadoPuerto (editar, eliminar o restaurar)
- THEN el sistema responde 404, tratando el caso como inexistente para ese usuario, para no filtrar por el código de respuesta si el caso existe en otra sede

#### Scenario: Papelera respeta sedes permitidas

- GIVEN un Administrativo limitado a Placilla
- WHEN abre la Papelera
- THEN solo ve casos eliminados de Placilla

### Requirement: Filtro por sede en exportación, búsqueda global, estadísticas y catálogos

El sistema MUST aplicar el filtro de sedes permitidas en exportación Excel, `/api/global-search`, `Estadisticas`/`StatisticsService`, `Sector` y `Comunas`, y en la bitácora de auditoría del caso.

#### Scenario: Exportación limitada a sedes permitidas

- GIVEN un Coordinador limitado a Placilla
- WHEN exporta el listado a Excel
- THEN el archivo contiene únicamente casos de Placilla

#### Scenario: Búsqueda global limitada

- GIVEN un Administrativo limitado a AvenidaArgentina
- WHEN busca por RUT o nombre en `/api/global-search`
- THEN los resultados excluyen casos de otras sedes

#### Scenario: Estadísticas limitadas

- GIVEN un Coordinador limitado a MercadoPuerto
- WHEN abre Estadisticas
- THEN los indicadores y gráficos reflejan solo datos de MercadoPuerto

### Requirement: Módulos no sede-scoped se mantienen sin cambios

El sistema MUST NOT aplicar filtro por sede a F8, Cambio de Domicilio, Certificados ni a la Importación de Excel; estos módulos MUST seguir controlados únicamente por rol, como hoy.

#### Scenario: F8 sin filtro de sede

- GIVEN un Administrativo limitado a Placilla
- WHEN accede al módulo F8 con permiso de módulo habilitado
- THEN el sistema no aplica ninguna restricción por sede

#### Scenario: Importación limitada por rol, no por sede

- GIVEN un Coordinador con permiso de importación
- WHEN importa un libro Excel con registros de varias sedes
- THEN el sistema permite la importación completa sin filtrar filas por sede

### Requirement: Usuario sin sedes asignadas

El sistema MUST tratar a un usuario no-Administrador/no-Jefatura sin ninguna sede asignada como sin acceso a datos por sede: listados, búsqueda, estadísticas y exportación MUST devolver conjuntos vacíos en vez de error o excepción no controlada.

#### Scenario: Coordinador sin sedes ve listado vacío

- GIVEN un Coordinador con `UserOffice` vacío
- WHEN solicita el listado de casos
- THEN el sistema devuelve una lista vacía sin error

#### Scenario: Alta de usuario exige al menos una sede

- GIVEN un Administrador creando un usuario con rol Coordinador o Administrativo
- WHEN intenta guardar sin marcar ninguna sede
- THEN el sistema MUST rechazar el guardado o SHOULD advertir explícitamente que el usuario quedará sin acceso a datos por sede

### Requirement: Auditoría de cambios de permisos

El sistema MUST registrar en `PermissionAudit` cada cambio de rol o de sedes asignadas a un usuario, incluyendo quién realizó el cambio, a quién afecta, el estado anterior, el estado nuevo y la fecha.

#### Scenario: Cambio de sedes queda auditado

- GIVEN un Administrador que modifica las sedes de un Administrativo
- WHEN guarda el cambio
- THEN se crea un registro en `PermissionAudit` con el usuario que hizo el cambio, el usuario afectado, las sedes antes y después, y la fecha

### Requirement: Migración de usuarios existentes

El sistema MUST asignar las 3 sedes a todos los usuarios existentes al desplegar esta funcionalidad, de modo que ningún usuario pierda acceso que ya tenía.

#### Scenario: Usuario existente tras la migración

- GIVEN un usuario Administrativo creado antes de esta funcionalidad
- WHEN se ejecuta la migración
- THEN el usuario queda con `UserOffice` = {AvenidaArgentina, Placilla, MercadoPuerto} y su acceso no cambia respecto al comportamiento anterior
