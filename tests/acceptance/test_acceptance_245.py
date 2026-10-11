import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandanten_dropdown_nicht_leer_beim_oeffnen():
    """T#245 (Duplikat von T#239): Mandanten-Dropdown in der Mandanten-Liste
    darf beim Oeffnen des Dialogs (leerer Filtertext, Klick auf das Dropdown)
    nicht leer bleiben, wenn Mandanten in der lokalen DB vorhanden sind."""
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

    assert "_mandanten" in leerer_filter_zweig and "MandantBox.Items.Add" in leerer_filter_zweig, (
        "T#245: Mandanten-Dropdown zeigt beim Oeffnen des Dialogs (leerer "
        "Filtertext) keine Eintraege aus der geladenen Mandantenliste an."
    )
