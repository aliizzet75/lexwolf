import os
import shutil
import subprocess

import pytest

_ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
_CSPROJ = os.path.join(_ROOT, "desktop", "LexWolf.Tests", "LexWolf.Tests.csproj")

dotnet_ok = shutil.which("dotnet") is not None


@pytest.mark.skipif(not dotnet_ok, reason="dotnet SDK nicht installiert")
def test_mandant_dropdown_logik_tests_gruen():
    """T#246 (Verify2 von T#239/T#245): MandantFilterTests.cs muss grün sein,
    d.h. ApplyMandantFilter zeigt bei leerem Filtertext (Dialog frisch
    geoeffnet) alle geladenen Mandanten statt eines leeren Dropdowns."""
    result = subprocess.run(
        ["dotnet", "test", _CSPROJ, "--filter", "FullyQualifiedName~MandantFilterTests"],
        capture_output=True, text=True, timeout=120,
    )
    assert result.returncode == 0, (
        f"T#246: MandantFilterTests.cs schlägt fehl -- Mandanten-Dropdown-Logik "
        f"regressiert.\nstdout:\n{result.stdout}\nstderr:\n{result.stderr}"
    )
    assert "Failed:     0" in result.stdout, result.stdout
