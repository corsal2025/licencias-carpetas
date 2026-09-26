# LicenciasCarpetas

Reemplazo del libro **"DETALLE CARPETAS DEPTO. LICENCIAS DE CONDUCIR"** por una aplicación web
(.NET 10, Razor Pages, SQLite) con el mismo stack que `outlook-comuna-router`, que corre en el
mismo equipo del Depto. de Licencias de Conducir de la Municipalidad de Valparaíso.

El Excel se importa una vez; desde ahí la base de datos es la fuente de verdad. El archivo original
nunca se modifica: solo se lee.

## Qué reemplaza, hoja por hoja

| En el Excel | En la aplicación |
| --- | --- |
| 18 hojas de agenda (mes × oficina: Av. Argentina, Placilla, Merc. Puerto) | Pantalla **Casos** (`/`), con filtros por oficina, año, mes, estado, decisión y sector |
| Hojas `PLANTILLA MODELO` (listas desplegables) | Catálogos fijos de estado de carpeta, decisión final e idoneidad moral |
| Columna `FECHA ULTIMA CARPETA` (fecha **o** comuna) | `Última carpeta`: si es fecha, deriva el sector; si es comuna, marca la carpeta como pedida a otro municipio |
| Búsqueda manual de la carpeta física | Sector derivado automáticamente: **Archivo** (antes de julio 2023) u **Oficina 43** (desde julio 2023), con listado imprimible en `/Sector/Archivo` y `/Sector/Oficina43` |
| Hoja `ESCANEADAS Y SUBIDAS` | Pantalla **Estadísticas** (`/Estadisticas`): agendadas, atendidas y % de atención se calculan solos; escaneadas y subidas siguen siendo contadores manuales por día |
| Hoja `CORREOS CAMBIO DE DOMICLIO` | Pantalla **Comunas** (`/Comunas`), buscable y editable |
| Copiar/pegar entre hojas para reportes | **Exportar a Excel** respetando los filtros aplicados |

| Colores de fila por `ESTADO DE LA CARPETA` (formato condicional) | Los mismos 13 colores del libro, copiados de sus reglas. Cambian al elegir otro estado, sin recargar |
| `COUNTIF` que marcaba RUT repetidos en violeta | El RUT se resalta cuando esa persona aparece más de una vez en lo que se está mirando |

Además de lo que hacía el Excel:

- **RUT validado** con dígito verificador y normalizado a `13.025.150-1`. Los RUT inválidos se
  conservan tal cual y el caso queda marcado como *Requiere revisión*.
- **Sin duplicados**: reimportar el mismo archivo actualiza los casos existentes en lugar de
  duplicarlos (clave: oficina + fecha de citación + RUT; si el RUT o la fecha no son legibles, la
  celda de origen — hoja y fila).
- **Variantes de escritura unificadas**: `ESPERA EXÁMEN`/`ESPERA EXAMEN`,
  `SE ENCUENTRA EN OF.43`/`OF. 43`, `CAMBIO DE DOM SUBIDO CON CORREO`/`CAMBIO DOM. SUBIDO CON CORREO`
  colapsan a un solo valor de catálogo.
- **Trazabilidad**: cada caso guarda de qué hoja y fila del Excel salió, y quién lo editó por última
  vez desde el dashboard.
- **Hojas reconocidas por su contenido, no por su nombre**: el libro se renombra (`ESCANEADAS Y
  SUBIDAS` pasó a `HOJA ESTADISTICAS`) y la importación sigue encontrándolas por sus encabezados.
- **Fecha de subida automática**: marcar cualquier estado de subida (Conaset, F8, oficio, cambio de
  domicilio) escribe la fecha del día. Una fecha ya escrita no se toca.
- **Orden con tildes correcto**: `ÁLVARO`, `MUÑOZ` y `PÉREZ` ordenan donde corresponde, no después
  de la Z.

## Campos que el Excel no tiene

Se escriben a mano y **una reimportación nunca los pisa**, porque el libro no trae columna para ellos:

