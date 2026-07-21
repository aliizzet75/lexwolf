"""Acceptance-Test T#184: WPF .NET 10 Scaffold unter tools/Anonymisierer/"""
import os
import pytest

BASE = os.path.join(os.path.dirname(__file__), '../../tools/Anonymisierer')
CSPROJ = os.path.join(BASE, 'Anonymisierer.csproj')
XAML = os.path.join(BASE, 'MainWindow.xaml')


def test_csproj_exists():
    assert os.path.isfile(CSPROJ), f"Projektdatei fehlt: {CSPROJ}"


def test_csproj_target_framework():
    content = open(CSPROJ).read()
    assert 'net10.0-windows' in content, "csproj muss net10.0-windows als TargetFramework enthalten"


def test_csproj_usewpf():
    content = open(CSPROJ).read()
    assert '<UseWPF>true</UseWPF>' in content, "csproj muss <UseWPF>true</UseWPF> enthalten"


def test_csproj_nugets():
    content = open(CSPROJ).read()
    assert 'DocumentFormat.OpenXml' in content, "NuGet DocumentFormat.OpenXml fehlt"
    assert 'iText7' in content, "NuGet iText7 fehlt (PdfPig ist read-only, laut Task KEIN PdfPig)"
    assert 'MimeKit' in content, "NuGet MimeKit fehlt"
    assert 'DiffPlex' in content, "NuGet DiffPlex fehlt"
    assert 'PdfPig' not in content, "PdfPig ist verboten (read-only, Task sagt KEIN PdfPig)"


def test_mainwindow_xaml_exists():
    assert os.path.isfile(XAML), f"MainWindow.xaml fehlt: {XAML}"


def test_mainwindow_has_dockpanel():
    content = open(XAML).read()
    assert 'DockPanel' in content, "MainWindow.xaml muss ein DockPanel (Top-Bar) enthalten"


def test_mainwindow_has_splitpane():
    content = open(XAML).read()
    has_split = 'Grid' in content or 'GridSplitter' in content or 'SplitPane' in content
    assert has_split, "MainWindow.xaml muss einen SplitPane/Grid mit Splitter enthalten"
