import os
import subprocess

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
if os.path.isdir("/data/.openclaw/workspace-codex/projects/lexwolf"):
    _ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"

_DESKTOP_PROJ = os.path.join(_ROOT, "desktop", "LexWolf.csproj")


def test_desktop_projekt_vorhanden():
    assert os.path.exists(_DESKTOP_PROJ), f"Desktop-Projekt fehlt: {_DESKTOP_PROJ}"


def test_dotnet_build_desktop_client_erfolgreich():
    """Task #256: KONTEXT/Repro-Schritte im Task sind Platzhaltertext ('Test'/'x'),
    daher pruefen wir minimal, dass der Desktop-Client unveraendert baubar bleibt
    (DoD: bestehende Funktionen bleiben funktionsfaehig)."""
    result = subprocess.run(
        ["dotnet", "build", _DESKTOP_PROJ, "-c", "Release", "--nologo", "-p:EnableWindowsTargeting=true"],
        cwd=os.path.join(_ROOT, "desktop"), capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet build LexWolf.csproj schlaegt fehl:\n"
        f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
