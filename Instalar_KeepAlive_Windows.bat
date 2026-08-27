@echo off
chcp 65001 > nul
echo ========================================================
echo   Instalando Servicio de Keep-Alive en Windows
echo ========================================================
echo.

set SCRIPT_PATH=%~dp0KeepAlive_Silencioso.vbs

schtasks /create /tn "LicenciasCarpetas_KeepAlive" /tr "wscript.exe \"%SCRIPT_PATH%\"" /sc onlogon /rl highest /f

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [OK] Tarea programada instalada con éxito.
    echo Se iniciará automáticamente cada vez que inicies sesión en Windows.
    echo.
    echo Iniciando el servicio ahora mismo...
    wscript.exe "%SCRIPT_PATH%"
    echo [OK] Servicio en ejecución en segundo plano.
) else (
    echo.
    echo [ERROR] No se pudo crear la tarea programada. Intenta ejecutar como Administrador.
)

pause
