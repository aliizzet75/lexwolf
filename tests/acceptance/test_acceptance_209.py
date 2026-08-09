import os
import re

DESKTOP_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'desktop')
MAINWINDOW_XAML = os.path.join(DESKTOP_DIR, 'MainWindow.xaml')
MAINWINDOW_CS = os.path.join(DESKTOP_DIR, 'MainWindow.xaml.cs')
ICON = os.path.join(DESKTOP_DIR, 'Assets', 'lexwolf.ico')


def _onsend_body():
    src = open(MAINWINDOW_CS, encoding='utf-8').read()
    m = re.search(r'private async void OnSend\([^)]*\)\s*\{(.*?)\n    \}', src, re.S)
    assert m, "OnSend() nicht gefunden in MainWindow.xaml.cs"
    return m.group(1)


def test_icon_asset_vorhanden():
    assert os.path.exists(ICON), "lexwolf.ico fehlt unter desktop/Assets/"


def test_xaml_enthaelt_wolf_ladeanimation():
    xaml = open(MAINWINDOW_XAML, encoding='utf-8').read()
    assert 'lexwolf.ico' in xaml, \
        "Ladeanimation im XAML soll das vorhandene lexwolf.ico-Motiv nutzen"
    assert re.search(r'Storyboard', xaml), \
        "Keine WPF-Storyboard-Animation (Puls/Rotation) im XAML gefunden"


def test_onsend_startet_und_stoppt_animation_bei_erfolg_und_fehler():
    body = _onsend_body()
    assert not re.search(r'AddReasoning\("⏳"', body), \
        "Alter Text-Status ('⏳ Anfrage wird verarbeitet...') soll durch die Wolf-Animation ersetzt sein"
    assert re.search(r'\.Begin\(', body), \
        "OnSend muss die Storyboard-Animation beim Anfragestart starten (.Begin(...))"
    assert re.search(r'\.Stop\(', body), \
        "OnSend muss die Animation stoppen — sonst bleibt sie bei Erfolg/Fehler sichtbar hängen"

    try_idx = body.index('try')
    catch_idx = body.index('catch')
    finally_match = re.search(r'\bfinally\b', body)
    stop_positions = [m.start() for m in re.finditer(r'\.Stop\(', body)]
    assert any(p > catch_idx for p in stop_positions) or (finally_match and finally_match.start() > catch_idx), \
        "Animation muss auch im Fehlerfall (catch/finally) gestoppt werden"
