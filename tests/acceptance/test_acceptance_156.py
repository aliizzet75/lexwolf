import os
import socket
import sys
import pytest

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
sys.path.insert(0, os.path.join(_ROOT, "backend"))


def _ollama_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 11434))
    s.close()
    return r == 0


def _pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


def test_rewrite_method_exists():
    from services.search_service import HybridSearchService
    assert hasattr(HybridSearchService, "_rewrite_query_for_legal_search"), \
        "_rewrite_query_for_legal_search() fehlt in HybridSearchService"


@pytest.mark.skipif(not _ollama_ok(), reason="OLLAMA nicht erreichbar auf port 11434")
def test_rewrite_kuendigungsschutz_enthaelt_paragraph_23_oder_kschg():
    from services.search_service import HybridSearchService
    svc = HybridSearchService()
    result = svc._rewrite_query_for_legal_search("12 Mitarbeiter Kündigungsschutz")
    assert isinstance(result, str) and len(result) > 0, "Rewrite lieferte leeren String"
    lower = result.lower()
    assert "§23" in lower or "§ 23" in lower or "kschg" in lower, \
        f"Rewrite enthält weder §23 noch KSchG: {result!r}"


@pytest.mark.skipif(not _ollama_ok(), reason="OLLAMA nicht erreichbar auf port 11434")
def test_rewrite_fallback_bei_fehler():
    from services.search_service import HybridSearchService
    svc = HybridSearchService()
    original = "Kündigungsschutz Kleinbetrieb"
    # Force fallback by temporarily breaking OLLAMA_URL
    old = os.environ.get("OLLAMA_URL", "")
    os.environ["OLLAMA_URL"] = "http://localhost:1"
    result = svc._rewrite_query_for_legal_search(original)
    os.environ["OLLAMA_URL"] = old
    assert result == original, f"Fallback gab nicht original query zurück: {result!r}"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
@pytest.mark.skipif(not _ollama_ok(), reason="OLLAMA nicht erreichbar auf port 11434")
def test_search_ruft_rewrite_vor_embedding_auf(monkeypatch):
    from services.search_service import HybridSearchService
    svc = HybridSearchService()
    calls = []

    original_rewrite = HybridSearchService._rewrite_query_for_legal_search

    def spy_rewrite(self, q):
        calls.append(q)
        return original_rewrite(self, q)

    monkeypatch.setattr(HybridSearchService, "_rewrite_query_for_legal_search", spy_rewrite)
    svc.search("Kündigungsschutz Kleinbetrieb", limit=3)
    assert len(calls) >= 1, "search() hat _rewrite_query_for_legal_search() nicht aufgerufen"
