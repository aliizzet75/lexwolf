import os
import sys
from pathlib import Path

import pytest

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
if os.path.isdir("/data/.openclaw/workspace-codex/projects/lexwolf"):
    _ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"

sys.path.insert(0, os.path.join(_ROOT, "backend"))
DOC_FILE = Path(_ROOT) / "docs" / "bea_compatibility.md"
ROUTE_FILE = Path(_ROOT) / "backend" / "api" / "routes" / "export.py"


def test_dokumentation_existiert_und_hat_pflichtabschnitte():
    assert DOC_FILE.exists(), "docs/bea_compatibility.md fehlt"
    content = DOC_FILE.read_text(encoding="utf-8")
    assert "PDF/A" in content
    assert "Font" in content or "Schriftart" in content
    assert "Gaps" in content or "Offene Punkte" in content


@pytest.mark.skipif(not ROUTE_FILE.exists(), reason="api/routes/export.py nicht vorhanden")
def test_pdf_export_fonts_sind_eingebettet():
    import pypdf
    from fastapi import FastAPI
    from fastapi.testclient import TestClient
    from api.routes.export import router

    app = FastAPI()
    app.include_router(router)
    payload = {
        "text": "Sehr geehrte Damen und Herren,\n\nhiermit erhebe ich Klage.",
        "metadaten": {"gericht": "Amtsgericht Berlin", "aktenzeichen": "1 C 98/26"},
    }
    resp = TestClient(app).post("/api/export/pdf", json=payload)
    assert resp.status_code == 200

    import io

    reader = pypdf.PdfReader(io.BytesIO(resp.content))
    fonts = reader.pages[0]["/Resources"]["/Font"]
    assert len(fonts) > 0, "Keine Schriftarten im PDF gefunden"
    for font_ref in fonts.values():
        font = font_ref.get_object()
        descriptor = font.get("/FontDescriptor")
        has_embedded = descriptor is not None and any(
            k in descriptor for k in ("/FontFile", "/FontFile2", "/FontFile3")
        )
        assert has_embedded, f"Font {font.get('/BaseFont')} ist nicht eingebettet (beA-Anforderung verletzt)"
