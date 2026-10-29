"""T#322: Mandant-geladen-Hinweis im Chat mit Zusammenfassung/Timeline-Aktionen."""
import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_MAINWINDOW = os.path.join(_DESKTOP, "MainWindow.xaml.cs")
_RENDERER = os.path.join(_DESKTOP, "Services", "ChatHtmlRenderer.cs")


def _read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def test_mandant_geladen_hinweis_mit_beiden_aktionen():
    renderer = _read(_RENDERER)
    m = re.search(r"geladen", renderer)
    assert m, "Kein 'Mandant ... geladen'-Hinweistext in ChatHtmlRenderer.cs gefunden."
    umgebung = renderer[max(0, m.start() - 600):m.end() + 600]
    assert "chat-action-button" in umgebung, "Hinweis nutzt nicht den chat-action-button-Mechanismus."
    assert "Zusammenfassung" in umgebung, "Aktion 'Zusammenfassung' fehlt im Mandant-geladen-Hinweis."
    assert "Timeline" in umgebung or "Zeitleiste" in umgebung, "Aktion 'Timeline' fehlt im Mandant-geladen-Hinweis."


def test_mandantwechsel_haeuft_hinweis_nicht_unbegrenzt_an():
    mw = _read(_MAINWINDOW)
    assert re.search(r"OnMandantChanged", mw), "OnMandantChanged nicht gefunden."
    # Erwartung: der vorherige Hinweis wird per Index ersetzt statt immer neu angehängt
    # (etabliertes Muster in dieser Datei, siehe _chatHistoryHtml[...] = ... bei Suchpanels).
    assert re.search(r"_chatHistoryHtml\[[^\]]+\]\s*=.*[Gg]eladen|[Mm]andantHinweisIndex", mw), \
        "Kein Hinweis darauf, dass der alte Mandant-geladen-Hinweis ersetzt statt angehängt wird."


def test_timeline_aktion_oeffnet_aktedialog_auf_zeitleiste():
    mw = _read(_MAINWINDOW)
    assert "AkteDialog" in mw and "Zeitleiste" in mw, \
        "Kein Hinweis, dass die Timeline-Aktion den AkteDialog auf dem Zeitleiste-Bereich öffnet."
