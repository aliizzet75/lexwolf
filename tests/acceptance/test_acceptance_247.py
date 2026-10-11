import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_leeren_des_suchfelds_laedt_volle_mandantenliste_neu():
    cs = _read(_CS)
    m = re.search(
        r"void\s+OnMandantTextChanged\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S
    )
    assert m, "OnMandantTextChanged nicht gefunden in MainWindow.xaml.cs"
    body = m.group(0)

    empty_branch = re.search(
        r"if\s*\(\s*string\.IsNullOrEmpty\(filter\)\s*\)\s*(\{.*?\}|[^\n]+;)",
        body, re.S,
    )
    assert empty_branch, (
        "Bug T#247: OnMandantTextChanged unterscheidet beim Leeren des "
        "Suchfelds nicht explizit, ob zuvor serverseitig (SQL) gefiltert "
        "wurde. Dadurch bleibt nach Suche+Leeren nur die eingeschraenkte "
        "_mandanten-Liste sichtbar statt des vollen Bestands."
    )

    reload_call = re.search(r"LoadMandantenAsync\s*\(\s*\)", empty_branch.group(1))
    assert reload_call, (
        "Bug T#247: Beim Leeren des Suchfelds muss LoadMandantenAsync() ohne "
        "Filter-Parameter aufgerufen werden, um den vollen, ungefilterten "
        "Mandantenbestand neu zu laden -- statt nur lokal per "
        "ApplyMandantFilter ueber die ggf. bereits SQL-eingeschraenkte "
        "_mandanten-Liste zu filtern."
    )
