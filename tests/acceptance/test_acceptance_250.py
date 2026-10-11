import os

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
_DESKTOP = os.path.join(_ROOT, "desktop")
_XAML = os.path.join(_DESKTOP, "MainWindow.xaml")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_testbereich_button_ist_im_desktop_client_sichtbar():
    """DoD T#250 (Duplikat von T#238/T#243): 'Testbereich'-UI muss im
    Desktop-Client sichtbar und nutzbar sein/bleiben."""
    xaml = _read(_XAML)
    assert 'x:Name="TestbereichBtn"' in xaml, "TestbereichBtn fehlt in MainWindow.xaml"
    assert 'Click="OnTestbereichClick"' in xaml, "Click-Handler fuer Testbereich fehlt"


def test_testbereich_handler_bleibt_funktionsfaehig():
    """DoD T#250: bestehende Funktion (Handler aus T#238/T#243) bleibt
    unveraendert funktionsfaehig -- Version 9.9.9 muss weiter angezeigt werden."""
    cs = _read(_CS)
    assert "private void OnTestbereichClick" in cs, "OnTestbereichClick fehlt in MainWindow.xaml.cs"
    assert "9.9.9" in cs, "Client-Version 9.9.9 wird im Testbereich-Dialog nicht mehr angezeigt"
