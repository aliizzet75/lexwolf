import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandanten_dropdown_bleibt_offen_nach_neuladen_bei_leerem_filter():
    """T#249 (weiteres Duplikat von T#239/T#242/T#244/T#245/T#248): Anwalt meldet
    'Dropdown zeigt keine Eintraege' nach 'Dialog oeffnen' + 'Dropdown klicken'.
    Regressionsschutz fuer den zuletzt gefixten Fall: wird das Suchfeld geleert
    und die volle Mandantenliste per LoadMandantenAsync() neu geladen, darf
    OnMandantTextChanged das gerade befuellte Dropdown nicht im selben Zug
    wieder unconditional schliessen (IsDropDownOpen=false), da es sonst beim
    naechsten Klick faelschlich leer wirkt."""
    cs = _read(_CS)
    m = re.search(r"void\s+OnMandantTextChanged\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert m, "OnMandantTextChanged nicht gefunden in MainWindow.xaml.cs"
    body = m.group(0)

    bad_pattern = re.search(
        r"MandantBox\.IsDropDownOpen\s*=\s*MandantBox\.Items\.Count\s*>\s*0\s*&&\s*!string\.IsNullOrEmpty\(filter\)\s*;",
        body,
    )
    assert not bad_pattern, (
        "T#249: OnMandantTextChanged schliesst das Dropdown unconditional per "
        "kombinierter Bedingung, sobald filter leer ist -- direkt nachdem die "
        "volle Mandantenliste neu geladen wurde. Das Dropdown muss bei leerem "
        "Filter unangetastet bleiben statt aktiv geschlossen zu werden."
    )

    guarded_open = re.search(
        r"if\s*\(\s*!string\.IsNullOrEmpty\(filter\)\s*\)\s*\n?\s*MandantBox\.IsDropDownOpen\s*=\s*MandantBox\.Items\.Count\s*>\s*0\s*;",
        body,
    )
    assert guarded_open, (
        "T#249: Erwartet, dass IsDropDownOpen nur beim Tippen (nicht-leerer "
        "Filter) aktiv gesetzt wird -- bei leerem Filter (Suchfeld geleert, "
        "Dialog frisch geoeffnet) darf das Dropdown nicht zwangsweise "
        "zugeklappt werden, sonst erscheint es beim naechsten Klick leer."
    )
