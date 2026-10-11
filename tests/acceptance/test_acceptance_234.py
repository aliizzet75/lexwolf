import subprocess

BASE = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/desktop"
XAML = f"{BASE}/Dialogs/FeatureWunschDialog.xaml"
CS = f"{BASE}/Dialogs/FeatureWunschDialog.xaml.cs"
TEST_PROJ = f"{BASE}/LexWolf.Tests/LexWolf.Tests.csproj"


def test_button_feature_wunsch_verlauf_leeren_vorhanden():
    xaml = open(XAML, encoding="utf-8").read()
    assert "leeren" in xaml.lower(), "Button 'Feature-Wunsch-Verlauf leeren' fehlt im Dialog-XAML"


def test_sicherheitsabfrage_text_und_clear_logik_vorhanden():
    cs = open(CS, encoding="utf-8").read()
    assert "Möchten Sie alle bisherigen Feature-Wünsche wirklich löschen?" in cs, (
        "Sicherheitsabfrage-Text fehlt im Code-Behind"
    )
    assert "ClearFeatureWunschHistory" in cs, "Aufruf der Clear-Methode fehlt im Dialog"


def test_localdb_clear_feature_wunsch_history_ist_gruen():
    result = subprocess.run(
        ["dotnet", "test", TEST_PROJ, "--filter", "FullyQualifiedName~LocalDbFeatureWunschTests"],
        cwd=BASE, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test schlägt fehl (erwartet, solange Task #234 nicht implementiert ist):\n"
        f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
