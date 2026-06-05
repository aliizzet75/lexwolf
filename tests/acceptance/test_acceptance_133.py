import pytest, os, sys, socket, inspect
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))

def _port_ok(p):
    s = socket.socket(); r = s.connect_ex(('localhost', p)); s.close(); return r == 0
services_ok = _port_ok(5432)

def test_rrf_method_and_signature():
    from services.search_service import HybridSearchService
    assert hasattr(HybridSearchService, 'reciprocal_rank_fusion'), 'reciprocal_rank_fusion fehlt'
    p = inspect.signature(HybridSearchService.reciprocal_rank_fusion).parameters
    assert 'dense' in p and 'sparse' in p and 'k' in p, 'Signatur braucht dense, sparse, k'
    assert p['k'].default == 60, f'k-Standard muss 60 sein, ist {p["k"].default}'

def test_rrf_formula_and_boost():
    from services.search_service import HybridSearchService
    svc = HybridSearchService.__new__(HybridSearchService)
    dense  = [{'id': 'BOTH', 'dense_rank': 1}, {'id': 'D_ONLY', 'dense_rank': 2}]
    sparse = [{'id': 'BOTH', 'sparse_rank': 1}, {'id': 'S_ONLY', 'sparse_rank': 2}]
    sc = {r['id']: r['score'] for r in svc.reciprocal_rank_fusion(dense, sparse, k=60)}
    assert abs(sc['BOTH'] - (1/61 + 1/61)) < 1e-9, f'RRF-Score BOTH falsch: {sc["BOTH"]}'
    assert sc['BOTH'] > sc['D_ONLY'] and sc['BOTH'] > sc['S_ONLY'], \
        'Chunk in beiden Listen muss höchsten Score haben'

def test_search_uses_rrf():
    from services.search_service import HybridSearchService
    assert 'reciprocal_rank_fusion' in inspect.getsource(HybridSearchService.search), \
        'search() muss reciprocal_rank_fusion() aufrufen'

@pytest.mark.skipif(not services_ok, reason='PostgreSQL nicht erreichbar auf Port 5432')
def test_search_results_have_rrf_score():
    from services.search_service import HybridSearchService
    results = HybridSearchService().search('Was ist ein Werkvertrag?', limit=3)
    assert isinstance(results, list) and all('score' in r for r in results), \
        'Alle Ergebnisse brauchen rrf score-Feld'
