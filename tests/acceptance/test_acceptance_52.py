import pytest, os, sys
from unittest.mock import patch, AsyncMock

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../backend"))

ROUTE_FILE = "/data/.openclaw/workspace-codex/projects/lexwolf/backend/api/routes/search.py"

def route_available():
    return os.path.exists(ROUTE_FILE)

def _make_client():
    from fastapi.testclient import TestClient
    from fastapi import FastAPI
    from api.routes.search import router
    app = FastAPI()
    app.include_router(router)
    return TestClient(app)

@pytest.mark.skipif(not route_available(), reason="api/routes/search.py nicht vorhanden")
class TestSearchMode:
    def test_default_mode_schnell(self):
        with patch("services.react_engine.ReActEngine.run_loop", new_callable=AsyncMock, return_value={"aussagen": []}):
            resp = _make_client().get("/search?q=Kuendigung")
        assert resp.status_code == 200

    def test_invalid_mode_rejected(self):
        resp = _make_client().get("/search?q=BGB&mode=mittel")
        assert resp.status_code == 422

    def test_schnell_max_rounds_one(self):
        captured = {}
        async def fake_run_loop(self_ref, q):
            captured["max_rounds"] = getattr(self_ref, "max_rounds", None)
            captured["use_graph"] = getattr(self_ref, "use_graph", None)
            return {"aussagen": []}
        with patch("services.react_engine.ReActEngine.run_loop", fake_run_loop):
            _make_client().get("/search?q=test&mode=schnell")
        assert captured.get("max_rounds") == 1
        assert captured.get("use_graph") is False

    def test_tief_max_rounds_five_with_graph(self):
        captured = {}
        async def fake_run_loop(self_ref, q):
            captured["max_rounds"] = getattr(self_ref, "max_rounds", None)
            captured["use_graph"] = getattr(self_ref, "use_graph", None)
            return {"aussagen": []}
        with patch("services.react_engine.ReActEngine.run_loop", fake_run_loop):
            _make_client().get("/search?q=test&mode=tief")
        assert captured.get("max_rounds") == 5
        assert captured.get("use_graph") is True
