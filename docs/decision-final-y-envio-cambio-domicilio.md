# Decisión Final: opciones de examen · Envío de correos de Cambio de Domicilio

**Fecha:** 2026-09-07

Registro de tres pedidos del operador y su resolución.

---

## 1. Columna "Decisión Final": opciones de examen

**Pedido:** agregar a la columna Decisión Final las opciones "ex. méd.", "ex. teó.", "ex. prác.".

**Hecho.** Tres valores nuevos en el enum `FinalDecision`
([src/LicenciasCarpetas/Domain/FinalDecision.cs](../src/LicenciasCarpetas/Domain/FinalDecision.cs)):

| Valor | Texto en pantalla |
| --- | --- |
| `ExamenMedico` | `EX. MÉDICO` |
| `ExamenTeorico` | `EX. TEÓRICO` |
| `ExamenPractico` | `EX. PRÁCTICO` |

Detalles:

- Los valores van **al final del enum**. La base de datos guarda el número ordinal
  (`FolderCaseRepository`, columna `FinalDecision INTEGER`); reordenar los valores de arriba
  reinterpretaría datos ya guardados.
- Todos los desplegables (filtro superior, alta manual, edición en la tabla, drawer lateral) y
  el gráfico de Estadísticas se llenan desde `FinalDecisionCatalog.All`, así que las opciones
  aparecen en las cuatro pantallas sin tocar nada más.
- El resolvedor de texto (`FinalDecisionCatalog.TryResolve`) reconoce las variantes al importar
  el Excel: `EX. MÉDICO`, `EX. MEDICO`, `EXAMEN MÉDICO`, `EXAMEN MEDICO` — todas caen en
  `ExamenMedico` (ídem teórico y práctico).

---

## 2 y 3. Envío de correos a otras comunas / envío al marcar "Cambio de domicilio solicitado"

**Pedido:** que funcione el envío de correos de solicitud a otras comunas, y arreglar el envío
al seleccionar "Cambio de domicilio solicitado" en Casos.

### Diagnóstico: el servidor de correo (EWS) no responde

El módulo Cambio de Domicilio — entrada y salida — habla con el Exchange municipal por EWS
(`https://mail.munivalpo.cl/EWS/Exchange.asmx`, Basic sobre TLS). Pruebas del 2026-09-07 desde
el equipo `SERVERVALPO`:

| Prueba | Resultado |
| --- | --- |
| `TCP mail.munivalpo.cl:443` | conecta (`TcpTestSucceeded: True`) |
| `GET https://mail.munivalpo.cl/owa/` | **HTTP 302 en 0,8 s** |
| `GET https://mail.munivalpo.cl/` | **HTTP 302** |
| `GET https://mail.munivalpo.cl/EWS/Exchange.asmx` | **cuelga — 0 bytes, timeout** |
| `POST .../EWS/Exchange.asmx` (con credenciales) | **cuelga — 0 bytes, timeout** |
| `GET .../EWS/Services.wsdl` | **cuelga — 0 bytes, timeout** |
| `LicenciasCarpetas.exe --test-ews` | `SocketException 10054` / timeout a los 100 s |

`mail.munivalpo.cl` resuelve a `192.168.110.81` (LAN), ruteado por el gateway `192.168.16.254`.
El TLS al host **funciona** (OWA responde). Lo que no responde es el directorio virtual `/EWS/`.

**Conclusión:** problema del servidor Exchange, no de esta aplicación ni de las credenciales
(que están bien cargadas en `publish/appsettings.Local.json`). Hace una semana el envío
funcionaba, así que cambió algo del lado del servidor.

**Qué revisar en el servidor de correo (tarea de IT):**

- El application pool `MSExchangeServicesAppPool` en IIS — suele quedar detenido o reciclando.
- El directorio virtual `EWS` (Default Web Site / Exchange Back End).
- Un firewall / WAF que esté tragando las conexiones a la ruta `/EWS/` (OWA arriba + EWS abajo
  apunta a una regla por ruta).

Diagnóstico rápido desde el equipo: `cd publish && .\LicenciasCarpetas.exe --test-ews`.

### El botón "Solicitar" de Casos está sano

Se revisó porque el pedido decía "no dispara al seleccionar cambio de domicilio":

- **Permiso:** `Login.cshtml.cs` da acceso incondicional a Cambio de Domicilio a todo rol
  distinto de `Administrativo` (`UserRoleCatalog.HasFullModuleAccess`). La cuenta `raul`
  (Administrador) tiene el botón visible aunque su `CanAccessCambioDomicilio` esté en 0 en la
  base — ese flag solo cuenta para `Administrativo`.
- **Aparición:** el JS `toggleSolicitarForm` muestra el botón apenas se elige el estado, sin
  esperar a Guardar.
- El botón está bien: lo único que falla es el envío EWS, por lo de arriba.

### Cambios de código (mitigación)

Mientras el servidor no responda no sale ningún correo, pero se dejaron dos arreglos para que
la aplicación se comporte bien cuando vuelva y para que el error no confunda:

1. **Mensaje de error correcto.** Antes decía *"revise la configuración SMTP o la conexión"*.
   Este módulo usa EWS, no SMTP. Ahora:
   *"No se pudo enviar el correo: el servidor de correo institucional (EWS) no respondió. La
   solicitud quedó como Borrador — reintente cuando el correo vuelva."*
   ([Index.cshtml.cs](../src/LicenciasCarpetas/Dashboard/Pages/Index.cshtml.cs),
   [Solicitar/Index.cshtml.cs](../src/LicenciasCarpetas/Dashboard/Pages/CambioDomicilio/Solicitar/Index.cshtml.cs))

2. **Corte a los 20 segundos.** `EwsClient` reintenta hasta 4 veces con 100 s de timeout cada
   una, así que con el servidor caído la pantalla quedaba colgada varios minutos antes de
   mostrar el error. `OutboundRequestSender` ahora corta el envío interactivo a los 20 s y
   reporta el fallo; la solicitud sigue como Borrador para reintentar.
   ([OutboundRequestSender.cs](../src/LicenciasCarpetas/CambioDomicilio/Solicitar/OutboundRequestSender.cs))

---

## Verificación

- Suite completa: **538/538** pruebas verdes (Debug y Release). 8 casos nuevos.
- `deploy\publish.ps1` ejecutado: `publish\LicenciasCarpetas.exe` reconstruido,
  `appsettings.Local.json` conservado.
- Prueba de humo del exe publicado: arranca, `/Login` responde HTTP 200, `/` responde HTTP 302.

## Pendiente (fuera de esta aplicación)

El envío y la sincronización de Cambio de Domicilio quedan a la espera de que IT restablezca el
directorio virtual `/EWS/` en `mail.munivalpo.cl`. El código ya está listo para ese momento.
