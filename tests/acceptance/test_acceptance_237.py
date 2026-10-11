import os, sys
from unittest.mock import patch

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../backend"))
XAML_FILE = os.path.join(os.path.dirname(__file__), "../../desktop/Dialogs/FeatureWunschDialog.xaml")


def _make_client():
    from fastapi.testclient import TestClient
    from fastapi import FastAPI
    from api.feature_feedback import router
    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


def test_dialog_xaml_bietet_auswahl_feature_oder_bug():
    content = open(XAML_FILE, encoding="utf-8").read()
    assert "Fehler melden" in content, "Auswahloption 'Fehler melden' fehlt im Dialog"
    assert "Feature-Wunsch" in content

def test_bug_ticket_ohne_repro_steps_und_client_version_wird_abgelehnt():
    payload = {"summary": {"titel": "Absturz", "beschreibung": "App stuerzt beim Speichern ab"}, "typ": "bug"}
    with patch("api.feature_feedback.requests.post") as mock_post:
        resp = _make_client().post("/feature-feedback/confirm", json=payload)
        assert resp.status_code == 400, "Bug-Ticket ohne repro_steps/client_version sollte abgelehnt werden (400)"
        mock_post.assert_not_called()

def test_feature_wunsch_flow_bleibt_unveraendert_validiert():
    resp = _make_client().post("/feature-feedback/confirm", json={"summary": {"titel": "", "beschreibung": ""}})
    assert resp.status_code == 400, "Bestehende Validierung (titel/beschreibung Pflicht) muss weiterhin greifen"
