"""T#319: Chat-Kopfzeile + AktenLeiste zu einer kompakten Leiste zusammenfassen."""
import os
import re

_XAML = os.path.join(os.path.dirname(__file__), "..", "..", "desktop", "MainWindow.xaml")
ALLE_HANDLER = [
    "OnStatistikClick", "OnZusammenfassungClick", "OnKopierenClick", "OnNotizenClick",
    "OnDokumenteClick", "OnAktenstammClick", "OnSchriftsatzClick", "OnGespraechClick",
    "OnGespraecheAnzeigenClick", "OnRechnerClick", "OnSachstandClick", "OnFragebogenClick",
]


def _read():
    with open(_XAML, encoding="utf-8") as f:
        return f.read()

def test_akten_leiste_ist_keine_eigene_volle_zeile_mehr():
    m = re.search(r'<Border[^>]*x:Name="AktenLeiste"[^>]*>', _read())
    assert not m, "AktenLeiste existiert noch als eigene volle Werkzeugleisten-Zeile."

def test_overflow_menu_vorhanden():
    xaml = _read()
    m = re.search(r'<Button[^>]*Content="[^"]*⋯[^"]*"', xaml)
    assert m, "Kein '⋯'-Overflow-Button in der Chat-Kopfzeile gefunden."
    umgebung = xaml[m.end():m.end() + 800]
    assert "ContextMenu" in umgebung or "Popup" in umgebung, "Kein ContextMenu/Popup beim '⋯'-Button."

def test_alle_bisherigen_click_handler_bleiben_referenziert():
    fehlend = [h for h in ALLE_HANDLER if f'Click="{h}"' not in _read()]
    assert not fehlend, f"Click-Handler nicht mehr referenziert: {fehlend}"
