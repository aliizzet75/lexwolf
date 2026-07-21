"""Acceptance-Test T#185: Top-Bar FolderPicker + ProgressBar + FileEntry-Model"""
import os
import re
import pytest

DESKTOP = os.path.join(os.path.dirname(__file__), '../../desktop')
XAML = os.path.join(DESKTOP, 'MainWindow.xaml')
SERVICES = os.path.join(DESKTOP, 'Services')


def test_mainwindow_has_folder_button():
    content = open(XAML).read()
    has_btn = bool(re.search(r'<Button[^>]+(?:ScanFolder|FolderPicker|VerzeichnisBtn|ScanBtn)', content))
    assert has_btn, "MainWindow.xaml muss einen FolderPicker-Button (z.B. x:Name='ScanFolderBtn') in der Top-Bar enthalten"


def test_mainwindow_has_progressbar():
    content = open(XAML).read()
    assert '<ProgressBar' in content, "MainWindow.xaml muss ein <ProgressBar>-Element für den Scan-Fortschritt enthalten"


def test_file_entry_model_exists():
    candidates = [
        os.path.join(SERVICES, 'FileEntry.cs'),
        os.path.join(DESKTOP, 'Models', 'FileEntry.cs'),
        os.path.join(DESKTOP, 'FileEntry.cs'),
    ]
    found = next((p for p in candidates if os.path.isfile(p)), None)
    assert found, f"FileEntry.cs nicht gefunden; gesucht in: {candidates}"


def test_file_entry_has_required_properties():
    candidates = [
        os.path.join(SERVICES, 'FileEntry.cs'),
        os.path.join(DESKTOP, 'Models', 'FileEntry.cs'),
        os.path.join(DESKTOP, 'FileEntry.cs'),
    ]
    path = next((p for p in candidates if os.path.isfile(p)), None)
    assert path, "FileEntry.cs nicht gefunden"
    content = open(path).read()
    assert 'Pfad' in content or 'Path' in content, "FileEntry muss Pfad/Path-Property haben"
    assert 'Groesse' in content or 'Groesse' in content or 'Size' in content or 'Größe' in content, \
        "FileEntry muss Größe/Size-Property haben"
    assert 'MandantFolder' in content or 'Mandant' in content, \
        "FileEntry muss MandantFolder-Property haben (1. Ebene = Mandant)"
