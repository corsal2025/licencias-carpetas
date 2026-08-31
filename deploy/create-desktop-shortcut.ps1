<#
.SYNOPSIS
    Creates a desktop shortcut that opens the LicenciasCarpetas dashboard.

.DESCRIPTION
    Double-clicking it starts the app if it is not already running, and opens the
    dashboard in the browser otherwise. Starting it twice would leave a second
    process fighting for the same port, so the launcher checks first.

.PARAMETER PublishPath
    Folder where LicenciasCarpetas.exe was published.

.PARAMETER DashboardUrl
    Dashboard URL to open (see Kestrel endpoints in appsettings.json).
#>

param(
    [string]$PublishPath = (Join-Path $PSScriptRoot "..\publish"),
    [string]$DashboardUrl = "http://localhost:5010"
)

$ErrorActionPreference = "Stop"

$exePath = Join-Path $PublishPath "LicenciasCarpetas.exe"
if (-not (Test-Path $exePath)) {
    throw "No se encontró $exePath. Ejecuta primero: .\deploy\publish.ps1"
}

# Doble clic: si la app no corre, la arranca con --open-browser (Program.cs abre la pestaña
# ~2 s después de enlazar el puerto); si ya corre, solo abre otra pestaña.
$launcherPath = Join-Path $PublishPath "abrir-dashboard.ps1"
$launcherContent = @"
`$running = Get-Process -Name "LicenciasCarpetas" -ErrorAction SilentlyContinue
if (-not `$running) {
    Start-Process -FilePath "$exePath" -ArgumentList "--open-browser" -WorkingDirectory "$PublishPath" -WindowStyle Hidden
} else {
    Start-Process "$DashboardUrl"
}
"@
Set-Content -Path $launcherPath -Value $launcherContent -Encoding UTF8

$desktopPath = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktopPath "Carpetas Licencias - Dashboard.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "powershell.exe"
$shortcut.Arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$launcherPath`""
$shortcut.WorkingDirectory = $PublishPath
$shortcut.Description = "Abrir el dashboard de Carpetas Licencias"
$iconPath = Join-Path $PublishPath "wwwroot\img\app-icon.ico"
if (Test-Path $iconPath) {
    $shortcut.IconLocation = "$iconPath,0"
} else {
    $shortcut.IconLocation = "$exePath,0"
}
$shortcut.Save()

Write-Host "Acceso directo creado en: $shortcutPath"
Write-Host "Al hacer doble clic: inicia la aplicación si no está corriendo, y abre $DashboardUrl."
