import os
import glob
import subprocess

_ANON_DIR = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/tools/Anonymisierer"
_TEST_PROJ = os.path.join(_ANON_DIR, "Anonymisierer.Tests", "Anonymisierer.Tests.csproj")

PFLICHTBEGRIFFE = [
    "Kindergeld", "Leasingrate", "Mietvertrag", "Immobilie",
    "Kfz", "Nebenkosten", "Steuererklarung", "Gesamtbelastung",
]


def _sources():
    files = glob.glob(os.path.join(_ANON_DIR, "Anonymisierer.Tests", "*.cs"))
    return "\n".join(open(f, encoding="utf-8").read() for f in files)


def test_xunit_kontext_test_enthaelt_alle_pflichtbegriffe():
    src = _sources()
    assert "[Fact]" in src, "Kein xUnit-Test ([Fact]) fuer Kontexterhaltung gefunden"
    assert "AnonymizeText" in src, "Test muss AnonymizeText aufrufen"
    for begriff in PFLICHTBEGRIFFE:
        assert begriff in src, f"Pflichtbegriff '{begriff}' fehlt im Test-Quellcode"


def test_dotnet_test_is_green():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ], cwd=_ANON_DIR,
        capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test schlägt fehl:\n{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
