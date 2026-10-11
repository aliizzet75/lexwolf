import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandant_dropdown_bleibt_nicht_leer_beim_oeffnen():
    """T#240 (Anwalts-Feedback, Client v1.2.3): Dialog oeffnen -> Mandanten-
    Dropdown klicken zeigt keine Eintraege. Reproduziert denselben Symptom-
    bereich wie T#239 (ApplyMandantFilter in MainWindow.xaml.cs)."""
    cs = _read(_CS)

    assert "MandantBox" in cs, "MandantBox-Steuerelement nicht gefunden"

    m = re.search(r"void\s+ApplyMandantFilter\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert m, "ApplyMandantFilter nicht gefunden in MainWindow.xaml.cs"
    body = m.group(0)

    if_match = re.search(
        r"if\s*\(\s*string\.IsNullOrEmpty\(filter\)\s*\)\s*(\{.*?\}|[^\n]+;)\s*else",
        body, re.S,
    )
    assert if_match, "Erwarteter if/else-Zweig fuer leeren Filter nicht gefunden"

    assert "_mandanten" in if_match.group(1), (
        "T#240: Beim Oeffnen des Dropdowns (leerer Filtertext) werden dem "
        "Dropdown keine Eintraege aus der geladenen _mandanten-Liste "
        "hinzugefuegt -- nur das Platzhalter-Item wird gesetzt."
    )
