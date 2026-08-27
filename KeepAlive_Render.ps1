# Script de Keep-Alive para Render
# Envía una petición HTTP cada 5 minutos para evitar que la instancia entre en suspensión

$url = "https://licencias-carpetas.onrender.com"
Write-Output "[$(Get-Date -Format 'HH:mm:ss')] Iniciando servicio Keep-Alive para $url..."

while ($true) {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15
        Write-Output "[$(Get-Date -Format 'HH:mm:ss')] Ping exitoso -> Status: $($response.StatusCode) (Servidor Activo)"
    } catch {
        Write-Output "[$(Get-Date -Format 'HH:mm:ss')] Ping enviado (Despertando servidor): $($_.Exception.Message)"
    }
    # Esperar 5 minutos (300 segundos)
    Start-Sleep -Seconds 300
}