| Campo | Para qué |
| --- | --- |
| **Penúltima carpeta** | Fecha de la carpeta anterior a la última. No influye en el sector |
| **Código F8** | Código del caso F8, texto libre |
| **Licencias** | Clases que el contribuyente viene a obtener (A1–A5, B, C, D, E, F). Selección múltiple |
| **Atendido** | Si la persona asistió. Alimenta el % de atención de Estadísticas |
| **Marcar** | Marca personal del operador; también elige qué entra en los informes de sector |

## Usar en local sin compilar (Windows)

La carpeta **`app-windows/`** trae la aplicación ya compilada:

1. Instalar una vez el **ASP.NET Core Runtime 10** (Windows x64):
   https://dotnet.microsoft.com/download/dotnet/10.0
2. Clonar o descargar el repositorio y hacer doble clic en `app-windows\Iniciar.bat`.
   Se abre http://localhost:5010. Para cerrar, cerrar la ventana negra.
3. Si no se conoce la clave de `admin`: en `app-windows`, `LicenciasCarpetas.exe --reset-password admin`.

La base se crea en `app-windows\data\` (fuera de git, ver `.gitignore`). Al hacer `git pull` con una
versión nueva, los datos se conservan. Para regenerar la carpeta:
`dotnet publish src/LicenciasCarpetas -c Release -r win-x64 --self-contained false -o app-windows`.

## Requisitos

- .NET 10 SDK
- El archivo `.xlsx` accesible (puede estar abierto en Excel o sincronizando en Google Drive: se lee
  con acceso compartido).

## Puesta en marcha

```powershell
dotnet build -c Release
```

1. Crear el usuario del dashboard:

```powershell
dotnet run -c Release --project src/LicenciasCarpetas -- --add-user operador
```

2. Importar el Excel (una vez; se puede repetir cuando el archivo cambie):

```powershell
dotnet run -c Release --project src/LicenciasCarpetas -- --import "G:\...\DETALLE CARPETAS DEPTO. LICENCIAS DE CONDUCIR 2026.xlsx"
```

Salida de la importación real del libro 2026:

```
Hojas leídas:        20
Filas leídas:        20932
Casos nuevos:        20894
Casos actualizados:  38
Requieren revisión:  31
Días con contadores: 149
Correos de comunas:  494
```

3. Levantar el dashboard:

```powershell
dotnet run -c Release --project src/LicenciasCarpetas
```

Abre `http://localhost:5010`. Kestrel escucha solo HTTP en ese puerto (`Kestrel:Endpoints` en
`appsettings.json`), el mismo binding que usa Docker detrás de un proxy inverso; no hay HTTPS ni
redirección local.

La aplicación corre **on-premise**, en el equipo del departamento (necesita estar dentro de la red
municipal para llegar al buzón de Exchange). No hay despliegue en la nube.

La ruta por defecto del Excel se configura en `Carpetas:DefaultWorkbookPath` (ver
`appsettings.Example.json`) y queda precargada en la pantalla `/Importar`.

## Pantallas

- `/` **Casos**: tabla paginada (100 por página) con edición en línea de nombre, RUT, fechas,
  atención, estado, decisión e idoneidad; alta manual de filas nuevas; marcado personal; envío a la
  papelera; exportación a `.xlsx` de **toda** la vista filtrada (sin tope de filas). Cualquier
  encabezado ordena por esa columna, y un segundo clic invierte el orden.
  Las carpetas ya subidas a Conaset bajan al final de la lista, ordenadas por fecha de subida: son
  trabajo terminado y dejan arriba lo pendiente.
- `/Papelera`: casos eliminados, con restauración. Nada se borra de verdad hasta confirmarlo ahí.
- `/Usuarios`: crear cuentas, cambiarle la contraseña a quien la olvidó, eliminar usuarios, y
  asignar **rol, módulos y sedes**. Incluye el historial de cambios de permisos.
- `/Persona` **Ficha de persona**: se escribe un RUT (con o sin puntos/guion) y se ve en una sola
  página, en solo lectura, todo lo registrado para esa persona: sus casos de todas las sedes, el
  historial de cambios, F8, Cambio de Domicilio (recibidos y solicitados) y Certificados. El RUT
  viaja por POST y nunca queda en la URL. También se abre con el botón **🧾 Ficha** de la búsqueda
  global (Ctrl+K).
