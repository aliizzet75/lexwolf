"""
Akzeptanztest für Task #242 (weiteres Duplikat von #239/#240/#241):
Mandanten-Dropdown in der Mandanten-Liste zeigt keine Einträge.

Der Fix liegt in desktop/MainWindow.xaml.cs (ApplyMandantFilter) und ist
bereits durch die xUnit-Regressionstests in
desktop/LexWolf.Tests/MandantFilterTests.cs abgedeckt (Task #239).
Dieser Test führt die echte Desktop-Client-Testsuite aus (kein Mock) und
stellt sicher, dass sie grün bleibt, d.h. dass das Dropdown bei leerem
Filtertext (Dialog frisch geöffnet) alle geladenen Mandanten anzeigt statt
leer zu bleiben.
"""
import shutil
import subprocess
from pathlib import Path

import pytest

_REPO = Path(__file__).resolve().parents[2]
_TEST_PROJECT = _REPO / "desktop" / "LexWolf.Tests" / "LexWolf.Tests.csproj"


def _dotnet_ok() -> bool:
    return shutil.which("dotnet") is not None and _TEST_PROJECT.exists()


@pytest.mark.skipif(not _dotnet_ok(), reason="dotnet SDK oder LexWolf.Tests.csproj nicht verfügbar")
def test_mandant_dropdown_regressionstests_gruen():
    result = subprocess.run(
        ["dotnet", "test", str(_TEST_PROJECT), "--filter", "FullyQualifiedName~MandantFilterTests"],
        capture_output=True, text=True, timeout=180,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "Failed:     0" in result.stdout
