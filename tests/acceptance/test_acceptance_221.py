import os
import subprocess

_DESKTOP = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/desktop"
_CSPROJ = os.path.join(_DESKTOP, "LexWolf.csproj")
_XAML = os.path.join(_DESKTOP, "MainWindow.xaml")
_XAML_CS = os.path.join(_DESKTOP, "MainWindow.xaml.cs")
_APP_CS = os.path.join(_DESKTOP, "App.xaml.cs")


def test_webview2_paket_referenziert():
    src = open(_CSPROJ, encoding="utf-8").read()
    assert "Microsoft.Web.WebView2" in src, "WebView2-NuGet-Paket fehlt in LexWolf.csproj"


def test_webview2_control_im_chat_bereich():
    src = open(_XAML, encoding="utf-8").read()
    assert "WebView2" in src, "Kein WebView2-Control in MainWindow.xaml gefunden"


def test_runtime_check_ohne_absturz():
    code = open(_XAML_CS, encoding="utf-8").read() + open(_APP_CS, encoding="utf-8").read()
    assert "WebView2RuntimeNotFoundException" in code or "GetAvailableBrowserVersionString" in code, (
        "Kein Runtime-Verfuegbarkeits-Check gefunden - fehlende Runtime wuerde zum Absturz fuehren"
    )


def test_release_build_mit_windows_targeting_erfolgreich():
    result = subprocess.run(
        ["dotnet", "build", "LexWolf.csproj", "-c", "Release", "-p:EnableWindowsTargeting=true"],
        cwd=_DESKTOP, capture_output=True, text=True, timeout=300,
    )
    assert result.returncode == 0, f"Build fehlgeschlagen:\n{result.stdout[-3000:]}\n{result.stderr[-2000:]}"
