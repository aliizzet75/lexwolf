import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_XAML = os.path.join(_DESKTOP, "MainWindow.xaml")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def test_testknopf_dauerhaft_sichtbar_in_hauptfenster():
    xaml = open(_XAML, encoding="utf-8").read()
    match = re.search(r'<Button[^>]*Content="[^"]*Testknopf[^"]*"[^>]*/?>', xaml)
    assert match, "Kein Button mit Beschriftung 'Testknopf' in MainWindow.xaml gefunden"
    button_tag = match.group(0)
    assert not re.search(r'Visibility="(Collapsed|Hidden)"', button_tag), \
        "Testknopf ist nicht dauerhaft sichtbar (Visibility=Collapsed/Hidden)"


def test_testknopf_click_handler_zeigt_exakten_popup_text():
    xaml = open(_XAML, encoding="utf-8").read()
    match = re.search(r'<Button[^>]*Content="[^"]*Testknopf[^"]*"[^>]*Click="(\w+)"', xaml)
    assert match, "Testknopf hat keinen Click-Handler in MainWindow.xaml"
    handler_name = match.group(1)

    cs = open(_CS, encoding="utf-8").read()
    handler_match = re.search(
        rf"private\s+\S+\s+{handler_name}\s*\([^)]*\)\s*\{{(.*?)\n    \}}", cs, re.DOTALL
    )
    assert handler_match, f"Handler '{handler_name}' nicht in MainWindow.xaml.cs gefunden"
    assert "Hallo vom Anwalts-Feedback-Test" in handler_match.group(1), \
        "Popup zeigt nicht exakt den Text 'Hallo vom Anwalts-Feedback-Test'"
