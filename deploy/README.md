# Despliegue

La aplicación se publica como un único `.exe` autocontenido: el equipo del departamento **no
necesita tener instalado el SDK ni el runtime de .NET**.

A diferencia de `outlook-comuna-router`, esta no corre en segundo plano ni revisa un buzón, así que
no lleva Tarea Programada. Se abre cuando el operador la necesita, con el acceso directo del
Escritorio.

## Instalación completa (equipo nuevo)

Desde una máquina con .NET 10 SDK y el repositorio clonado:

```powershell
.\deploy\publish.ps1 -DevCert -AddUser operador -ImportWorkbook "G:\...\DETALLE CARPETAS DEPTO. LICENCIAS DE CONDUCIR 2026.xlsx" -Shortcut
```

Eso hace, en orden:

1. **`-DevCert`** — instala y confía el certificado HTTPS de desarrollo (`dotnet dev-certs https --trust`).
   (La app corre en HTTP en `http://localhost:5010`, así que este paso hoy es opcional.)
2. Compila, corre las 89 pruebas y publica en `publish\LicenciasCarpetas.exe` (~109 MB, incluye el
   runtime). Con `-SkipTests` se salta las pruebas.
3. Avisa si `Carpetas:DefaultWorkbookPath` está vacío. No es obligatorio: la ruta del Excel también
   se escribe a mano en la pantalla `/Importar`. Para dejarla fija hay que editar
   `src\LicenciasCarpetas\appsettings.json` y volver a publicar — `dotnet publish` sobrescribe el
   `appsettings.json` de `publish\` en cada publicación.
4. **`-AddUser operador`** — crea el usuario del dashboard y pide la contraseña por consola.
5. **`-ImportWorkbook`** — importa el Excel (~90 s con el libro completo de 2026).
6. **`-Shortcut`** — deja "Carpetas Licencias - Dashboard" en el Escritorio.

## Uso diario

Doble clic en el acceso directo del Escritorio:

- Si la aplicación no está corriendo, la inicia; ella misma abre el navegador a los ~2 segundos.
- Si ya está corriendo, solo abre `http://localhost:5010` en una pestaña nueva.

Para cerrarla: Administrador de tareas → proceso `LicenciasCarpetas`.

## Actualizar a una versión nueva

```powershell
git pull
.\deploy\publish.ps1
```

La base de datos vive en `publish\data\carpetas.db` y **no se toca al republicar**: los casos, las
ediciones y los usuarios sobreviven a la actualización.

## Reimportar el Excel

Cuando el libro cambie y se quiera traer lo nuevo:

```powershell
cd publish
.\LicenciasCarpetas.exe --import "G:\...\DETALLE CARPETAS DEPTO. LICENCIAS DE CONDUCIR 2026.xlsx"
```

O desde la pantalla `/Importar` del dashboard. Los casos que ya existen se actualizan, no se
duplican. **Ojo**: el Excel gana sobre lo editado en el dashboard para las filas que reimporta; lo
único que la importación nunca pisa es el marcado personal del operador.

## Copias de seguridad

Todo el estado está en un solo archivo: `publish\data\carpetas.db`. Copiarlo (con la aplicación
cerrada) es respaldo suficiente. Contiene nombres y RUT sin cifrar, así que el respaldo va donde
vaya el resto de los datos del departamento, no a una carpeta pública.

## Scripts

| Script | Para qué |
| --- | --- |
| `publish.ps1` | Publica el exe y, opcionalmente, certificado, usuario, importación y acceso directo |
| `create-desktop-shortcut.ps1` | Solo el acceso directo (útil en un equipo donde ya se copió `publish\`) |

## Segundo equipo sin SDK

Copiar la carpeta `publish\` completa, y en el equipo destino:

```powershell
dotnet dev-certs https --trust   # requiere el SDK; si no lo hay, aceptar la advertencia del navegador
.\deploy\create-desktop-shortcut.ps1 -PublishPath "C:\ruta\a\publish"
```

Si se copia también `publish\data\carpetas.db`, el equipo arranca con todos los casos ya
importados; si no, se importa el Excel allí mismo.

## Correo institucional (EWS)

El botón **"Sincronizar Ahora"** del módulo *Enviar Cambio de Domicilio* se conecta por EWS al buzón
compartido `cambiodedomicilio@munivalpo.cl` (`https://mail.munivalpo.cl/EWS/Exchange.asmx`, auth
Basic sobre TLS). La URL y demás parámetros ya están en `appsettings.json`; **falta solo el usuario y
la clave del buzón**, que no viajan en git.

Van en `appsettings.Local.json` **junto al `.exe`** (plantilla: `appsettings.Local.Example.json`).
Ese archivo está fuera de git y `dotnet publish` **no lo sobrescribe**: sobrevive a cada `git pull` +
republicación.

Dos formas de dejarlo:

```powershell
# a) Al publicar, pasando las credenciales (las escribe en publish\appsettings.Local.json):
.\deploy\publish.ps1 -EwsUser "cambiodedomicilio@munivalpo.cl" -EwsPassword "LA-CLAVE"

# b) A mano: copiar la plantilla y editarla
copy src\LicenciasCarpetas\appsettings.Local.Example.json publish\appsettings.Local.json
notepad publish\appsettings.Local.json
```

Alternativa sin archivo (variables de entorno de la máquina):

```powershell
setx CambioDomicilio__Ews__Username "cambiodedomicilio@munivalpo.cl"
setx CambioDomicilio__Ews__Password "LA-CLAVE"
```

Sin credenciales, la app arranca igual y todo lo demás funciona; solo "Sincronizar Ahora" muestra
error hasta configurarlas. Las carpetas de Outlook que lee (`CARP. PARA PEDIR`) y a las que mueve el
correo ya subido (`CARP. YA SUBIDAS`) deben existir en ese buzón con ese nombre exacto; se cambian en
`appsettings.json` (`CambioDomicilio:SourceFolderName` / `ConfirmationFolderName`).
