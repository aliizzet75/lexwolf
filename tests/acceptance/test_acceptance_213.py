import os
import re
import subprocess

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_LOCALDB_CS = os.path.join(_DESKTOP, "Database", "LocalDb.cs")
_TEST_PROJ = os.path.join(_DESKTOP, "LexWolf.Tests", "LexWolf.Tests.csproj")


def test_tabelle_notizen_in_initialize():
    src = open(_LOCALDB_CS, encoding="utf-8").read()
    assert re.search(r"CREATE TABLE IF NOT EXISTS notizen", src), \
        "notizen wird nicht per IF NOT EXISTS angelegt"
    for spalte in ["mandant_id", "titel_kurz", "text", "erstellt", "geaendert"]:
        assert spalte in src, f"Spalte '{spalte}' fehlt in notizen"


def test_methoden_vorhanden():
    src = open(_LOCALDB_CS, encoding="utf-8").read()
    assert "InsertNotiz" in src, "InsertNotiz() fehlt in LocalDb.cs"
    assert "UpdateNotiz" in src, "UpdateNotiz() fehlt in LocalDb.cs"
    assert "GetNotizen" in src, "GetNotizen() fehlt in LocalDb.cs"


def test_dotnet_test_notizen_gruen():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ, "--filter", "FullyQualifiedName~Notiz"],
        cwd=_DESKTOP, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test Notiz schlaegt fehl:\n"
        f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
