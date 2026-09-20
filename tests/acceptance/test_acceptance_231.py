import os
import re
import subprocess

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_XAML = os.path.join(_DESKTOP, "MainWindow.xaml")
_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def test_testknopf_button_nicht_im_hauptfenster_xaml():
    xaml = open(_XAML, encoding="utf-8").read()
    match = re.search(r'<Button[^>]*Content="[^"]*Testknopf[^"]*"[^>]*/?>', xaml)
    assert match is None, "Testknopf-Button ist noch in MainWindow.xaml vorhanden"
    assert "TestknopfBtn" not in xaml, "Referenz 'TestknopfBtn' ist noch in MainWindow.xaml vorhanden"


def test_testknopf_handler_und_popup_text_entfernt():
    cs = open(_CS, encoding="utf-8").read()
    assert "OnTestknopfClick" not in cs, "Click-Handler 'OnTestknopfClick' ist noch in MainWindow.xaml.cs vorhanden"
    assert "Hallo vom Anwalts-Feedback-Test" not in cs, "Popup-Text des Testknopfs ist noch im Code vorhanden"


def test_hauptfenster_projekt_kompiliert_ohne_testknopf():
    csproj = os.path.join(_DESKTOP, "LexWolf.csproj")
    result = subprocess.run(
        ["dotnet", "build", csproj, "-c", "Release", "--nologo", "-p:EnableWindowsTargeting=true"],
        capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, f"Build fehlgeschlagen:\n{result.stdout}\n{result.stderr}"
