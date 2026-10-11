import os
import subprocess

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS_MAIN = os.path.join(_DESKTOP, "MainWindow.xaml.cs")
_CS_CHECKER = os.path.join(_DESKTOP, "Services", "UpdateChecker.cs")


def test_update_fortschritt_nutzt_hauptfenster_statusleiste():
    cs = open(_CS_MAIN, encoding="utf-8").read()
    assert "IProgress<double>" in cs or "Progress<double>" in cs, (
        "Update-Fortschritt wird nicht über ein IProgress<double>-Callback ermittelt"
    )
    assert "ProgressBar.Value" in cs and "SetProgressBusy" in cs, (
        "Update-Prozess nutzt nicht die bestehende Hauptfenster-Fortschrittsleiste"
    )


def test_update_fortschritt_wird_ueber_dispatcher_aktualisiert():
    cs = open(_CS_MAIN, encoding="utf-8").read()
    assert "Dispatcher.Invoke" in cs, "UI-Updates laufen nicht über den Dispatcher (Thread-Sicherheit)"


def test_update_dialog_blockiert_waehrend_laufendem_update():
    cs = open(_CS_MAIN, encoding="utf-8").read()
    # Während Download/Installation darf das Fenster nicht frei bedienbar bleiben
    # (z.B. per IsEnabled=false auf dem Fenster/einem Overlay, oder Closing-Guard).
    assert "IsEnabled = false" in cs and ("this.IsEnabled" in cs or "Window.IsEnabled" in cs), (
        "Hauptfenster wird während des Updates nicht blockiert (kein modales Verhalten)"
    )


def test_update_fehler_zeigt_aussagekraeftige_meldung_und_stoppt_fortschritt():
    cs = open(_CS_MAIN, encoding="utf-8").read()
    assert "Update fehlgeschlagen" in cs, "Keine aussagekräftige Fehlermeldung beim Update-Fehler"
    assert "SetProgressBusy(false)" in cs, "Fortschrittsanzeige wird bei Fehlern nicht beendet"


def test_downloadinstaller_meldet_fortschritt_0_bis_100():
    cs = open(_CS_CHECKER, encoding="utf-8").read()
    assert "progress?.Report" in cs, "Download meldet keinen Fortschritt über IProgress<double>"


def test_desktop_projekt_kompiliert():
    csproj = os.path.join(_DESKTOP, "LexWolf.csproj")
    result = subprocess.run(
        ["dotnet", "build", csproj, "-c", "Release", "--nologo", "-p:EnableWindowsTargeting=true"],
        capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, f"Build fehlgeschlagen:\n{result.stdout}\n{result.stderr}"
