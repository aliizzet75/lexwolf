import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")
_XAML = os.path.join(_DESKTOP, "MainWindow.xaml")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandantbox_wird_beim_start_und_dialogoeffnen_befuellt():
    xaml = _read(_XAML)
    assert 'x:Name="MandantBox"' in xaml, "MandantBox-ComboBox fehlt im MainWindow.xaml"

    cs = _read(_CS)
    assert "_ = LoadMandantenAsync();" in cs, (
        "T#263: Mandanten werden beim Start/Dialogoeffnen nicht geladen -- "
        "LoadMandantenAsync() wird nicht aufgerufen, wodurch das Mandanten-"
        "Dropdown beim Klick keine Eintraege zeigt."
    )

    m = re.search(r"void\s+ApplyMandantFilter\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert m, "ApplyMandantFilter nicht gefunden in MainWindow.xaml.cs"
    body = m.group(0)
    assert "_mandanten" in body and "MandantBox.Items.Add" in body, (
        "T#263: ApplyMandantFilter befuellt MandantBox.Items nicht aus der "
        "geladenen _mandanten-Liste -- Dropdown bleibt beim Oeffnen des Dialogs leer."
    )
