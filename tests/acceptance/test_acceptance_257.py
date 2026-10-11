import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_XAML = os.path.join(_DESKTOP, "MainWindow.xaml")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_kopieren_button_vorhanden_und_zunaechst_versteckt():
    xaml = _read(_XAML)
    btn = re.search(r'<Button\s+x:Name="KopierenBtn"[^>]*/?>', xaml, re.S)
    assert btn, "T#257: KopierenBtn fehlt in MainWindow.xaml (analog zu ZusammenfassungBtn/NotizenBtn)."
    tag = btn.group(0)
    assert 'Visibility="Collapsed"' in tag, "T#257: KopierenBtn muss standardmaessig Collapsed sein."
    assert re.search(r'Click="On\w*Kopier\w*Click"', tag), "T#257: KopierenBtn braucht einen OnKopierenClick-Handler."


def test_kopieren_button_wird_mit_notizenbtn_sichtbar_gemacht():
    cs = _read(_CS)
    assert re.search(r"KopierenBtn\.Visibility\s*=\s*Visibility\.Visible\s*;", cs), (
        "T#257: KopierenBtn wird nicht analog zu NotizenBtn/ZusammenfassungBtn sichtbar geschaltet, "
        "sobald eine Assistenten-Antwort vorliegt."
    )


def test_kopieren_click_handler_kopiert_klartext_in_zwischenablage():
    cs = _read(_CS)
    handler = re.search(r"void\s+On\w*Kopier\w*Click\s*\([^)]*\)\s*\{.*?\n    \}", cs, re.S)
    assert handler, "T#257: OnKopierenClick-Methode nicht in MainWindow.xaml.cs gefunden."
    assert "Clipboard.SetText" in handler.group(0), "T#257: Handler muss Clipboard.SetText verwenden."
