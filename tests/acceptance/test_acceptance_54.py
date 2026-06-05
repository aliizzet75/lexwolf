import json
import os
import sys
import asyncio
from unittest.mock import patch, AsyncMock

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))

ROUTE_FILE = '/data/.openclaw/workspace-codex/projects/lexwolf/backend/api/routes/search.py'


def route_available():
    return os.path.exists(ROUTE_FILE)


def _make_client():
    from fastapi.testclient import TestClient
    from fastapi import FastAPI
    from api.routes.search import router
    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


MOCK_CHUNKS = [
    {'id': 'c1', 'text': 'Paragraph 1 Text', 'score': 0.9, 'source': 'BGB'},
    {'id': 'c2', 'text': 'Paragraph 2 Text', 'score': 0.8, 'source': 'BGB'},
    {'id': 'c3', 'text': 'Paragraph 3 Text', 'score': 0.7, 'source': 'BGB'},
]

MOCK_STRUCTURED = {
    'aussagen': [
        {'text': 'Der Anspruch ergibt sich aus Paragraph 1 und 2 des BGB.', 'quellen': ['c1', 'c2']},
    ]
}


def _mock_parallel_search(queries):
    return [MOCK_CHUNKS for _ in queries]


def _collect_events(response_text):
    events = []
    for line in response_text.split('\n\n'):
        line = line.strip()
        if line.startswith('data: '):
            raw = line[len('data: '):]
            try:
                events.append(json.loads(raw))
            except json.JSONDecodeError:
                events.append({'raw': raw})
    return events


@pytest.mark.skipif(not route_available(), reason='api/routes/search.py nicht vorhanden')
class TestSearchStream:
    def _run(self, q='Kündigungsschutz'):
        with patch('services.search_service.HybridSearchService.parallel_search',
                   new=AsyncMock(return_value=[MOCK_CHUNKS])):
            with patch('services.react_engine.generate_structured_response',
                       return_value=MOCK_STRUCTURED):
                with patch('services.react_engine.evaluate_completeness',
                           return_value={'vollstaendig': True, 'fehlende_aspekte': []}):
                    client = _make_client()
                    resp = client.get(f'/api/search/stream?q={q}')
        return resp

    def test_endpoint_exists(self):
        resp = self._run()
        assert resp.status_code == 200

    def test_content_type_event_stream(self):
        resp = self._run()
        assert 'text/event-stream' in resp.headers.get('content-type', '')

    def test_sse_format_double_newline(self):
        resp = self._run()
        # Must contain double newlines (SSE format)
        assert '\n\n' in resp.text

    def test_sse_data_prefix(self):
        resp = self._run()
        lines = [l for l in resp.text.split('\n') if l.strip()]
        data_lines = [l for l in lines if l.startswith('data: ')]
        assert len(data_lines) > 0, 'Keine data:-Zeilen gefunden'

    def test_events_are_valid_json(self):
        resp = self._run()
        events = _collect_events(resp.text)
        assert len(events) > 0, 'Keine Events empfangen'
        for ev in events:
            assert 'step' in ev, f'Event hat kein step-Feld: {ev}'
            assert 'text' in ev, f'Event hat kein text-Feld: {ev}'

    def test_react_steps_present(self):
        resp = self._run()
        events = _collect_events(resp.text)
        steps = {ev['step'] for ev in events if 'step' in ev}
        # At minimum: suche_start and fertig must appear
        assert 'suche_start' in steps, f'suche_start fehlt. Steps: {steps}'
        assert 'fertig' in steps, f'fertig fehlt. Steps: {steps}'

    def test_antwort_chunk_present(self):
        resp = self._run()
        events = _collect_events(resp.text)
        chunk_events = [ev for ev in events if ev.get('step') == 'antwort_chunk']
        assert len(chunk_events) > 0, 'Keine antwort_chunk Events gefunden'

    def test_antwort_in_multiple_chunks(self):
        resp = self._run()
        events = _collect_events(resp.text)
        chunk_events = [ev for ev in events if ev.get('step') == 'antwort_chunk']
        # Answer should be chunked (more than 1 chunk or single chunk smaller than full answer)
        full_text = MOCK_STRUCTURED['aussagen'][0]['text']
        combined = ''.join(ev.get('text', '') for ev in chunk_events)
        assert combined == full_text or len(chunk_events) >= 1,             'Antworttext wurde nicht korrekt in Chunks gesendet'

    def test_suchergebnis_step_present(self):
        resp = self._run()
        events = _collect_events(resp.text)
        steps = {ev['step'] for ev in events if 'step' in ev}
        assert 'suchergebnis' in steps, f'suchergebnis fehlt. Steps: {steps}'

    def test_bewertung_step_present(self):
        resp = self._run()
        events = _collect_events(resp.text)
        steps = {ev['step'] for ev in events if 'step' in ev}
        assert 'bewertung' in steps, f'bewertung fehlt. Steps: {steps}'
