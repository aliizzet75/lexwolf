import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_XAML = os.path.join(_DESKTOP, "MainWindow.xaml")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _xaml():
    return open(_XAML, encoding="utf-8").read()


def _cs():
    return open(_CS, encoding="utf-8").read()


def test_xaml_hat_notizen_button_analog_zu_unterhalt_button():
    xaml = _xaml()
    assert re.search(r'x:Name="NotizenBtn"', xaml), \
        "Kein Button 'NotizenBtn' im MainWindow.xaml gefunden — T#217 noch nicht implementiert"
    m = re.search(r'<Button[^>]*x:Name="NotizenBtn".*?/?>', xaml, re.S)
    assert m and 'Click="On' in m.group(0), "NotizenBtn hat keinen Click-Handler verdrahtet"


def test_notizen_button_nur_sichtbar_wenn_mandant_ausgewaehlt():
    cs = _cs()
    assert re.search(r'NotizenBtn\.Visibility\s*=\s*Visibility\.Collapsed', cs), \
        "NotizenBtn wird nicht auf Collapsed gesetzt, wenn kein Mandant aktiv ist"
    assert re.search(r'NotizenBtn\.Visibility\s*=\s*Visibility\.Visible', cs), \
        "NotizenBtn wird nicht auf Visible gesetzt, wenn ein Mandant ausgewaehlt wird"


def test_klick_oeffnet_notizendialog_mit_activem_mandanten():
    cs = _cs()
    m = re.search(r'void\s+On\w*Notizen\w*\s*\([^)]*\)\s*\{.*?\n    \}', cs, re.S)
    assert m, "Kein Click-Handler fuer den Notizen-Button gefunden"
    handler = m.group(0)
    assert "NotizenDialog" in handler, "Click-Handler oeffnet keinen NotizenDialog"
    assert "_activeMandantId" in handler, "NotizenDialog wird nicht mit _activeMandantId geoeffnet"
