import os
import pytest

_DESKTOP = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/desktop"
_SERVICE = os.path.join(_DESKTOP, "Services", "MandantAnalyseService.cs")
_MAINWINDOW = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def _read(p):
    return open(p, encoding="utf-8").read()


def test_mandant_analyse_service_existiert():
    assert os.path.exists(_SERVICE), f"Datei fehlt: {_SERVICE}"


def test_analyse_status_enum_mit_idle_scanning_ready():
    if not os.path.exists(_SERVICE):
        pytest.skip("MandantAnalyseService.cs noch nicht erstellt")
    content = _read(_SERVICE)
    assert "enum" in content and "AnalyseStatus" in content, "AnalyseStatus-Enum fehlt"
    for wert in ("Idle", "Scanning", "Ready"):
        assert wert in content, f"Status '{wert}' fehlt im AnalyseStatus-Enum"


def test_scan_abbrechbar_ueber_cancellationtoken():
    if not os.path.exists(_SERVICE):
        pytest.skip("MandantAnalyseService.cs noch nicht erstellt")
    content = _read(_SERVICE)
    assert "CancellationTokenSource" in content, (
        "Kein CancellationTokenSource im Service — Wechsel des Mandanten waehrend "
        "laufendem Scan kann den alten Scan nicht sauber abbrechen (siehe DoD Task #218)"
    )


def test_mainwindow_stoesst_hintergrund_scan_bei_mandant_wechsel_an():
    content = _read(_MAINWINDOW)
    assert "MandantAnalyseService" in content or "AnalyseStatus" in content, (
        "OnMandantChanged in MainWindow.xaml.cs loest noch keinen Hintergrund-Scan "
        "fuer Chat/Notizen/Dokumente aus (Task #218 noch nicht implementiert)"
    )
