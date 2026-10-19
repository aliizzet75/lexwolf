import os
import subprocess

_DESKTOP = os.path.join(os.path.dirname(__file__), "..", "..", "desktop")
_CS_LOCALDB = os.path.join(_DESKTOP, "Database", "LocalDb.cs")
_DIALOGS_DIR = os.path.join(_DESKTOP, "Dialogs")
_TEST_PROJ = os.path.join(_DESKTOP, "LexWolf.Tests", "LexWolf.Tests.csproj")


def _all_desktop_cs_and_xaml_text():
    chunks = []
    for root, _dirs, files in os.walk(_DESKTOP):
        if any(p in root for p in (os.sep + "bin", os.sep + "obj")):
            continue
        for f in files:
            if f.endswith((".cs", ".xaml")):
                chunks.append(open(os.path.join(root, f), encoding="utf-8", errors="ignore").read())
    return "\n".join(chunks)


def test_keine_regression_mandanten_projektion_aus_task_235():
    cs = open(_CS_LOCALDB, encoding="utf-8").read()
    assert "SELECT id, name FROM mandanten" in cs, (
        "Regression: GetMandanten()-Projektion aus Task #235 fehlt/wurde entfernt"
    )


def test_feature_wunsch_status_und_laufende_entwicklungen_uebersicht_vorhanden():
    alle = _all_desktop_cs_and_xaml_text()
    assert "LaufendeEntwicklungen" in alle or "Laufende Entwicklungen" in alle, (
        "Übersicht 'Laufende Entwicklungen' fehlt noch im Client - erwartet, solange Task #236 nicht implementiert ist"
    )
    assert "EigeneFeatureWuensche" in alle or "Eigene Feature-Wünsche" in alle, (
        "Übersicht 'Eigene Feature-Wünsche' mit Status fehlt noch im Client - erwartet, solange Task #236 nicht implementiert ist"
    )


def test_neuigkeiten_nur_ueber_update_dialog_kein_neustart_popup_pro_feature():
    # Der fruehere FeatureStatusChecker zeigte pro fertigem Milestone ein eigenes
    # "Jetzt neu starten?"-Popup -> Kette aus Popup+Neustart beim Anwalt, obwohl
    # ein Neustart ohne Update nichts Neues bringt. Neuigkeiten kommen jetzt
    # ausschliesslich gesammelt ueber den Update-Dialog (client_update.py notes).
    alle = _all_desktop_cs_and_xaml_text()
    assert "FeatureStatusChecker" not in alle, (
        "Regression: Feature-Fertigstellungs-Popup mit Neustart ist wieder im Client"
    )
    assert "update.Notes" in alle, (
        "Update-Dialog zeigt die Changelog-Notizen nicht mehr an"
    )
