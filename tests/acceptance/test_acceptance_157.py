import inspect
import os
import socket
import sys

import psycopg2
import pytest

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf.disabled"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf.disabled"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
sys.path.insert(0, os.path.join(_ROOT, "backend"))


def _pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_websearch_kuendigungsschutz_count_gt0():
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE ts_vector @@ websearch_to_tsquery('german', 'Kündigungsschutz')")
    count = cur.fetchone()[0]
    conn.close()
    assert count > 0, f"websearch_to_tsquery('Kündigungsschutz') → {count} Treffer, ts_vector muss neu berechnet werden"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_sparse_kuendigung_mitarbeiter_min5():
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE ts_vector @@ websearch_to_tsquery('german', 'Kündigung Mitarbeiter')")
    count = cur.fetchone()[0]
    conn.close()
    assert count >= 5, f"Nur {count} Treffer für 'Kündigung Mitarbeiter', mind. 5 erwartet"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_gin_index_auf_ts_vector_vorhanden():
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute(
        "SELECT indexname FROM pg_indexes WHERE tablename='legal_chunks' AND indexdef ILIKE '%gin%' AND indexdef ILIKE '%ts_vector%'"
    )
    idx = cur.fetchone()
    conn.close()
    assert idx is not None, "Kein GIN-Index auf ts_vector gefunden — CREATE INDEX idx_ts_vector ... USING GIN(ts_vector) fehlt"


def test_sparse_search_nutzt_websearch_to_tsquery():
    from services.search_service import HybridSearchService
    src = inspect.getsource(HybridSearchService._sparse_search)
    assert "websearch_to_tsquery" in src, "_sparse_search() nutzt noch plainto_tsquery statt websearch_to_tsquery"


def test_sparse_search_hat_or_fallback():
    from services.search_service import HybridSearchService
    src = inspect.getsource(HybridSearchService._sparse_search)
    assert "OR" in src or " | " in src, "_sparse_search() hat keinen OR-Fallback für 0-Ergebnis-Queries (Einzelwörter | verknüpft)"
