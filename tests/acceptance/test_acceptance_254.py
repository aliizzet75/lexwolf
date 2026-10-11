import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandant_dropdown_bleibt_beim_oeffnen_nicht_leer():
    cs = _read(_CS)

    apply_filter = re.search(r"void\s+ApplyMandantFilter\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert apply_filter, "ApplyMandantFilter nicht gefunden in MainWindow.xaml.cs"
    assert "_mandanten" in apply_filter.group(0), (
        "T#254: Bei leerem Filtertext werden dem Dropdown keine Eintraege aus "
        "_mandanten hinzugefuegt -- das Mandanten-Dropdown bleibt beim Oeffnen leer."
    )

    text_changed = re.search(r"void\s+OnMandantTextChanged\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert text_changed, "OnMandantTextChanged nicht gefunden in MainWindow.xaml.cs"
    body = text_changed.group(0)
    assert re.search(r"if\s*\(\s*!string\.IsNullOrEmpty\(filter\)\s*\)\s*\n?\s*MandantBox\.IsDropDownOpen", body), (
        "T#254: IsDropDownOpen wird auch bei leerem Filter unbedingt gesetzt -- "
        "das frisch befuellte Dropdown klappt sofort wieder zu und erscheint "
        "beim naechsten Klick faelschlich als leer."
    )