- `/Sector/Archivo` y `/Sector/Oficina43`: **documento imprimible** para pedir carpetas físicas, con
  escudo municipal, destino, período, quién solicita, total, filas numeradas y firmas. Filtrable por
  día o mes de citación. Muestra nombre, RUT y fecha de última carpeta, con el mes escrito
  (`15/marzo/2024`) para que nadie confunda día con mes en papel.
  El botón **"Marcar como pedidas"** registra lo que salió impreso y esas carpetas no vuelven a
  aparecer en el siguiente documento; "Volver a pedir" las devuelve a la lista.
- `/Estadisticas`: día a día del mes elegido — escaneadas y subidas editables, agendadas/atendidas y
  % de atención por oficina calculados, desglose por estado de carpeta y decisión final, y **tres
  gráficos de barras**: atención por oficina, licencias por clase (profesionales destacadas) y
  estados de carpeta, cada barra con el mismo color que esa fila tiene en Casos. Los gráficos son
  CSS puro, sin librerías: un equipo sin internet no puede quedarse con la pantalla en blanco.
- `/Estadisticas/Comparativo` **Comparativo por sede**: Av. Argentina, Placilla y Merc. Puerto
  lado a lado para un mes, semestre o año: otorgados, denegados, en curso y sin decisión; desglose por
  estado; días citación→decisión (promedio y mediana, con el N usado); backlog por antigüedad
  (🟢 <7 días, 🟡 7–14, 🔴 ≥15); tendencia mensual e indicadores de calidad de datos. Exporta a
  Excel (hojas Resumen, Estados y Tendencia). La fecha de decisión sale del historial de cambios, así
  que los casos importados del libro (nunca editados acá) se informan como "decididos sin fecha".
- `/Sector/Archivo` y `/Sector/Oficina43`: listado imprimible (imprimir del navegador → PDF) de las
  carpetas a retirar. Por defecto muestra solo los casos marcados; hay un enlace para ver todo el sector.
- `/Comunas`: directorio de correos por municipio.
- `/Importar`: importación desde el dashboard, con resumen y avisos.
- `/ChangePassword`: cambio de contraseña del operador.
- `/Setup`: creación de la primera cuenta. Solo aparece mientras no existe ninguna; después se
  cierra y las cuentas se crean desde `/Usuarios`, ya con sesión iniciada.

## Cuentas, roles y sedes

| Rol | Sedes | Pantallas |
| --- | --- | --- |
| Administrador | Todas | Todo, incluida Usuarios (único que asigna roles y sedes) |
| Jefatura | Todas | Todo menos Usuarios |
| Coordinador | Las asignadas | Casos, estadísticas, sector, comunas, papelera; sin Importar |
| Administrativo | Las asignadas | Casos; F8 y Cambio de Domicilio según se le habilite |

- La restricción por sede se aplica **en el servidor**: listados, conteos, exportación, papelera,
  sector, estadísticas, comparativo, ficha de persona y búsqueda global solo muestran casos de las
  sedes del usuario, y cualquier caso de otra sede pedido por URL responde 404.
- F8, Cambio de Domicilio, Certificados e Importar no tienen sede y no se filtran.
- Un cambio de rol, módulos o sedes se aplica en la **siguiente acción** del usuario afectado, sin que
  tenga que volver a entrar, y queda registrado en el historial de permisos.
- Al actualizar desde una versión anterior, todos los usuarios existentes reciben las tres sedes: nadie
  pierde acceso.


La primera cuenta se crea sola en pantalla al abrir una instalación nueva. Las siguientes, desde
`/Usuarios`. Mínimo 8 caracteres, y el nombre de usuario no distingue mayúsculas ni espacios
sobrantes.

Si alguien olvida su contraseña, otro usuario se la cambia desde `/Usuarios`. Si **nadie** puede
entrar, se restablece desde el equipo:

```powershell
.\LicenciasCarpetas.exe --list-users
.\LicenciasCarpetas.exe --reset-password operador
```

