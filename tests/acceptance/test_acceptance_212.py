import os
import re

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_SERVICE_CS = os.path.join(_DESKTOP, "Services", "ChatSummaryService.cs")
_MAINWINDOW_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def test_chatsummaryservice_datei_existiert():
    assert os.path.isfile(_SERVICE_CS), \
        "Services/ChatSummaryService.cs fehlt noch — Implementierung von T#212 aussteht"


def test_service_ist_async_und_speichert_zusammenfassung():
    src = open(_SERVICE_CS, encoding="utf-8").read()
    assert re.search(r"async\s+Task", src), "Service enthält keine async Task-Methode"
    assert "InsertChatZusammenfassung" in src, \
        "Service speichert Ergebnis nicht über InsertChatZusammenfassung()"
    assert ".Result" not in src and ".Wait(" not in src, \
        "Service blockiert mit .Result/.Wait() statt await — würde den UI-Thread einfrieren"


def test_mainwindow_loest_zusammenfassung_bei_mandantenwechsel_aus():
    src = open(_MAINWINDOW_CS, encoding="utf-8").read()
    assert "ChatSummaryService" in src, \
        "MainWindow.xaml.cs bindet ChatSummaryService nicht ein"
    match = re.search(r"OnMandantChanged.*?\n    \}", src, re.DOTALL)
    assert match, "OnMandantChanged-Methode nicht gefunden"
    assert "ChatSummaryService" in match.group(0), \
        "Zusammenfassung wird nicht bei Mandant-Wechsel (OnMandantChanged) ausgelöst"
    assert ".Result" not in match.group(0) and ".Wait(" not in match.group(0), \
        "Blockierender Aufruf (.Result/.Wait()) in OnMandantChanged würde die UI einfrieren"
