import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandanten_dropdown_bei_dialog_oeffnen_und_klick_nicht_leer():
    """T#261 ("Dropdown leer"): erneuter Report von "Dialog oeffnen ->
    Dropdown klicken" mit leerem Mandanten-Dropdown. Gleiches Symptom wie
    T#239/#247/#258/#260. Prueft end-to-end, dass ApplyMandantFilter bei
    leerem Filtertext die geladenen Mandanten in MandantBox.Items eintraegt
    und das Dropdown dabei nicht sofort wieder zugeklappt wird."""
    cs = _read(_CS)

    m = re.search(r"void\s+ApplyMandantFilter\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert m, "ApplyMandantFilter nicht gefunden in MainWindow.xaml.cs"
    body = m.group(0)

    assert "MandantBox.Items.Clear()" in body
    populated = re.search(
        r"if\s*\(\s*string\.IsNullOrEmpty\(filter\)\s*\)\s*\{.*?"
        r"_mandanten\.Select\(m\s*=>\s*m\.Name\).*?MandantBox\.Items\.Add\(name\)",
        body, re.S,
    )
    assert populated, (
        "Bug T#261: Bei leerem Filter werden die geladenen Mandanten aus "
        "_mandanten nicht in MandantBox.Items uebernommen -- das Dropdown "
        "erscheint beim Anklicken leer."
    )

    otc = re.search(r"void\s+OnMandantTextChanged\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert otc, "OnMandantTextChanged nicht gefunden in MainWindow.xaml.cs"
    assert not re.search(
        r"IsDropDownOpen\s*=\s*MandantBox\.Items\.Count\s*>\s*0\s*&&\s*"
        r"!string\.IsNullOrEmpty\(filter\)\s*;",
        otc.group(0),
    ), "Bug T#261: hartes IsDropDownOpen=false klappt frisch befuelltes Dropdown sofort wieder zu."
