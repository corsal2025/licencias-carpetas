Set WshShell = CreateObject("WScript.Shell")
WshShell.Run "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & Replace(WScript.ScriptFullName, "KeepAlive_Silencioso.vbs", "KeepAlive_Render.ps1") & """", 0, False
