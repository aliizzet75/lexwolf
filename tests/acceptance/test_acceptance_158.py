import os
import socket
import sys

import psycopg2
import pytest

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
sys.path.insert(0, os.path.join(_ROOT, "backend"))
os.environ.setdefault("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")

PG_DSN = "postgresql://postgres:postgres@localhost:5432/lexwolf"
KSCHG_23_ID = 6155  # 'KSCHG - § 23 Geltungsbereich'


def _pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


def test_boost_method_exists():
    from services.search_service import HybridSearchService
    assert hasattr(HybridSearchService, "_boost_by_recognized_paragraphs"), \
        "_boost_by_recognized_paragraphs() fehlt in HybridSearchService"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_kschg23_in_db():
    conn = psycopg2.connect(PG_DSN)
    cur = conn.cursor()
    cur.execute("SELECT id, title FROM legal_chunks WHERE id = %s", (KSCHG_23_ID,))
    row = cur.fetchone()
    conn.close()
    assert row is not None, f"KSchG §23 chunk (id={KSCHG_23_ID}) nicht in DB"
    assert "23" in row[1], f"Unerwarteter Titel: {row[1]}"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_paragraph_boost_returns_kschg23_in_top5():
    from services.search_service import HybridSearchService
    svc = HybridSearchService()
    results = svc._boost_by_recognized_paragraphs("§23 KSchG Geltungsbereich Kleinbetrieb", limit=10)
    ids = [r.get("id") for r in results[:5]]
    assert KSCHG_23_ID in ids, (
        f"KSchG §23 (id={KSCHG_23_ID}) nicht in Top-5 der Paragraph-Boost-Ergebnisse. "
        f"Top-5-IDs: {ids}"
    )
