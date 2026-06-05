import pytest, os, sys, socket
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))

def _port_ok(p):
    for host in ('localhost', 'host.docker.internal'):
        s = socket.socket(); r = s.connect_ex((host, p)); s.close()
        if r == 0:
            return True
    return False

services_ok = _port_ok(5432) and _port_ok(7687)

def test_hybrid_search_with_graph_exists():
    from services.search_service import HybridSearchService
    assert hasattr(HybridSearchService, 'hybrid_search_with_graph'),         'Methode hybrid_search_with_graph fehlt in HybridSearchService'

@pytest.mark.skipif(not services_ok, reason='PostgreSQL oder Neo4j nicht erreichbar')
def test_simple_query_no_graph_overhead():
    from services.search_service import HybridSearchService
    results = HybridSearchService().hybrid_search_with_graph('Was besagt § 1 BGB?')
    assert isinstance(results, list)

@pytest.mark.skipif(not services_ok, reason='PostgreSQL oder Neo4j nicht erreichbar')
def test_complex_query_no_duplicates():
    from services.search_service import HybridSearchService
    results = HybridSearchService().hybrid_search_with_graph(
        'Wie verhält sich § 1 BGB zu § 23 KSchG?'
    )
    assert isinstance(results, list)
    ids = [r['id'] for r in results if 'id' in r]
    assert len(ids) == len(set(ids)), 'Duplikate im Ergebnis'
