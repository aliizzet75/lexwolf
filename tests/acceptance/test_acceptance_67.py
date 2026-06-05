import pytest, socket, subprocess, httpx
from pathlib import Path
DESKTOP = Path('/data/.openclaw/workspace-codex/projects/lexwolf/desktop')
OLLAMA_HOST = 'openclaw-oo5q-ollama-1'
OLLAMA_PORT = 11434
def ollama_ok():
    s = socket.socket(); s.settimeout(2); r = s.connect_ex((OLLAMA_HOST, OLLAMA_PORT)); s.close(); return r == 0
def mistral_verfuegbar():
    if not ollama_ok(): return False
    r = httpx.get(f'http://{OLLAMA_HOST}:{OLLAMA_PORT}/api/tags', timeout=5)
    return any('mistral' in m.get('name','') for m in r.json().get('models',[]))
def test_ollama_client_datei_existiert():
    assert list(DESKTOP.glob('**/Ollama*.cs')), f'Keine OllamaClient .cs-Datei in {DESKTOP}'
def test_ollama_url_konfigurierbar():
    candidates = list(DESKTOP.glob('**/Ollama*.cs')) + list(DESKTOP.glob('**/appsettings*.json'))
    assert any('11434' in f.read_text() for f in candidates), 'Port 11434 nicht in Konfiguration gefunden'
def test_kein_silent_fail():
    files = list(DESKTOP.glob('**/Ollama*.cs'))
    assert files, 'OllamaClient.cs fehlt'
    src = files[0].read_text()
    if 'catch' in src.lower():
        assert 'throw' in src or 'Exception' in src, 'Ollama fängt Fehler still ab (catch ohne throw)'
def test_dotnet_build_mit_ollama():
    r = subprocess.run(['dotnet', 'build', str(DESKTOP / 'LexWolf.csproj'), '--no-restore', '-v', 'q'],
                       capture_output=True, text=True, timeout=120)
    assert r.returncode == 0, f'dotnet build fehlgeschlagen:\n{r.stdout}\n{r.stderr}'
@pytest.mark.skipif(not mistral_verfuegbar(), reason='mistral:7b nicht in Ollama (ollama pull mistral:7b erforderlich)')
def test_mistral_7b_aufrufbar():
    r = httpx.post(f'http://{OLLAMA_HOST}:{OLLAMA_PORT}/api/generate',
                   json={'model': 'mistral:7b', 'prompt': 'Hallo', 'stream': False}, timeout=30)
    assert r.status_code == 200, f'Ollama HTTP {r.status_code}: {r.text[:200]}'
