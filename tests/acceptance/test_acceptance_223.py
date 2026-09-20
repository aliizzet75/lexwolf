import os
import subprocess

_DESKTOP = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/desktop"
_TEST_PROJ = os.path.join(_DESKTOP, "LexWolf.Tests", "LexWolf.Tests.csproj")
_RENDERER_CS = os.path.join(_DESKTOP, "Services", "ChatHtmlRenderer.cs")


def test_chathighlightingtests_vorhanden():
    src = open(os.path.join(_DESKTOP, "LexWolf.Tests", "ChatHighlightingTests.cs"), encoding="utf-8").read()
    for marker in ("highlight-frist", "highlight-paragraph", "highlight-empfehlung", "Keine_Falsch_Positiven"):
        assert marker in src, f"Regressionstest fuer Task #223 deckt '{marker}' nicht ab"


def test_renderer_hat_drei_hervorhebungs_kategorien():
    src = open(_RENDERER_CS, encoding="utf-8").read()
    for marker in ("highlight-frist", "highlight-paragraph", "highlight-empfehlung"):
        assert marker in src, (
            f"ChatHtmlRenderer.cs erkennt Kategorie '{marker}' noch nicht "
            f"(Implementierung von Task #223 aussteht)"
        )


def test_dotnet_test_chathighlighting_ist_gruen():
    result = subprocess.run(
        ["dotnet", "test", _TEST_PROJ, "--filter", "FullyQualifiedName~ChatHighlighting"],
        cwd=_DESKTOP, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, (
        f"dotnet test schlägt fehl (erwartet, solange Task #223 nicht implementiert ist):\n"
        f"{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
    )
