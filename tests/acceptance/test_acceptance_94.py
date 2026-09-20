import io
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent.parent.parent / "backend"))

_ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"
ROUTE_FILE = Path(_ROOT) / "backend" / "api" / "routes" / "export.py"

CM_TO_TWIPS = 566929 / 100  # docx section.left_margin Emu-Vergleich via cm


@pytest.mark.skipif(not ROUTE_FILE.exists(), reason="api/routes/export.py nicht vorhanden")
class TestDocxExport:
    def _response(self):
        from docx import Document
        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from api.routes.export import router

        app = FastAPI()
        app.include_router(router)
        payload = {
            "text": "Sehr geehrte Damen und Herren,\n\nhiermit erhebe ich Klage.",
            "metadaten": {"gericht": "Amtsgericht Berlin", "aktenzeichen": "1 C 1/26"},
        }
        return TestClient(app).post("/api/export/docx", json=payload)

    def test_gibt_docx_zurueck(self):
        resp = self._response()
        assert resp.status_code == 200
        ctype = resp.headers.get("content-type", "")
        assert "wordprocessingml" in ctype or "octet-stream" in ctype

    def test_formatierung_times_new_roman_12pt_und_raender(self):
        from docx import Document
        resp = self._response()
        doc = Document(io.BytesIO(resp.content))
        style = doc.styles["Normal"]
        assert style.font.name == "Times New Roman"
        assert style.font.size is not None and round(style.font.size.pt) == 12
        section = doc.sections[0]
        for margin in (section.left_margin, section.right_margin, section.top_margin, section.bottom_margin):
            assert round(margin.cm, 1) == 2.5

    def test_seitenzahlen_vorhanden(self):
        from docx import Document
        resp = self._response()
        doc = Document(io.BytesIO(resp.content))
        section = doc.sections[0]
        footer_xml = section.footer._element.xml
        assert "PAGE" in footer_xml
