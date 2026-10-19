# Startet KI-Proxy und lokales Backend (je eigenes Fenster) und danach LexWolf
# gegen das lokale Backend. Alle KI-Anfragen laufen dort ueber ask_llm zum
# Proxy (Claude Code auf diesem Rechner); Daten kommen per SSH-Tunnel vom
# Server, am Server selbst wird nichts geaendert. Nur ASCII, siehe ensure-proxy.ps1.
& (Join-Path $PSScriptRoot "ensure-proxy.ps1")
if ($LASTEXITCODE -ne 0) { exit 1 }
& (Join-Path $PSScriptRoot "ensure-backend.ps1")
if ($LASTEXITCODE -ne 0) { exit 1 }

$env:LEXWOLF_BACKEND_URL = "http://localhost:8000"
Start-Process "C:\Program Files\LexWolf\LexWolf.exe"
