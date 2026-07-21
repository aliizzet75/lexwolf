import json
import os
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent.parent.parent / "backend"))

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE_HOST  # Always use host path since we're in a container

DESKTOP_CS = Path(_ROOT) / "desktop" / "MainWindow.xaml.cs"
ROUTE_FILE = Path(_ROOT) / "backend" / "api" / "routes" / "search.py"


def _collect_events(text):
    events = []
    for block in text.split("\n\n"):
        block = block.strip()
        if block.startswith("data: "):
            try:
                events.append(json.loads(block[6:]))
            except json.JSONDecodeError:
                pass
    return events


# ── Desktop-Quellcode-Prüfungen ──────────────────────────────────────────────

def test_mainwindow_exists():
    assert DESKTOP_CS.exists(), f"MainWindow.xaml.cs fehlt: {DESKTOP_CS}"


def test_sse_stream_call_in_mainwindow():
    assert DESKTOP_CS.exists(), f"MainWindow.xaml.cs fehlt: {DESKTOP_CS}"
    src = DESKTOP_CS.read_text()
    assert "search/stream" in src, "Kein SSE-Aufruf (/api/search/stream) in MainWindow.xaml.cs"


def test_reasoning_panel_live_updates():
    assert DESKTOP_CS.exists(), f"MainWindow.xaml.cs fehlt: {DESKTOP_CS}"
    src = DESKTOP_CS.read_text()
    assert "AddReasoning" in src, "AddReasoning() fehlt – Denkprozess-Panel wird nicht befüllt"
    assert any(k in src for k in ("ReadLineAsync", "GetStreamAsync", "ReadToEndAsync")), \
        "Kein async Stream-Read in MainWindow.xaml.cs (ReadLineAsync / GetStreamAsync fehlt)"


# ── Backend-SSE-Endpunkt ─────────────────────────────────────────────────────

@pytest.mark.skipif(not ROUTE_FILE.exists(), reason="api/routes/search.py nicht vorhanden")
class TestSseStream:
    def _make_client(self):
        from unittest.mock import patch, AsyncMock
        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from api.routes.search import router
        app = FastAPI()
        app.include_router(router)
        chunks = [{"id": "c1", "text": "Kündigungsschutz §1 KSchG", "score": 0.9, "source": "KSchG"}]
        with patch("services.search_service.HybridSearchService.parallel_search",
                   new=AsyncMock(return_value=[chunks])), \
             patch("services.react_engine.generate_structured_response",
                   return_value={"aussagen": [{"text": "Der Anspruch besteht.", "quellen": ["c1"]}]}), \
             patch("services.react_engine.evaluate_completeness",
                   return_value={"vollstaendig": True, "fehlende_aspekte": []}):
            resp = TestClient(app).get("/api/search/stream?q=Kündigung")
        return resp

    def test_stream_erreichbar(self):
        resp = self._make_client()
        assert resp.status_code == 200
        assert "text/event-stream" in resp.headers.get("content-type", "")

    def test_events_enthalten_step_und_text(self):
        events = _collect_events(self._make_client().text)
        assert len(events) > 0, "Keine SSE-Events empfangen"
        for ev in events:
            assert "step" in ev and "text" in ev, f"Event fehlt step/text: {ev}"

    def test_fertig_event_vorhanden(self):
        steps = {ev["step"] for ev in _collect_events(self._make_client().text) if "step" in ev}
        assert "fertig" in steps, f"fertig-Event fehlt. Steps: {steps}"
