import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../backend"))

ROUTE_FILE = os.path.join(os.path.dirname(__file__), "../../backend/api/ner.py")


def _make_client():
    from fastapi.testclient import TestClient
    from fastapi import FastAPI
    from api.ner import router
    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


def test_adjacent_per_entities_merged_to_full_name():
    resp = _make_client().post("/ner", json={"text": "Ali Izzet Erkol wohnt in Berlin."})
    assert resp.status_code == 200
    entities = [e for e in resp.json()["entities"] if e["label"] == "PER"]
    assert len(entities) == 1, f"Erwartet genau eine PER-Entity, erhalten: {entities}"
    assert entities[0]["text"] == "Ali Izzet Erkol"
    assert entities[0]["start"] == 0
    assert entities[0]["end"] == len("Ali Izzet Erkol")


def test_unrelated_per_entities_stay_separate():
    resp = _make_client().post(
        "/ner", json={"text": "Ali Izzet Erkol traf sich mit Maria Schmidt."}
    )
    assert resp.status_code == 200
    names = {e["text"] for e in resp.json()["entities"] if e["label"] == "PER"}
    assert "Ali Izzet Erkol" in names
    assert "Maria Schmidt" in names
