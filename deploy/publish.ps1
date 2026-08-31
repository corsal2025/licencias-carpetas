<#
.SYNOPSIS
    Publishes LicenciasCarpetas as a self-contained single-file exe.

.DESCRIPTION
    - Builds, tests and publishes to ./publish (no .NET SDK needed on the target PC)
    - Creates appsettings.json from the example if missing
    - Optionally: trusts the HTTPS dev certificate, creates the dashboard user,
      imports the Excel workbook and creates a desktop shortcut

.PARAMETER PublishDir
    Output directory for the published exe (default: ./publish)

.PARAMETER DevCert
    Trust the ASP.NET Core HTTPS development certificate

.PARAMETER AddUser
    Create a dashboard user with this name (prompts for the password)

.PARAMETER ImportWorkbook
    Path of the .xlsx to import right after publishing

.PARAMETER Shortcut
    Create a desktop shortcut that opens the dashboard

.PARAMETER SkipTests
    Publish without running the test suite first

.EXAMPLE
    # Full install on the department PC
    .\deploy\publish.ps1 -DevCert -AddUser operador -ImportWorkbook "G:\...\DETALLE CARPETAS 2026.xlsx" -Shortcut

    # Just rebuild the exe over an existing installation
    .\deploy\publish.ps1
#>

