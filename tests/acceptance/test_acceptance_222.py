import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_RENDERER_CS = os.path.join(_DESKTOP, "Services", "ChatHtmlRenderer.cs")
_MAINWINDOW_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def test_chathtmlrenderer_datei_existiert():
    assert os.path.isfile(_RENDERER_CS), \
        "Services/ChatHtmlRenderer.cs fehlt noch — Implementierung von T#222 aussteht"


def test_renderer_konvertiert_absaetze_listen_codebloecke_links():
    src = open(_RENDERER_CS, encoding="utf-8").read()
    for marker in ("<p", "<li", "<code", "<a "):
        assert marker in src, \
            f"ChatHtmlRenderer erzeugt kein '{marker}' — Absaetze/Listen/Code/Links fehlen"
    assert re.search(r"string\s+\w+\s*\([^)]*string\s+\w+", src), \
        "Kein string->HTML Konvertierungs-Einstiegspunkt gefunden"


def test_mainwindow_nutzt_chathtmlrenderer_fuer_ki_antworten():
    src = open(_MAINWINDOW_CS, encoding="utf-8").read()
    assert "ChatHtmlRenderer" in src, \
        "MainWindow.xaml.cs bindet ChatHtmlRenderer nicht ein — KI-Antworten bleiben Rohtext"
    assert "ChatWebView" in src, \
        "ChatWebView (WebView2-Grundgeruest) wird nicht mehr fuer die Chat-Ausgabe verwendet"
