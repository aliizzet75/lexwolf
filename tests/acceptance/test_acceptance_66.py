import pytest
import subprocess
from pathlib import Path

DESKTOP = Path('/data/.openclaw/workspace-codex/projects/lexwolf/desktop')
PROJ_FILE = DESKTOP / 'LexWolf.csproj'
XAML_FILE = DESKTOP / 'MainWindow.xaml'


def test_desktop_verzeichnis_existiert():
    assert DESKTOP.exists(), f'desktop/ fehlt: {DESKTOP}'


def test_csproj_existiert():
    assert PROJ_FILE.exists(), f'LexWolf.csproj fehlt: {PROJ_FILE}'


def test_mainwindow_xaml_existiert():
    assert XAML_FILE.exists(), f'MainWindow.xaml fehlt: {XAML_FILE}'


def test_fenstertitel_lexwolf():
    txt = XAML_FILE.read_text()
    assert 'LexWolf' in txt, 'Fenster-Titel "LexWolf" fehlt in MainWindow.xaml'


def test_drei_panel_layout_vorhanden():
    txt = XAML_FILE.read_text()
    panels = ['Denkprozess', 'Editor', 'Mandant']
    found = [p for p in panels if p in txt]
    assert len(found) >= 3, f'3-Panel-Layout unvollständig. Gefunden: {found}, erwartet: {panels}'


def test_dotnet_build_erfolgreich():
    result = subprocess.run(
        ['dotnet', 'build', str(PROJ_FILE), '--no-restore', '-v', 'q'],
        capture_output=True, text=True, timeout=120
    )
    assert result.returncode == 0, f'dotnet build fehlgeschlagen:\n{result.stdout}\n{result.stderr}'
