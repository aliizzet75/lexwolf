import os
import sys
from pathlib import Path

import pytest

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
if os.path.isdir("/data/.openclaw/workspace-codex/projects/lexwolf"):
    _ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"

sys.path.insert(0, os.path.join(_ROOT, "backend"))
ROUTE_FILE = Path(_ROOT) / "backend" / "api" / "routes" / "export.py"


@pytest.mark.skipif(not ROUTE_FILE.exists(), reason="api/routes/export.py nicht vorhanden")
class TestPdfExport:
    def _response(self):
        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from api.routes.export import router

        app = FastAPI()
        app.include_router(router)
        payload = {
            "text": "Sehr geehrte Damen und Herren,\n\nhiermit erhebe ich Klage.",
            "metadaten": {"gericht": "Amtsgericht Berlin", "aktenzeichen": "1 C 1/26"},
        }
        return TestClient(app).post("/api/export/pdf", json=payload)

    def test_gibt_pdf_zurueck(self):
        resp = self._response()
        assert resp.status_code == 200
        assert resp.headers.get("content-type") == "application/pdf"
        assert resp.content.startswith(b"%PDF")

    def test_pdf_ist_nicht_leer_und_druckfertig(self):
        resp = self._response()
        assert len(resp.content) > 500
        assert b"%%EOF" in resp.content[-64:] or b"%%EOF" in resp.content
