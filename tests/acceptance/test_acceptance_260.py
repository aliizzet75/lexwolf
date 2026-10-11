import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandanten_dropdown_nicht_leer_bei_dialog_oeffnen_und_klick():
    """T#260 ("Verifikation238 Dropdown leer"): erneuter Report von "Dialog
    oeffnen -> Dropdown klicken zeigt leeres Dropdown". Gleiches Symptom wie
    T#239/#240/#241/#242/#244/#245/#246/#248/#249/#252/#254/#258/#259
    (ApplyMandantFilter in MainWindow.xaml.cs)."""
    cs = _read(_CS)

    m = re.search(r"void\s+ApplyMandantFilter\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert m, "ApplyMandantFilter nicht gefunden in MainWindow.xaml.cs"
    body = m.group(0)

    assert "MandantBox.Items.Clear()" in body, (
        "ApplyMandantFilter sollte die Items neu befuellen (Clear + Add)."
    )
    populated = re.search(
        r"if\s*\(\s*string\.IsNullOrEmpty\(filter\)\s*\)\s*\{.*?"
        r"_mandanten\.Select\(m\s*=>\s*m\.Name\).*?MandantBox\.Items\.Add\(name\)",
        body,
        re.S,
    )
    assert populated, (
        "Bug T#260: Bei leerem Filter (frisch geoeffnetes Dropdown) werden die "
        "geladenen Mandanten aus _mandanten nicht in MandantBox.Items uebernommen "
        "-- das Dropdown erscheint beim Anklicken leer."
    )
