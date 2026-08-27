@echo off
title Limpiador de Pendrive Philips (Disco 1)
color 1F

echo ================================================================
echo               LIMPIEZA PROFUNDA DE PENDRIVE (DISCO 1)
echo ================================================================
echo.
echo Se va a formatear y desbloquear el Disco 1 (Philips USB 32GB).
echo Todos los datos del pendrive seran eliminados.
echo.
pause

echo select disk 1 > "%temp%\diskpart_script.txt"
echo attributes disk clear readonly >> "%temp%\diskpart_script.txt"
echo clean >> "%temp%\diskpart_script.txt"
echo convert mbr >> "%temp%\diskpart_script.txt"
echo create partition primary >> "%temp%\diskpart_script.txt"
echo format fs=ntfs quick label="USB_32GB" >> "%temp%\diskpart_script.txt"
echo assign >> "%temp%\diskpart_script.txt"
echo exit >> "%temp%\diskpart_script.txt"

echo Ejecutando limpieza con Diskpart...
diskpart /s "%temp%\diskpart_script.txt"
del "%temp%\diskpart_script.txt"

echo.
echo ================================================================
echo  PROCESO COMPLETADO: El pendrive quedo formateado y sin bloqueos.
echo ================================================================
pause
