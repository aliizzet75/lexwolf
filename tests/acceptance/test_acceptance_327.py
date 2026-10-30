"""T#327: ChatHtmlRenderer (WebView2-HTML) theme-abhängig statt fest dunkel.
Rein statisch (kein dotnet-Runner in der Sandbox)."""
import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_RENDERER = os.path.join(_DESKTOP, "Services", "ChatHtmlRenderer.cs")
_APP_CS = os.path.join(_DESKTOP, "App.xaml.cs")
_MAINWINDOW = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def test_chathtmlrenderer_hat_settheme_methode():
    renderer = _read(_RENDERER)
    assert re.search(r"SetTheme\s*\(\s*bool\s+\w+\s*\)", renderer), \
        "ChatHtmlRenderer.SetTheme(bool dunkel) fehlt (gleiches Muster wie SetOutputFont)."


def test_getchatcss_nutzt_kein_festes_dunkel_hex_mehr():
    renderer = _read(_RENDERER)
    css_start = renderer.index("GetChatCss")
    css_block = renderer[css_start:css_start + 6000]
    assert "#0d1117" not in css_block, \
        "GetChatCss() enthält noch den fest codierten Dunkel-Hintergrund #0d1117."


def test_app_setzetheme_ruft_chathtmlrenderer_settheme_auf():
    app_cs = _read(_APP_CS)
    assert "ChatHtmlRenderer.SetTheme" in app_cs, \
        "App.SetzeTheme() ruft ChatHtmlRenderer.SetTheme(dunkel) nicht auf."


def test_refreshchatwebview_wird_nach_themewechsel_aufgerufen():
    mw = _read(_MAINWINDOW)
    idx = mw.index("App.SetzeTheme(_settings.DunkelModus)", mw.index("SettingsDialog wendet das Dunkel/Hell"))
    umgebung = mw[idx:idx + 400]
    assert "RefreshChatWebView" in umgebung, \
        "Nach dem Theme-Umschalten in den Einstellungen wird RefreshChatWebView() nicht aufgerufen."
