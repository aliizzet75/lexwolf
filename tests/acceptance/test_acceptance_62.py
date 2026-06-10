import sys, os, json, importlib.util, tempfile
from pathlib import Path

_BASE = Path("/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf.disabled")
if not _BASE.exists():
    _BASE = Path("/data/.openclaw/workspace-codex/projects/lexwolf")
ROUTE_FILE = _BASE / "backend/api/routes/quality.py"
sys.path.insert(0, str(_BASE / "backend"))

def test_route_file_and_content():
    assert ROUTE_FILE.exists(), f"Route-Datei fehlt: {ROUTE_FILE}"
    src = ROUTE_FILE.read_text()
    for token in ("dashboard", "results.json", "halluzinations_rate", "quellen_genauigkeit", "vollstaendigkeits_score"):
        assert token in src, f"'{token}' fehlt in {ROUTE_FILE.name}"
    assert "trend" in src.lower(), "Trend-Vergleich fehlt"

def test_dashboard_response_via_testclient():
    from fastapi.testclient import TestClient
    from fastapi import FastAPI
    sample = [
        {"halluzinations_rate": 0.05, "quellen_genauigkeit": 0.92, "vollstaendigkeits_score": 0.88},
        {"halluzinations_rate": 0.08, "quellen_genauigkeit": 0.90, "vollstaendigkeits_score": 0.85},
    ]
    with tempfile.NamedTemporaryFile(mode='w', suffix='.json', delete=False) as f:
        json.dump(sample, f); tmp = f.name
    os.environ["QUALITY_RESULTS_PATH"] = tmp
    spec = importlib.util.spec_from_file_location("routes.quality", ROUTE_FILE)
    mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
    os.unlink(tmp)
    app = FastAPI(); app.include_router(mod.router)
    resp = TestClient(app).get("/api/quality/dashboard")
    assert resp.status_code == 200
    data = resp.json()
    for key in ("halluzinations_rate", "quellen_genauigkeit", "vollstaendigkeits_score", "trend"):
        assert key in data, f"Feld '{key}' fehlt in Response"
