import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_XAML = os.path.join(_DESKTOP, "MainWindow.xaml")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")
_DB = os.path.join(_DESKTOP, "Database", "LocalDb.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandant_zusammenfassung_tabelle_existiert_mit_quellen_hash():
    db = _read(_DB)
    m = re.search(r'CREATE TABLE IF NOT EXISTS mandant_zusammenfassung.*?\)', db, re.S)
    assert m, "Tabelle 'mandant_zusammenfassung' fehlt in LocalDb.cs — T#219 nicht implementiert"
    tbl = m.group(0)
    for spalte in ("mandant_id", "quellen_hash", "erstellt"):
        assert spalte in tbl, f"Spalte '{spalte}' fehlt in mandant_zusammenfassung"


def test_xaml_hat_zusammenfassung_button():
    xaml = _read(_XAML)
    assert re.search(r'x:Name="ZusammenfassungBtn"', xaml), \
        "Kein Button 'ZusammenfassungBtn' im MainWindow.xaml gefunden"
    m = re.search(r'<Button[^>]*x:Name="ZusammenfassungBtn".*?/?>', xaml, re.S)
    assert m and 'Click="On' in m.group(0), "ZusammenfassungBtn hat keinen Click-Handler verdrahtet"


def test_click_handler_prueft_quellen_hash_vor_neugenerierung():
    cs = _read(_CS)
    m = re.search(r'void\s+On\w*Zusammenfassung\w*\s*\([^)]*\)\s*\{.*?\n    \}', cs, re.S)
    assert m, "Kein Click-Handler fuer ZusammenfassungBtn gefunden"
    handler = m.group(0)
    assert "quellen_hash" in handler or "QuellenHash" in handler or "Hash" in handler, (
        "Handler prueft keinen Content-Hash — kann veralteten Cache nicht von neuen "
        "Chats/Notizen/Dokumenten unterscheiden (DoD Task #219)"
    )
