import pytest
import os
import sys
import socket
import inspect

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))


def _port_ok(p):
    s = socket.socket()
    r = s.connect_ex(('localhost', p))
    s.close()
    return r == 0


services_ok = _port_ok(5432)
api_key_ok = bool(os.environ.get("CLAUDE_API_KEY") or os.environ.get("ANTHROPIC_API_KEY"))


def test_hyde_embed_exists():
    """hyde_embed() Methode muss in HybridSearchService vorhanden sein."""
    from services.search_service import HybridSearchService
    assert hasattr(HybridSearchService, 'hyde_embed'), \
        'Methode hyde_embed fehlt in HybridSearchService'


def test_hyde_embed_has_query_param():
    """hyde_embed() muss einen query Parameter akzeptieren."""
    from services.search_service import HybridSearchService
    sig = inspect.signature(HybridSearchService.hyde_embed)
    assert 'query' in sig.parameters, 'hyde_embed muss query Parameter haben'


def test_search_uses_hyde_embed():
    """search() muss hyde_embed() statt direkt generate_embedding() aufrufen."""
    from services.search_service import HybridSearchService
    source = inspect.getsource(HybridSearchService.search)
    assert 'hyde_embed' in source, \
        'search() soll hyde_embed() verwenden, nicht direkt generate_embedding()'


def test_hyde_embed_llm_prompt():
    """hyde_embed() muss den korrekten juristischen LLM-Prompt verwenden."""
    from services.search_service import HybridSearchService
    source = inspect.getsource(HybridSearchService.hyde_embed)
    assert 'juristische' in source.lower() or 'jurist' in source.lower(), \
        'hyde_embed() muss einen juristischen Prompt verwenden'
    assert '2-3' in source or '2 - 3' in source, \
        'hyde_embed() Prompt muss 2-3 Sätze anfordern'


@pytest.mark.skipif(not services_ok, reason='PostgreSQL nicht erreichbar')
@pytest.mark.skipif(not api_key_ok, reason='CLAUDE_API_KEY / ANTHROPIC_API_KEY nicht gesetzt')
def test_hyde_embed_returns_vector():
    """hyde_embed() muss einen 1536-dimensionalen Float-Vektor zurückgeben."""
    from services.search_service import HybridSearchService
    svc = HybridSearchService()
    vector = svc.hyde_embed("Was ist ein Werkvertrag nach BGB?")
    assert isinstance(vector, list), 'hyde_embed muss eine Liste zurückgeben'
    assert len(vector) == 1536, \
        f'Embedding-Dimensionen stimmen nicht: erwartet 1536, erhalten {len(vector)}'
    assert all(isinstance(x, float) for x in vector[:10]), \
        'Alle Werte müssen float sein'


@pytest.mark.skipif(not services_ok, reason='PostgreSQL nicht erreichbar')
@pytest.mark.skipif(not api_key_ok, reason='CLAUDE_API_KEY / ANTHROPIC_API_KEY nicht gesetzt')
def test_hyde_embed_improves_query_semantics():
    """HyDE-Vektor muss sich vom direkten Query-Vektor unterscheiden (LLM expandiert)."""
    from services.search_service import HybridSearchService
    svc = HybridSearchService()
    query = "Kündigung fristlos Arbeitsrecht"
    hyde_vec = svc.hyde_embed(query)
    direct_vec = svc.embedding_service.generate_embedding(query)
    # Vectors should differ since LLM generates an expanded hypothetical answer
    diff = sum(abs(a - b) for a, b in zip(hyde_vec, direct_vec))
    assert diff > 0.1, 'HyDE-Vektor und direkter Query-Vektor sind identisch — LLM wurde nicht aufgerufen'
