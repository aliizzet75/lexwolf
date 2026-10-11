import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandant_dropdown_zeigt_mandanten_bei_leerem_filtertext():
    cs = _read(_CS)
    m = re.search(r"void\s+ApplyMandantFilter\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert m, "ApplyMandantFilter nicht gefunden in MainWindow.xaml.cs"
    body = m.group(0)

    if_match = re.search(
        r"if\s*\(\s*string\.IsNullOrEmpty\(filter\)\s*\)\s*(\{.*?\}|[^\n]+;)\s*else",
        body, re.S,
    )
    assert if_match, "Erwarteter if/else-Zweig fuer leeren Filter nicht gefunden"
    leerer_filter_zweig = if_match.group(1)

    assert "_mandanten" in leerer_filter_zweig, (
        "Bug T#239: Bei leerem Filtertext (Dialog frisch geoeffnet, noch nichts "
        "getippt) werden dem Dropdown keine Eintraege aus _mandanten hinzugefuegt "
        "-- nur das Platzhalter-Item KeinMandantLabel wird gesetzt. Dadurch zeigt "
        "das Mandanten-Dropdown beim ersten Oeffnen keine Mandanten, obwohl "
        "welche in der lokalen DB vorhanden sind."
    )