param(
    [string]$PublishDir = "publish",
    [switch]$DevCert,
    [string]$AddUser = "",
    [string]$ImportWorkbook = "",
    [switch]$Shortcut,
    [switch]$SkipTests,
    [string]$EwsUser = "",
    [string]$EwsPassword = ""
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = "src\LicenciasCarpetas\LicenciasCarpetas.csproj"
$TestProject = "tests\LicenciasCarpetas.Tests\LicenciasCarpetas.Tests.csproj"
$PublishPath = Join-Path $RepoRoot $PublishDir
$Exe = Join-Path $PublishPath "LicenciasCarpetas.exe"

Write-Host "=== LicenciasCarpetas: publicación ===" -ForegroundColor Cyan
Write-Host "Repo:   $RepoRoot"
Write-Host "Salida: $PublishPath"
Write-Host ""

if ($DevCert) {
    Write-Host "[1/6] Instalando certificado HTTPS de desarrollo..." -ForegroundColor Yellow
    dotnet dev-certs https --trust
    Write-Host "  OK - certificado instalado." -ForegroundColor Green
    Write-Host ""
}

Write-Host "[2/6] Compilando y publicando (self-contained, single-file)..." -ForegroundColor Yellow
Get-Process LicenciasCarpetas -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Push-Location $RepoRoot
try {
    dotnet restore
    dotnet build $Project -c Release --no-restore
    if (-not $SkipTests) {
        dotnet test $TestProject -c Release --verbosity normal
    }
    Get-Process LicenciasCarpetas -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    if (Test-Path $Exe) {
        Remove-Item $Exe -Force -ErrorAction SilentlyContinue
    }
    dotnet publish $Project -c Release -r win-x64 --self-contained /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $PublishPath
    
    # Copiar e_sqlite3.dll nativa si no quedó al lado del exe
    $sqliteDllSource = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\sqlitepclraw.lib.e_sqlite3" -Filter "e_sqlite3.dll" -Recurse | 
                       Where-Object { $_.FullName -like "*win-x64\native*" } | Select-Object -First 1
    if ($sqliteDllSource -and (Test-Path $sqliteDllSource.FullName)) {
        Copy-Item $sqliteDllSource.FullName (Join-Path $PublishPath "e_sqlite3.dll") -Force
    }
} finally {
    Pop-Location
}
Write-Host "  OK - publicado en: $PublishPath" -ForegroundColor Green
Write-Host ""

Write-Host "[3/6] Revisando appsettings.json y datos..." -ForegroundColor Yellow
# `dotnet publish` sobrescribe appsettings.json con el del proyecto en cada publicación, así que
# este paso solo avisa: cualquier ajuste permanente va en src\LicenciasCarpetas\appsettings.json.
$TargetConfig = Join-Path $PublishPath "appsettings.json"
$workbookPath = (Get-Content $TargetConfig -Raw | ConvertFrom-Json).Carpetas.DefaultWorkbookPath
if ([string]::IsNullOrWhiteSpace($workbookPath)) {
    Write-Host "  Carpetas:DefaultWorkbookPath está vacío." -ForegroundColor Yellow
    Write-Host "  No es obligatorio: la ruta del Excel también se escribe en la pantalla /Importar." -ForegroundColor Yellow
    Write-Host "  Para dejarla fija, edita src\LicenciasCarpetas\appsettings.json y vuelve a publicar." -ForegroundColor Yellow
} else {
    Write-Host "  OK - Excel por defecto: $workbookPath" -ForegroundColor Green
}

$PublishDataDir = Join-Path $PublishPath "data"
if (-not (Test-Path $PublishDataDir)) {
    New-Item -ItemType Directory -Path $PublishDataDir -Force | Out-Null
}
$ComunasCsvPublish = Join-Path $PublishDataDir "comunas.csv"
$ComunasCsvSource = Join-Path $RepoRoot "data\comunas.csv"
if ((Test-Path $ComunasCsvSource) -and (-not (Test-Path $ComunasCsvPublish))) {
    Copy-Item $ComunasCsvSource $ComunasCsvPublish -Force
    Write-Host "  OK - comunas.csv copiado a publish/data" -ForegroundColor Green
}

# Credenciales del correo institucional (EWS) para el botón "Sincronizar Ahora" del módulo
# Cambio de Domicilio. Se escriben en publish\appsettings.Local.json, que está fuera de git y
# que `dotnet publish` no sobrescribe: sobrevive a cada republicación.
$LocalConfig = Join-Path $PublishPath "appsettings.Local.json"
if ($EwsUser -and $EwsPassword) {
    $ewsBlock = [ordered]@{
        CambioDomicilio = [ordered]@{
            Ews = [ordered]@{
                Url      = "https://mail.munivalpo.cl/EWS/Exchange.asmx"
                Username = $EwsUser
                Password = $EwsPassword
            }
        }
    }
    $ewsBlock | ConvertTo-Json -Depth 5 | Out-File -FilePath $LocalConfig -Encoding utf8
    Write-Host "  OK - credenciales EWS escritas en appsettings.Local.json" -ForegroundColor Green
} elseif (Test-Path $LocalConfig) {
    Write-Host "  OK - appsettings.Local.json existente conservado (credenciales EWS)" -ForegroundColor Green
} else {
    Write-Host "  appsettings.Local.json no existe: el boton 'Sincronizar Ahora' de Cambio de" -ForegroundColor Yellow
    Write-Host "  Domicilio fallara hasta configurar CambioDomicilio:Ews (ver deploy\README.md)." -ForegroundColor Yellow
}
Write-Host ""

if ($AddUser) {
    Write-Host "[4/6] Creando usuario del dashboard: $AddUser..." -ForegroundColor Yellow
    & $Exe --add-user $AddUser
    Write-Host "  OK - usuario '$AddUser' creado." -ForegroundColor Green
    Write-Host ""
}

if ($ImportWorkbook) {
    Write-Host "[5/6] Importando el Excel (puede tardar ~90 s con el libro completo)..." -ForegroundColor Yellow
    if (-not (Test-Path $ImportWorkbook)) {
        throw "No se encontró el archivo Excel: $ImportWorkbook"
    }
    & $Exe --import $ImportWorkbook
    Write-Host "  OK - importación terminada." -ForegroundColor Green
    Write-Host ""
}

if ($Shortcut) {
    Write-Host "[6/6] Creando acceso directo en el Escritorio..." -ForegroundColor Yellow
    & (Join-Path $RepoRoot "deploy\create-desktop-shortcut.ps1") -PublishPath $PublishPath
    Write-Host ""
}

Write-Host "=== Listo ===" -ForegroundColor Cyan
Write-Host "Ejecutable:  $Exe"
Write-Host "Importar:    .\LicenciasCarpetas.exe --import `"<ruta del .xlsx>`""
Write-Host "Dashboard:   http://localhost:5010"
