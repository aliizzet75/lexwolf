import subprocess

BASE = "/data/.openclaw/workspace-codex/projects/lexwolf/desktop"
LOCALDB_CS = f"{BASE}/Database/LocalDb.cs"
MAINWINDOW_CS = f"{BASE}/MainWindow.xaml.cs"
FILETREENODE_CS = f"{BASE}/Models/FileTreeNode.cs"
TEST_PROJ = f"{BASE}/LexWolf.Tests/LexWolf.Tests.csproj"


def test_mandanten_query_projiziert_nur_id_und_name():
    cs = open(LOCALDB_CS, encoding="utf-8").read()
    assert "SELECT id, name FROM mandanten" in cs, (
        "GetMandanten() soll nur die für Liste/Dropdown benötigten Felder (id, name) laden"
    )


def test_filetree_baut_nicht_mehr_eager_alle_mandanten_rekursiv_auf():
    cs = open(MAINWINDOW_CS, encoding="utf-8").read()
    assert "LazyLoad" in cs or "OnFileTreeNodeExpanded" in cs or "IsExpanded" in cs, (
        "BuildFileTree/AddFileTreeChildren laufen aktuell synchron und rekursiv fuer ALLE "
        "Mandantenordner beim Start bzw. bei jedem neuen Mandanten (Dispatcher.Invoke) durch "
        "und bauen den kompletten Baum eager auf - kein Lazy Loading beim Aufklappen. "
        "Erwartet, solange Task #235 nicht implementiert ist."
    )


def test_mandanten_performance_regressionstest_ist_gruen():
    result = subprocess.run(
        ["dotnet", "test", TEST_PROJ, "--filter", "FullyQualifiedName~MandantenPerformanceTests"],
        cwd=BASE, capture_output=True, text=True, timeout=300,
    )
    assert "No test matches" not in result.stdout, (
        "Performance-Regressionstest (>=200 Mandanten, Baseline vs. optimiert) fehlt noch "
        "in LexWolf.Tests - erwartet, solange Task #235 nicht implementiert ist."
    )
    assert result.returncode == 0, (
        f"dotnet test schlaegt fehl:\n{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
