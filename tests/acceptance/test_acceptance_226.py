import os
import pytest

_MAINWINDOW = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/desktop/MainWindow.xaml.cs"


def _read():
    return open(_MAINWINDOW, encoding="utf-8").read()


def test_mainwindow_existiert():
    assert os.path.exists(_MAINWINDOW), f"Datei fehlt: {_MAINWINDOW}"


def test_mandant_wechsel_laedt_chat_historie_aus_db():
    content = _read()
    assert "GetChatZusammenfassungen" in content and "GetChatHistorySeit" in content, (
        "OnMandantChanged laedt beim Mandant-Wechsel noch keine vorhandene "
        "Chat-Historie/Zusammenfassung aus der DB (Task #226 DoD 1)"
    )


def test_laden_blockiert_ui_thread_nicht():
    content = _read()
    assert "Task.Run" in content, (
        "Chat-Historie-Laden erfolgt nicht ueber Task.Run/Hintergrund-Task — "
        "UI koennte beim Mandant-Wechsel blockieren (Task #226 DoD 3, Vorbild T#218 ScanAsync)"
    )


def test_race_condition_schutz_bei_schnellem_wechsel():
    content = _read()
    assert "CancellationTokenSource" in content or "CancellationToken" in content, (
        "Kein CancellationToken-Schutz beim Chat-Historie-Laden gefunden — bei "
        "schnellem Mandant-Wechsel hintereinander koennte die Historie des falschen "
        "Mandanten angezeigt werden (Task #226 DoD 4, Vorbild MandantAnalyseService.ScanAsync)"
    )
