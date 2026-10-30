"""
T#325: Theme.cs + App.xaml: vollständige, kontrastsichere Farbpalette statt
nur 4 Kernfarben. Prüft rein statisch (kein dotnet-Runner im Sandbox):
- App.SetzeTheme() setzt ALLE bisher fest codierten Resource-Keys um
  (nicht mehr nur die 4 Kernfarben LwFensterHintergrund/LwVordergrund/
  LwOberflaeche/LwAkzent).
- App.xaml referenziert diese Keys jetzt per DynamicResource statt eines
  literalen Hex-Werts in den betroffenen Style-Settern (hier exemplarisch
  an LwButton/LwRaised geprüft).
"""
import os

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
if not os.path.isdir(_ROOT):
    _ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"

APP_XAML_CS = os.path.join(_ROOT, "desktop", "App.xaml.cs")
APP_XAML = os.path.join(_ROOT, "desktop", "App.xaml")

NEUE_KEYS = [
    "LwBg", "LwPanel", "LwRaised", "LwBorder", "LwText", "LwTextStrong",
    "LwMuted", "LwAccent", "LwRed", "LwOrange", "LwGreen", "LwBlue",
]


def test_setze_theme_setzt_alle_neuen_resource_keys():
    cs = open(APP_XAML_CS, encoding="utf-8").read()
    fehlend = [k for k in NEUE_KEYS if f'Resources["{k}"]' not in cs]
    assert not fehlend, f"SetzeTheme() setzt diese Keys nicht um: {fehlend}"


def test_lwbutton_style_referenziert_dynamicresource_statt_literalem_hex():
    xaml = open(APP_XAML, encoding="utf-8").read()
    block = xaml[xaml.index('x:Key="LwButton"'):xaml.index('x:Key="LwPrimaryButton"')]
    assert "#21262d" not in block, "LwButton nutzt noch literalen Hex-Wert statt DynamicResource"
    assert "{DynamicResource LwRaised}" in block
