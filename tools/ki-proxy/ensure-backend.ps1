# Stellt sicher, dass das lokale Backend (lokales_backend.py: SSH-Tunnel zur
# Server-Datenbank + uvicorn auf Port 8000, KI ueber ask_llm -> KI-Proxy)
# laeuft: startet es bei Bedarf in einem eigenen Konsolenfenster und wartet,
# bis /health antwortet. Wird von .vscode/tasks.json (F5) und start-lokal.ps1
# genutzt. Exit 1, wenn es nicht hochkommt. Nur ASCII, siehe ensure-proxy.ps1.
$url = "http://localhost:8000/health"
$starter = Join-Path $PSScriptRoot "lokales_backend.py"

function Test-Backend {
    # 10 s: waehrend einer laufenden KI-Anfrage oder beim Laden des Embedding-Modells
    # antwortet /health verzoegert - mit 2 s galt ein laufendes Backend als "nicht erreichbar".
    try { Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 10 | Out-Null; return $true }
    catch { return $false }
}

if (Test-Backend) {
    Write-Host "Lokales Backend laeuft bereits auf http://localhost:8000"
    exit 0
}

$env:PYTHONIOENCODING = "utf-8"
Start-Process python -ArgumentList "-u", "`"$starter`"" -WindowStyle Minimized
# Start dauert auf langsamen Rechnern gut 2-3 Minuten (Server-Umgebung per SSH
# lesen, Tunnel, Python-Bibliotheken laden, Neo4j-Verbindung).
for ($i = 0; $i -lt 300; $i++) {
    Start-Sleep -Seconds 1
    if (Test-Backend) {
        Write-Host "Lokales Backend gestartet auf http://localhost:8000 (eigenes Konsolenfenster)"
        exit 0
    }
}
Write-Host "Lokales Backend ist nach 300 s nicht erreichbar - siehe dessen Konsolenfenster"
exit 1
