import os
import glob
import subprocess

_ANON_DIR = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/tools/Anonymisierer"
_TEST_PROJ = os.path.join(_ANON_DIR, "Anonymisierer.Tests", "Anonymisierer.Tests.csproj")


def _sources():
    files = glob.glob(os.path.join(_ANON_DIR, "Anonymisierer.Tests", "*.cs"))
    return "\n".join(open(f, encoding="utf-8").read() for f in files)


def test_xunit_test_fuer_pool_erschoepfung_vorhanden():
    src = _sources()
    assert "PoolExhaustion" in src, "Regressionstest fuer Task #205 (Betrag-/Datum-Pool-Erschoepfung) fehlt"
    assert "[Fact]" in src and "AnonymizeText" in src
    for marker in ("Betrag", "Datum"):
        assert marker in src, f"Test muss Entitaetstyp '{marker}' abdecken"


def test_dotnet_test_pool_erschoepfung_ist_gruen():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ, "--filter", "FullyQualifiedName~PoolExhaustion"],
        cwd=_ANON_DIR, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test schlaegt fehl (erwartet, solange Task #205 nicht implementiert ist):\n"
        f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
