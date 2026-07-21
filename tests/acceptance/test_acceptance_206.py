import os
import subprocess

_ANON_DIR = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/tools/Anonymisierer"
_CORE_CS = os.path.join(_ANON_DIR, "Anonymisierer.Core", "Anonymizer.cs")
_TEST_PROJ = os.path.join(_ANON_DIR, "Anonymisierer.Tests", "Anonymisierer.Tests.csproj")


def test_kein_getcultureinfo_aufruf_in_anonymizer():
    src = open(_CORE_CS, encoding="utf-8").read()
    for line in src.splitlines():
        code = line.split("//", 1)[0]
        assert "CultureInfo.GetCultureInfo(" not in code, (
            f"Anonymizer.cs ruft noch CultureInfo.GetCultureInfo(...) auf (Zeile: {line!r}); "
            "das crasht unter InvariantGlobalization (Task #206)."
        )


def test_dotnet_test_pool_erschoepfung_bleibt_gruen():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ, "--filter", "FullyQualifiedName~PoolExhaustion"],
        cwd=_ANON_DIR, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test schlaegt fehl:\n{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