No hay recuperación por correo: el sistema no tiene servidor de correo, y una pantalla web que
cambiara contraseñas sin sesión iniciada dejaría la agenda completa a merced de cualquiera que
alcance este puerto.

## Respaldos

Cada arranque copia la base a `data/backups/` **antes** de aplicar migraciones, y conserva las 10
copias más recientes (`Carpetas:BackupsToKeep`). Un fallo al respaldar nunca impide arrancar.

**Cifrado de respaldos (recomendado):** con `Carpetas:BackupEncryptionKey` en
`appsettings.Local.json`, cada copia se verifica y se guarda solo cifrada (`.db.enc`, AES-256-GCM);
la copia en claro se borra. Así un respaldo copiado a un pendrive o carpeta compartida no expone RUT,
nombres, correos ni celulares. Para restaurar:

```powershell
.\LicenciasCarpetas.exe --decrypt-backup data\backups\carpetas-20260926-0900.db.enc restaurada.db
```

Si se pierde la frase, los respaldos cifrados **no se pueden recuperar**: guardarla también fuera
del equipo. Los respaldos anteriores a activar el cifrado siguen en claro hasta que rotan.

Las copias quedan junto a la base, en el mismo disco: sirven contra un borrado accidental o una
migración fallida, **no** contra la falla del disco. Para eso hay que copiar `data/` a otro medio.

## Pruebas

```powershell
dotnet test -c Release
```

Más de 600 pruebas: catálogos y variantes de escritura, validación de RUT, lectura de celdas (fecha real,
serial de Excel, texto tipeado), mapeo de filas, deduplicación al reimportar, detección de hojas por
contenido, filtros, orden (incluido el de nombres con tilde y el hundimiento de lo ya subido),
paginación, papelera, asistencia, clases de licencia, colores por estado, RUT repetidos, informes de
sector por período, estadísticas, saneado del libro, exportación completa, autenticación (bloqueo por
intentos fallidos, mayúsculas en el usuario, restablecimiento), respaldos, ficha de persona, KPI por
sede y roles por sede (filtro en cada pantalla, 404 fuera de sede, sesión rehecha al cambiar permisos
y una prueba de arquitectura que impide que una pantalla use el repositorio de casos sin filtro).

### CI

`.github/workflows/ci.yml` corre `build + test` en cada push y pull request a `main`, y también a
mano desde la pestaña Actions.

**Hoy ningún run llega a ejecutarse**: todos terminan en `startup_failure` a los 0 segundos, sin
crear un solo job. La causa está fuera del repositorio, y así se descartó lo demás:

- No es el archivo: un workflow mínimo de un solo `echo hello` falla exactamente igual.
- No es el sistema operativo del runner: falla en `windows-latest` y en `ubuntu-latest`.
- No es este repositorio: `outlook-comuna-router` acumula 22 runs, **todos** `startup_failure` desde
  el primero (10-07-2026); nunca ejecutó uno.
- Actions está habilitado en el repositorio (`enabled: true`, `allowed_actions: all`) y la cuenta es
  plan pro. Otros repositorios de la misma cuenta sí ejecutaron runs reales hasta el 01-07-2026;
  desde entonces no hay minutos de Actions consumidos en ninguno.

Eso apunta a la facturación de la cuenta (límite de gasto alcanzado o método de pago rechazado), que
bloquea Actions en todos los repositorios privados a la vez. Se revisa en
`github.com/settings/billing`. Mientras tanto, la verificación es local con `dotnet test`.

## Despliegue

El equipo del departamento no necesita el SDK: la aplicación se publica como un único `.exe`
autocontenido. Instalación completa en un comando (certificado HTTPS, usuario, importación del
Excel y acceso directo en el Escritorio):

```powershell
.\deploy\publish.ps1 -DevCert -AddUser operador -ImportWorkbook "G:\...\DETALLE CARPETAS 2026.xlsx" -Shortcut
```

Runbook completo, actualización, reimportación y respaldos en [`deploy/README.md`](deploy/README.md).
No lleva Tarea Programada: a diferencia de `outlook-comuna-router`, esta aplicación no corre en
segundo plano, se abre cuando el operador la necesita.


