import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandant_dropdown_bleibt_nach_neuladen_offen_und_gefuellt():
    cs = _read(_CS)
    m = re.search(
        r"void\s+OnMandantTextChanged\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S
    )
    assert m, "OnMandantTextChanged nicht gefunden in MainWindow.xaml.cs"
    body = m.group(0)

    buggy_pattern = re.search(
        r"IsDropDownOpen\s*=\s*MandantBox\.Items\.Count\s*>\s*0\s*&&\s*"
        r"!string\.IsNullOrEmpty\(filter\)\s*;",
        body,
    )
    assert not buggy_pattern, (
        "Bug T#258: IsDropDownOpen wird unconditional in derselben Zeile wie "
        "die leere-Filter-Pruefung gesetzt. Beim Oeffnen des Dialogs bzw. "
        "Leeren des Suchfelds klappt das Dropdown dadurch sofort wieder zu, "
        "bevor der Nutzer es erneut anklicken kann -- es erscheint leer."
    )

    guarded = re.search(
        r"if\s*\(\s*!string\.IsNullOrEmpty\(filter\)\s*\)\s*\n\s*"
        r"MandantBox\.IsDropDownOpen\s*=\s*MandantBox\.Items\.Count\s*>\s*0\s*;",
        body,
    )
    assert guarded, (
        "Bug T#258: Das Dropdown soll nur beim aktiven Tippen (nicht-leerer "
        "Filter) auf-/zugeklappt werden. Bei leerem Filter darf die frisch "
        "befuellte Liste nicht durch ein hartes IsDropDownOpen=false wieder "
        "geschlossen werden."
    )
