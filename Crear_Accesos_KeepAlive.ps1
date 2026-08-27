$wsh = New-Object -ComObject WScript.Shell

# Acceso directo en el Escritorio
$desktopPath = [Environment]::GetFolderPath('Desktop')
$linkDesktop = $wsh.CreateShortcut("$desktopPath\Mantener_Render_Despierto.lnk")
$linkDesktop.TargetPath = "wscript.exe"
$linkDesktop.Arguments = '"C:\Users\raul.salazar\Desktop\1.-licencias-carpetas\KeepAlive_Silencioso.vbs"'
$linkDesktop.WorkingDirectory = "C:\Users\raul.salazar\Desktop\1.-licencias-carpetas"
$linkDesktop.Description = "Mantiene despierto el servidor de Render en segundo plano"
$linkDesktop.Save()

# Acceso directo en Inicio de Windows (Inicio automático al encender el PC)
$startupPath = [Environment]::GetFolderPath('Startup')
$linkStartup = $wsh.CreateShortcut("$startupPath\Mantener_Render_Despierto.lnk")
$linkStartup.TargetPath = "wscript.exe"
$linkStartup.Arguments = '"C:\Users\raul.salazar\Desktop\1.-licencias-carpetas\KeepAlive_Silencioso.vbs"'
$linkStartup.WorkingDirectory = "C:\Users\raul.salazar\Desktop\1.-licencias-carpetas"
$linkStartup.Description = "Mantiene despierto el servidor de Render en segundo plano"
$linkStartup.Save()

Write-Output "Accesos directos creados e instalados en Inicio de Windows con éxito."
