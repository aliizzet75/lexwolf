import os
import re
import subprocess

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_LOCALDB_CS = os.path.join(_DESKTOP, "Database", "LocalDb.cs")
_TEST_PROJ = os.path.join(_DESKTOP, "LexWolf.Tests", "LexWolf.Tests.csproj")


def test_tabelle_chat_zusammenfassungen_in_initialize():
    src = open(_LOCALDB_CS, encoding="utf-8").read()
    assert re.search(r"CREATE TABLE IF NOT EXISTS chat_zusammenfassungen", src), \
        "chat_zusammenfassungen wird nicht per IF NOT EXISTS angelegt"
    for spalte in ["mandant_id", "sitzung_start", "sitzung_ende", "zusammenfassung", "erstellt"]:
        assert spalte in src, f"Spalte '{spalte}' fehlt in chat_zusammenfassungen"


def test_chat_history_unveraendert_vorhanden():
    src = open(_LOCALDB_CS, encoding="utf-8").read()
    assert "CREATE TABLE IF NOT EXISTS chat_history" in src, \
        "chat_history-Tabelle wurde entfernt oder verändert — Volltext muss erhalten bleiben"
    assert "AddChatMessage" in src, "AddChatMessage() (Volltext-Insert) wurde entfernt"


def test_methoden_vorhanden():
    src = open(_LOCALDB_CS, encoding="utf-8").read()
    assert "InsertChatZusammenfassung" in src, "InsertChatZusammenfassung() fehlt in LocalDb.cs"
    assert "GetChatZusammenfassungen" in src, "GetChatZusammenfassungen() fehlt in LocalDb.cs"


def test_dotnet_test_chat_zusammenfassung_gruen():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ, "--filter", "FullyQualifiedName~ChatZusammenfassung"],
        cwd=_DESKTOP, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test ChatZusammenfassung schlaegt fehl:\n"
        f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
