import pytest, os, sys, inspect
from unittest.mock import patch, MagicMock
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))

def test_rerank_signature():
    from services.search_service import HybridSearchService
    assert hasattr(HybridSearchService, 'rerank_results'), 'rerank_results() fehlt'
    p = inspect.signature(HybridSearchService.rerank_results).parameters
    assert 'query' in p and 'chunks' in p and 'top_k' in p
    assert p['top_k'].default == 5, f'top_k default muss 5 sein, ist {p["top_k"].default}'

def test_rerank_filters_low_scores():
    from services.search_service import HybridSearchService
    svc = HybridSearchService.__new__(HybridSearchService)
    chunks = [{'id': i, 'text': f'Text {i}'} for i in range(4)]
    scores = [9, 6, 8, 3]  # Chunks 0 und 2 haben Score>=7
    responses = [MagicMock(content=[MagicMock(text=f'RELEVANZ: {s}')]) for s in scores]
    mock_client = MagicMock()
    mock_client.messages.create.side_effect = responses
    with patch('anthropic.Anthropic', return_value=mock_client):
        result = svc.rerank_results('Werkvertrag', chunks, top_k=5)
    assert len(result) == 2, f'Nur Chunks mit Score>=7 erwartet, got {len(result)}'
    assert all(c['rerank_score'] >= 7 for c in result)
    assert result[0]['rerank_score'] >= result[-1]['rerank_score'], 'Ergebnisse nach Score sortieren'

def test_rerank_respects_top_k():
    from services.search_service import HybridSearchService
    svc = HybridSearchService.__new__(HybridSearchService)
    chunks = [{'id': i, 'text': f'Text {i}'} for i in range(5)]
    responses = [MagicMock(content=[MagicMock(text='RELEVANZ: 9')]) for _ in chunks]
    mock_client = MagicMock()
    mock_client.messages.create.side_effect = responses
    with patch('anthropic.Anthropic', return_value=mock_client):
        result = svc.rerank_results('Frage', chunks, top_k=2)
    assert len(result) <= 2, f'top_k=2 muss beachtet werden, got {len(result)}'

def test_tief_modus_uses_rerank_schnell_does_not():
    from services import react_engine
    src = inspect.getsource(react_engine.ReActEngine.run_loop)
    assert 'rerank_results' in src, 'run_loop() muss rerank_results() aufrufen'
    assert "modus == 'tief'" in src or 'modus == "tief"' in src, \
        'Reranking muss an Tief-Modus gebunden sein'
