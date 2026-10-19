# Stellt sicher, dass der lokale KI-Proxy auf Port 8010 laeuft: startet ihn bei
# Bedarf in einem eigenen Konsolenfenster und wartet, bis er Verbindungen annimmt.# Wird von .vscode/tasks.json (F5) und start-lokal.ps1 genutzt. Exit 1, wenn der
# Proxy nicht hochkommt - dann startet LexWolf gar nicht erst ohne KI.
# Bewusst nur ASCII: Windows PowerShell 5.1 liest UTF-8 ohne BOM als ANSI, ein
# Gedankenstrich wird dabei zu einem Anfuehrungszeichen und zerlegt Strings.
$port = 8010
$proxy = Join-Path $PSScriptRoot "lexwolf_ki_proxy.py"

function Test-Proxy {
    $client = New-Object System.Net.Sockets.TcpClient
    try { $client.Connect("127.0.0.1", $port); return $true }
    catch { return $false }
    finally { $client.Dispose() }
}

if (Test-Proxy) {
    Write-Host "KI-Proxy laeuft bereits auf http://localhost:$port"
    exit 0
}

$env:PYTHONIOENCODING = "utf-8"
Start-Process python -ArgumentList "-u", "`"$proxy`"" -WindowStyle Minimized
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if (Test-Proxy) {
        Write-Host "KI-Proxy gestartet auf http://localhost:$port (eigenes Konsolenfenster, Log: $env:TEMP\lexwolf-ki-proxy.log)"
        exit 0
    }
}
Write-Host "KI-Proxy ist nach 30 s nicht erreichbar - siehe $env:TEMP\lexwolf-ki-proxy.log"
exit 1
