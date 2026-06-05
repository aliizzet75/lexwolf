import pytest
import sys
from pathlib import Path

DESKTOP = Path("/data/.openclaw/workspace-codex/projects/lexwolf/desktop")
sys.path.insert(0, str(DESKTOP))

GUELTIGE_TYPEN = {"Gehaltsaenderung", "Fristablauf", "Scheidung", "Neuer_Fall", "Unbekannt"}


def test_modul_existiert():
    assert (DESKTOP / "ereignis_erkennung.py").exists(), "ereignis_erkennung.py fehlt im desktop-Ordner"


def test_klasse_und_methode_vorhanden():
    from ereignis_erkennung import EreignisErkenner
    erkenner = EreignisErkenner()
    assert callable(getattr(erkenner, "erkenne", None)), "EreignisErkenner.erkenne() nicht gefunden"


def test_erkenne_gibt_liste_zurueck():
    from ereignis_erkennung import EreignisErkenner
    result = EreignisErkenner().erkenne("Gehalt erhöht auf 4500 EUR", "Mueller")
    assert isinstance(result, list), "erkenne() muss eine Liste zurückgeben"


def test_ereignis_dict_struktur():
    from ereignis_erkennung import EreignisErkenner
    result = EreignisErkenner().erkenne("Gehalt erhöht auf 4500 EUR", "Mueller")
    assert len(result) >= 1, "erkenne() muss mindestens ein Ereignis zurückgeben"
    for e in result:
        assert "typ" in e and "mandant" in e and "details" in e, f"Pflichtfelder fehlen: {e}"
        assert e["typ"] in GUELTIGE_TYPEN, f"Ungültiger Typ: {e['typ']}"
        assert e["mandant"] == "Mueller", "mandant muss übergeben werden"


def test_fristablauf_erkannt():
    from ereignis_erkennung import EreignisErkenner
    result = EreignisErkenner().erkenne("Frist läuft am 15. Juni ab", "Schmidt")
    typen = [e["typ"] for e in result]
    assert "Fristablauf" in typen, f"Fristablauf nicht erkannt, gefunden: {typen}"


def test_leerer_text_gibt_unbekannt():
    from ereignis_erkennung import EreignisErkenner
    result = EreignisErkenner().erkenne("", "Test")
    assert isinstance(result, list), "erkenne() muss bei leerem Text eine Liste zurückgeben"
