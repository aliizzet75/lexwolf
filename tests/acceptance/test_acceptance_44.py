import pytest, os, sys, socket, asyncio, time
from unittest.mock import patch
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))
def _pg_ok(): s=socket.socket(); r=s.connect_ex(('localhost',5432)); s.close(); return r==0
services_ok = _pg_ok()
def test_parallel_search_exists():
    from services.search_service import HybridSearchService
    assert hasattr(HybridSearchService, 'parallel_search'), 'parallel_search fehlt'
def test_parallel_search_deduplication_and_type():
    from services.search_service import HybridSearchService
    with patch.object(HybridSearchService, 'search', side_effect=[
        [{"chunk_id": "dup", "score": 0.5, "text": "a"}],
        [{"chunk_id": "dup", "score": 0.9, "text": "a"}, {"chunk_id": "uniq", "score": 0.7, "text": "b"}]
    ]):
        svc = HybridSearchService.__new__(HybridSearchService)
        result = asyncio.run(svc.parallel_search(["q1", "q2"]))
    assert isinstance(result, list) and len(result) == 2, 'Muss list mit 2 Sublisten zurückgeben'
    assert all(isinstance(r, list) for r in result), 'Jedes Element muss eine list sein'
    flat = [item for sub in result for item in sub]
    cids = [r["chunk_id"] for r in flat if "chunk_id" in r]
    assert len(cids) == len(set(cids)), 'Duplikate nach chunk_id nicht dedupliziert'
    dup_item = next(r for r in flat if r["chunk_id"] == "dup")
    assert dup_item["score"] == 0.9, 'Höchster Relevanz-Score muss behalten werden'
@pytest.mark.skipif(not services_ok, reason='PostgreSQL nicht erreichbar auf Port 5432')
def test_parallel_search_faster_than_sequential():
    from services.search_service import HybridSearchService
    svc, queries = HybridSearchService(), ["§ 1 BGB", "Schadensersatz", "Kündigung"]
    t0=time.time(); asyncio.run(svc.parallel_search(queries)); pt=time.time()-t0
    t1=time.time(); [svc.search(q) for q in queries]; st=time.time()-t1
    assert pt < st, f'Parallel ({pt:.2f}s) nicht schneller als sequenziell ({st:.2f}s)'
