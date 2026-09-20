import os
import re

DESKTOP_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'desktop')
CS = os.path.join(DESKTOP_DIR, 'MainWindow.xaml.cs')
XAML = os.path.join(DESKTOP_DIR, 'MainWindow.xaml')


def _cs():
    return open(CS, encoding='utf-8').read()


def _all_src():
    return _cs() + open(XAML, encoding='utf-8').read()


def test_statuschanged_handler_reagiert_auf_scanning():
    src = _cs()
    assert 'AnalyseStatus.Scanning' in src, \
        "StatusChanged-Handler (bzw. Aufrufer) muss auf AnalyseStatus.Scanning reagieren"


def test_wird_analysiert_text_am_zusammenfassung_button():
    assert 'wird analysiert' in _all_src(), \
        "Waehrend Scanning soll der Zusammenfassung-Button/Bereich 'wird analysiert' anzeigen"


def test_zusammenfassungbtn_deaktiviert_waehrend_scanning():
    src = _cs()
    assert re.search(r'ZusammenfassungBtn\.IsEnabled\s*=\s*false', src, re.I), \
        "ZusammenfassungBtn muss waehrend Scanning deaktiviert werden, sonst doppelter LLM-Call moeglich"


def test_animation_endet_bei_ready():
    src = _cs()
    assert re.search(r'AnalyseStatus\.Ready', src), \
        "Handler muss auf AnalyseStatus.Ready reagieren und Animation/Deaktivierung wieder aufheben"
