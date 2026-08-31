# Proposal: integrar Cambio de Domicilio en licencias-carpetas

## Intent

Hoy "Cambio de Domicilio" es una app aparte (`outlook-comuna-router`) con su propio login, su propia
`router.db` y su propio puerto; el nav de esta app la abre como link externo a `https://localhost:5001`.
Eso obliga a operar dos servidores, dos bases y dos juegos de usuarios para un mismo trabajo diario.
Se pliega el módulo dentro de licencias-carpetas — un proceso, un login, una `carpetas.db` — con el
mismo patrón de fold-in que ya se usó para F8. El scaffolding ya existe: política `CambioDomicilioAccess`,
claim `mod:cambio-domicilio`, `UserProvisioning.Create(canAccessCambioDomicilio)`, `DashboardUser.CanAccessCambioDomicilio`.

## Scope

### In Scope
- Portar `Domain, Persistence, Ews, Mail, Extraction, Routing, Directories, Notifications, Reporting, Statistics` a `src/LicenciasCarpetas/CambioDomicilio/`.
- Nuevas páginas `Dashboard/Pages/CambioDomicilio/{Index,Discarded,Comunas,Estadisticas,Certificado,Sector}.cshtml`, gateadas por la política `CambioDomicilioAccess` ya existente.
- "Sincronizar ahora": handler POST que llama `RunCycleAsync` igual que hoy (`IndexModel.OnPostSyncNowAsync`), conservando el `SemaphoreSlim` anti-solapamiento.
- Tablas nuevas (`PersonRequest`, `DiscardedEmail`) en la `carpetas.db` existente vía el patrón `EnsureSchema` de `Program.cs`.
- Reemplazar el link externo del nav (`_Layout.cshtml:78`) por `asp-page="/CambioDomicilio/Index"`.

### Out of Scope
- Cambiar la lógica EWS/buzón: se porta tal cual, sin refactor.
- Fusionar `ComunaContact` (lista de notificación editable) con `Directories` (CSV de matching de ruteo): quedan separados.
- Migrar el login/usuarios de la app hermana ni dar de baja `outlook-comuna-router` — eso es un cambio futuro, tras verificar este en producción.
- Reponer un worker de fondo automático: hoy no existe, no se inventa.

## Capabilities

### New Capabilities
- `cambio-domicilio-routing`: ciclo de sincronización EWS, extracción, ruteo y confirmación de casos.
- `cambio-domicilio-dashboard`: pantallas de casos, descartados, estadísticas y certificado bajo `CambioDomicilioAccess`.

### Modified Capabilities
- None (no hay `openspec/specs/` previo en este repo).

## Approach

Una sola SQLite (`carpetas.db`) con tablas propias del módulo, registradas en `EnsureSchemas`.
Config reconciliada bajo sección `CambioDomicilio:` (hoy `Router:`), siguiendo la convención
`Carpetas:` / `F8:`; credenciales EWS en User Secrets/variables de entorno, nunca en `appsettings.json`.
El sync sigue siendo on-demand desde una request autenticada, sin `AddHostedService` nuevo.

## Affected Areas

| Área | Impacto | Descripción |
|------|---------|-------------|
| `src/LicenciasCarpetas/CambioDomicilio/**` | New | Código portado |
| `src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/**` | New | Pantallas |
| `src/LicenciasCarpetas/Program.cs` | Modified | DI, opciones, `EnsureSchemas` |
| `src/LicenciasCarpetas/Dashboard/Pages/Shared/_Layout.cshtml` | Modified | Link interno |
| `appsettings.json` | Modified | Sección `CambioDomicilio:` |

## Decisiones abiertas para sdd-design

1. **¿"Sincronizar ahora" bloquea el request?** Recomendación por defecto: **mantenerlo síncrono**
   (paridad exacta con hoy, cero riesgo de regresión, feedback directo). Si el ciclo supera el
   timeout de Kestrel/proxy, se evalúa en design un disparo en background con poll de estado.
2. **`ComunaContact` vs `Directories`.** Recomendación: **repositorios y nombres distintos** —
   `ComunaContact` (tabla, notificación) intacto; el directorio de ruteo se porta como
   `ComunaRoutingEntry` (CSV, sin tocar el formato). Cero fusión.

## Risks

| Riesgo | Prob. | Mitigación |
|--------|-------|------------|
| Exposición de credenciales EWS en proceso compartido | Med | User Secrets/env vars; sin defaults en repo; documentar rotación |
| Choque de claves de config `Router:*` vs `CambioDomicilio:*` | Med | Renombrado explícito + fallo temprano si falta la sección |
| Colisión de nombres de tabla en `carpetas.db` | Baja | Prefijar tablas del módulo; verificar antes de `EnsureSchema` |

## Rollback Plan

Revertir el commit del fold-in y restaurar el link externo en `_Layout.cshtml`. La app hermana sigue
viva y con su `router.db` intacta (no se migra ni se borra), así que el rollback es inmediato.
Las tablas nuevas en `carpetas.db` quedan huérfanas pero inertes; hay respaldo previo por `DatabaseBackup`.

## Dependencies

- Acceso a credenciales EWS y buzón Exchange on-prem de la cuenta de servicio.
- CSV de directorio de comunas accesible desde la máquina que corre licencias-carpetas.

## Success Criteria

- [x] Un operador con `mod:cambio-domicilio` abre el módulo desde el nav sin segundo login. *(nav interno `asp-page`, `CambioDomicilioAccessTests`)*
- [x] "Sincronizar ahora" procesa las carpetas y crea/actualiza casos igual que la app hermana. *(el reporte CSV por ciclo ahora se escribe de verdad — C1 remediado; `OnPostSyncNowTests`)*
- [x] `ComunaContact` sigue funcionando sin cambios de comportamiento. *(C2 remediado: el módulo de ruteo ya no escribe en `ComunaContact` ni fabrica comunas — proyección de solo lectura en un solo sentido; spec enmendada)*
- [x] Sin sección `CambioDomicilio:` configurada, el resto de la app arranca igual. *(`CambioDomicilioStartupTests`)*
