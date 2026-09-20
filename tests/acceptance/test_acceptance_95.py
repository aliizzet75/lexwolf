import io
import os
import sys
from pathlib import Path

import pytest

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
if os.path.isdir("/data/.openclaw/workspace-codex/projects/lexwolf"):
    _ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"

sys.path.insert(0, os.path.join(_ROOT, "backend"))
ROUTE_FILE = Path(_ROOT) / "backend" / "api" / "routes" / "kanzlei.py"


@pytest.mark.skipif(not ROUTE_FILE.exists(), reason="api/routes/kanzlei.py nicht vorhanden")
class TestBriefkopf:
    def _app(self):
        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from api.routes.kanzlei import router as kanzlei_router
        from api.routes.export import router as export_router

        app = FastAPI()
        app.include_router(kanzlei_router)
        app.include_router(export_router)
        return TestClient(app)

    def test_upload_briefkopf_docx(self):
        client = self._app()
        docx_bytes = io.BytesIO(b"PK\x03\x04fake-docx-content")
        resp = client.post(
            "/api/kanzlei/briefkopf",
            files={"file": ("briefkopf.docx", docx_bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")},
        )
        assert resp.status_code == 200

    def test_export_ohne_briefkopf_faellt_zurueck(self):
        client = self._app()
        payload = {"text": "Hiermit erhebe ich Klage.", "metadaten": {"aktenzeichen": "1 C 1/26"}}
        resp = client.post("/api/export/docx", json=payload)
        assert resp.status_code == 200

    def test_platzhalter_werden_ersetzt(self):
        from docx import Document

        client = self._app()
        payload = {
            "text": "Betrifft: {AKTENZEICHEN}, Mandant: {MANDANT}, Datum: {DATUM}",
            "metadaten": {"aktenzeichen": "1 C 1/26", "mandant": "Max Mustermann"},
        }
        resp = client.post("/api/export/docx", json=payload)
        assert resp.status_code == 200
        doc = Document(io.BytesIO(resp.content))
        full_text = "\n".join(p.text for p in doc.paragraphs)
        assert "{AKTENZEICHEN}" not in full_text
        assert "{MANDANT}" not in full_text
        assert "1 C 1/26" in full_text
        assert "Max Mustermann" in full_text
