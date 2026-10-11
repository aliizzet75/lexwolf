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


def test_hintergrund_check_mit_fertigstellungs_toast_vorhanden():
    alle = _all_desktop_cs_and_xaml_text()
    assert "FeatureStatusChecker" in alle or "FeatureFertigstellungService" in alle, (
        "Periodischer Hintergrund-Service fuer Feature-Fertigstellung fehlt - erwartet, solange Task #236 nicht implementiert ist"
    )
    assert "Später erinnern" in alle and ("neu starten" in alle.lower()), (
        "Toast mit 'Client jetzt neu starten' / 'Später erinnern' fehlt - erwartet, solange Task #236 nicht implementiert ist"
    )
