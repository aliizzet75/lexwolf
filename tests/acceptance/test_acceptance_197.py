import os
import glob
import subprocess

_ANON_DIR = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/tools/Anonymisierer"
_TEST_PROJ = os.path.join(_ANON_DIR, "Anonymisierer.Tests", "Anonymisierer.Tests.csproj")


def _test_sources():
    return glob.glob(os.path.join(_ANON_DIR, "Anonymisierer.Tests", "*.cs"))


def test_xunit_cross_document_test_exists():
    sources = "\n".join(open(f, encoding="utf-8").read() for f in _test_sources())
    assert "[Fact]" in sources, "Kein xUnit-Test ([Fact]) in Anonymisierer.Tests gefunden"
    assert "SetMappingFile" in sources, "Test muss SetMappingFile aufrufen"
    assert "GetReverseMapping" in sources, "Test muss GetReverseMapping() prüfen"
    assert "TestData" in sources, "Test muss Dokumente aus TestData/ verarbeiten"


def test_dotnet_test_is_green():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ, "--filter", "FullyQualifiedName~CrossDocument"],
        cwd=_ANON_DIR, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test schlägt fehl:\n{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
