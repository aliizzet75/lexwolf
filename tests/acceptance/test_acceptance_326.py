"""T#326: Alle Dialoge auf dieselben Theme-Variablen (Lw*-DynamicResource-Keys) umstellen."""
import glob
import os
import re

_DIALOGS = os.path.join(os.path.dirname(__file__), "..", "..", "desktop", "Dialogs")
# Bewusst NUR Text-/Hintergrund-/Rahmenfarben, keine Status-/Semantikfarben (grün/rot/gelb/blau).
_THEME_HEX = ["#0d1117", "#161b22", "#21262d", "#30363d", "#c9d1d9", "#8b949e", "#e6edf3"]
_SCHRIFTSATZ_CS = os.path.join(_DIALOGS, "SchriftsatzDialog.xaml.cs")


def _read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def test_dialog_xaml_hat_keine_literalen_theme_hexwerte_mehr():
    treffer = []
    for pfad in sorted(glob.glob(os.path.join(_DIALOGS, "*.xaml"))):
        inhalt = _read(pfad)
        for hex_wert in _THEME_HEX:
            if hex_wert in inhalt:
                treffer.append(f"{os.path.basename(pfad)}: {hex_wert}")
    assert not treffer, (
        "Noch literale Text-/Hintergrund-/Rahmenfarben statt DynamicResource gefunden:\n"
        + "\n".join(treffer)
    )


def test_schriftsatzdialog_codebehind_nutzt_theme_ressourcen_statt_literalen_rgb():
    cs = _read(_SCHRIFTSATZ_CS)
    start = re.search(r"private UIElement BuildAnonymisiertRow\(", cs)
    ende = re.search(r"private UIElement BuildRechercheRow\(.*?\n    \}", cs, re.DOTALL)
    assert start and ende, "BuildAnonymisiertRow/BuildRechercheRow nicht gefunden."
    block = cs[start.start():ende.end()]
    # Hintergrund/Rahmen/Muted-Text (#0d1117, #30363d, #a8b3bd) dürfen nicht mehr literal
    # per Color.FromRgb hartcodiert sein, sondern müssen aus Application.Current.Resources kommen.
    for literal in ["0x0d, 0x11, 0x17", "0x30, 0x36, 0x3d", "0xa8, 0xb3, 0xbd"]:
        assert literal not in block, (
            f"BuildRechercheRow/BuildAnonymisiertRow hartcodiert noch {literal} statt "
            "Application.Current.Resources[...] zu nutzen."
        )
    assert "Application.Current.Resources[" in block, (
        "BuildRechercheRow/BuildAnonymisiertRow liest keine Theme-Ressourcen aus Application.Current.Resources."
    )
