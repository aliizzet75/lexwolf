import os
import subprocess

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS_RENDERER = os.path.join(_DESKTOP, "Services", "ChatHtmlRenderer.cs")
_CS_MAIN = os.path.join(_DESKTOP, "MainWindow.xaml.cs")


def test_chat_css_verbietet_textselektion_nicht():
    css = open(_CS_RENDERER, encoding="utf-8").read()
    assert "user-select:none" not in css.replace(" ", "").replace("\n", "")


def test_chat_webview_deaktiviert_kontextmenue_nicht():
    cs = open(_CS_MAIN, encoding="utf-8").read()
    assert "AreDefaultContextMenusEnabled = false" not in cs, (
        "Natives Kontextmenu (Kopieren) ist im WebView2-Chat deaktiviert"
    )
    assert "AreDevToolsEnabled = false" not in cs or "AreDefaultContextMenusEnabled" not in cs


def test_chat_bubbles_haben_kein_oncontextmenu_blocker():
    css_and_html_sources = open(_CS_RENDERER, encoding="utf-8").read()
    assert "oncontextmenu" not in css_and_html_sources.lower()
    assert "onselectstart" not in css_and_html_sources.lower()


def test_desktop_projekt_kompiliert():
    csproj = os.path.join(_DESKTOP, "LexWolf.csproj")
    result = subprocess.run(
        ["dotnet", "build", csproj, "-c", "Release", "--nologo", "-p:EnableWindowsTargeting=true"],
        capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, f"Build fehlgeschlagen:\n{result.stdout}\n{result.stderr}"
