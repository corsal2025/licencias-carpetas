$script = "C:\Users\raul.salazar\Desktop\1.-licencias-carpetas\KeepAlive_Silencioso.vbs"
$action = New-ScheduledTaskAction -Execute "wscript.exe" -Argument "`"$script`""
$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Days 365)
Register-ScheduledTask -TaskName "LicenciasCarpetas_KeepAlive" -Action $action -Trigger $trigger -Settings $settings -Force
Start-Process -FilePath "wscript.exe" -ArgumentList "`"$script`""
Write-Output "Tarea programada instalada y ejecutándose correctamente."
