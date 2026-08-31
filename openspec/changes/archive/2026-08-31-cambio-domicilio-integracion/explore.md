# Exploración: integración del módulo Cambio de Domicilio

## Qué hace hoy la app hermana (outlook-comuna-router)

`RouterWorker : BackgroundService` — **corregido tras verificar código fuente**: NO sondea
automáticamente cada 30 min. `ExecuteAsync` solo asegura el schema al arrancar; el ciclo real
(`RunCycleAsync`) corre únicamente cuando el operador aprieta "Sincronizar ahora" en el dashboard
(`IndexModel.OnPostSyncNowAsync` llama `RunCycleAsync` directo). Un `SemaphoreSlim` evita
ciclos superpuestos si se aprieta el botón mientras uno ya corre.

Cuando corre el ciclo: lee la carpeta "CARP. PARA PEDIR" de un buzón Exchange on-prem vía
EWS/SOAP crudo (`Ews/`), extrae nombre+RUT por correo (`Extraction/`, multi-persona, RUT
validado por dígito verificador), resuelve la comuna solicitante por dominio/dirección
(`Directories/`), crea casos `Pending` en su propia SQLite `router.db` (`Routing/`, sin ORM),
también revisa la carpeta de confirmaciones para marcar casos como subidos, y escribe un reporte
CSV. Tiene su propio dashboard Razor Pages con su propio login. **Es on-demand, no background
continuo** — no hay Tarea Programada de Windows para esto.

## Hallazgo clave

licencias-carpetas **ya anticipa este fold-in**: `Program.cs` define política `CambioDomicilioAccess`
sobre claim `mod:cambio-domicilio`, `UserProvisioning.Create` ya acepta `canAccessCambioDomicilio`,
`DashboardUser.CanAccessCambioDomicilio` existe, y `_Layout.cshtml` trae el comentario literal
"Cambio de Domicilio sigue siendo una app externa (su turno de integrarse todavía no llega)".
Mismo patrón de scaffolding que se usó para F8 antes de portarlo.

## Recomendación

Fold-in completo (patrón F8): `src/LicenciasCarpetas/CambioDomicilio/{Domain,Persistence,Ews,
Extraction,Routing,Directories,Notifications,Reporting}` + `AddHostedService<RouterWorker>()`.
El código ya venía preparado para este paso. Tratar el hosting del worker en background y la
reconciliación `ComunaContact` vs `Directories` como decisiones explícitas de diseño en
`sdd-propose`, no como detalles de implementación — ninguno de los dos riesgos existió en el
port de F8.

## Top 3 riesgos (actualizado tras corrección: sync es on-demand, no automático)

1. **Ciclo se dispara desde una request HTTP** (el botón "Sincronizar ahora" hace POST →
   `RunCycleAsync`), y ese ciclo llama a Exchange vía EWS y puede tardar. Portado tal cual dentro
   de licencias-carpetas, hay que decidir si corre síncrono bloqueando la request o se dispara
   como tarea en segundo plano con el `SemaphoreSlim` ya existente evitando duplicados — pero ya
   no hay riesgo de "perder ciclos de background al reiniciar", porque no hay ciclos de
   background: cada sync es una acción explícita del operador.
2. **Exposición de credenciales EWS**: credenciales de cuenta de servicio Exchange on-prem, hoy
   aisladas en User Secrets de la app hermana, pasarían al mismo proceso que las credenciales SMTP
   existentes — mayor radio de exposición. Reconciliar claves `Router:*` con las convenciones
   `Carpetas:*`/`F8:*` de esta app.
3. **Modelos de datos parecidos pero no iguales**: `ComunaContact` (Comuna+Email, lista de
   notificación editable por usuario) vs `Directories` (Comuna+ContactEmail+Domain, usado para
   matching de ruteo entrante) — fusionarlos ingenuamente rompe el flujo de notificación o las
   reglas de matching.

## Contexto adicional

El openspec archivado de la app hermana documenta por qué se eligió confirmación manual como
gate: `2026-07-02-add-address-change-routing`, `add-folder-based-triggering`,
`add-upload-confirmation-flow`, `add-web-dashboard` — vale revisarlos antes del diseño.

## Siguiente fase

`sdd-propose` — definir alcance del fold-in, decisión sobre hosting del worker, y reconciliación
de modelos de datos ComunaContact/Directories.
