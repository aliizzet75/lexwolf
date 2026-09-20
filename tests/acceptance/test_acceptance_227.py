import os
import re
import subprocess

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_LOCALDB_CS = os.path.join(_DESKTOP, "Database", "LocalDb.cs")
_XAML = os.path.join(_DESKTOP, "Dialogs", "NotizenDialog.xaml")
_CS = os.path.join(_DESKTOP, "Dialogs", "NotizenDialog.xaml.cs")
_TEST_PROJ = os.path.join(_DESKTOP, "LexWolf.Tests", "LexWolf.Tests.csproj")


def test_deletenotiz_methode_vorhanden():
    src = open(_LOCALDB_CS, encoding="utf-8").read()
    assert "DeleteNotiz" in src, "DeleteNotiz() fehlt noch in LocalDb.cs — Implementierung von T#227 aussteht"
    assert re.search(r"DELETE\s+FROM\s+notizen", src, re.IGNORECASE), \
        "DeleteNotiz() loescht offenbar nicht per DELETE FROM notizen"


def test_xaml_hat_loeschen_button():
    xaml = open(_XAML, encoding="utf-8").read()
    assert re.search(r"L(ö|oe)schen", xaml), \
        "Kein Loeschen-Button/-Element im NotizenDialog.xaml gefunden"


def test_codebehind_ruft_deletenotiz_nur_nach_bestaetigung():
    cs = open(_CS, encoding="utf-8").read()
    assert "DeleteNotiz" in cs, "NotizenDialog.xaml.cs ruft LocalDb.DeleteNotiz() nicht auf"
    match = re.search(r"(private\s+\S+\s+\w*L(ö|oe)sch\w*\s*\([^)]*\)\s*\{.*?\n    \})", cs, re.DOTALL)
    assert match, "Kein erkennbarer Loeschen-Handler in NotizenDialog.xaml.cs gefunden"
    handler = match.group(1)
    assert "MessageBox" in handler, \
        "Loeschen-Handler zeigt keine Bestaetigung (MessageBox) an — versehentliches Loeschen waere moeglich"
    assert "DeleteNotiz" in handler, "Bestaetigter Loeschen-Handler ruft DeleteNotiz() nicht auf"


def test_dotnet_test_notiz_loeschen_gruen():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ, "--filter", "FullyQualifiedName~Notiz"],
        cwd=_DESKTOP, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test Notiz schlaegt fehl:\n{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