**Base de datos:** se mantiene SQLite también en un servidor municipal (decisión 2026-09-26). Para 3
sedes y unas decenas de miles de casos alcanza; el servidor corre la misma app con Docker/TrueNAS
(`TRUENAS_DEPLOY.md`). La base debe quedar en disco local del servidor, no en una carpeta de red.

## Detalles de implementación que conviene conocer

- **Validaciones de datos del Excel**: ClosedXML se niega a abrir el libro real porque las listas
  desplegables de las hojas `PLANTILLA` superan los 255 caracteres que acepta. La importación
  detecta ese fallo, hace una copia temporal sin los nodos `dataValidation` y la lee desde ahí
  (`Import/WorkbookSanitizer.cs`). El archivo original queda intacto.
- **La importación tarda ~15 segundos** para las ~21.500 filas del libro completo (una transacción
  por hoja; fila por fila tardaba minuto y medio).
- **Datos personales en reposo**: la base SQLite guarda nombres y RUT sin cifrar; depende del
  cifrado de disco del equipo. Está en `data/`, ignorado por git.

## Estructura

```
src/LicenciasCarpetas/
  Domain/          # FolderCase, catálogos (estado, decisión, idoneidad, oficina), RUT, fechas en español
  Configuration/   # CarpetasOptions (bind de appsettings)
  Import/          # Lectura del .xlsx: hojas de agenda, contadores diarios, directorio de comunas
  Persistence/     # Repositorios SQLite (sin ORM)
  Statistics/      # Reconstrucción de la hoja ESCANEADAS Y SUBIDAS
  Reporting/       # Exportación a .xlsx
  F8/              # Módulo F8 Urgentes: mismo login y misma carpetas.db (tabla UrgentRequest propia),
                   # detrás de la política "F8Access" (claim mod:f8-urgentes)
  CambioDomicilio/ # Módulo Cambio de Domicilio: mismo login y misma carpetas.db (tablas PersonRequest,
                   # DeletedSourceMessage, DiscardedEmail propias), sincroniza contra EWS solo cuando
                   # el operador aprieta "Sincronizar ahora" (sin polling automático), detrás de la
                   # política "CambioDomicilioAccess" (claim mod:cambio-domicilio)
  Dashboard/       # Auth (PBKDF2) y páginas Razor (incluye Dashboard/Pages/F8/ y Dashboard/Pages/CambioDomicilio/)
tests/LicenciasCarpetas.Tests/
```

## Cambio de Domicilio: correo institucional

El botón **"Sincronizar Ahora"** del módulo *Enviar Cambio de Domicilio* ya está cableado a Exchange
Web Services (`https://mail.munivalpo.cl/EWS/Exchange.asmx`, buzón `cambiodedomicilio@munivalpo.cl`).
Falta solo cargar `CambioDomicilio:Ews:Username` y `Password`, que **no viajan en git**: van en
`appsettings.Local.json` junto al `.exe` (plantilla `src/LicenciasCarpetas/appsettings.Local.Example.json`)
o en variables de entorno. Runbook en [`deploy/README.md`](deploy/README.md) § "Correo institucional (EWS)".
Funcionamiento completo de principio a fin, con diagramas de flujo: [`docs/cambio-domicilio-informe.md`](docs/cambio-domicilio-informe.md)
(versión imprimible: `docs/cambio-domicilio-informe.html`).


El Panel de Control muestra el estado del correo institucional (🟢 conectado, 🔴 sin conexión,
⚪ sin configurar o sin verificar) con un botón **Verificar correo**, que lista la carpeta de entrada
con un tope de 15 segundos. Para el detalle técnico del error está `--test-ews`.

## Arquitectura

Diagrama vivo, versionado en el repo y sin conexión a internet: [`docs/arquitectura.html`](docs/arquitectura.html).
Cubre capas, ciclo de vida de una carpeta, modelo de datos y despliegue. Toda propuesta SDD que
cambie arquitectura, módulos, modelo o despliegue debe actualizarlo (ver `openspec/config.yaml`).

## Créditos

Desarrollado por **Raúl Salazar**. Ver [`AUTHORS.md`](AUTHORS.md).
