import pytest
import os
import sys
import asyncio
from unittest.mock import patch, AsyncMock, MagicMock

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))


def test_react_engine_class_exists():
    from services.react_engine import ReActEngine
    assert callable(ReActEngine)


def test_run_loop_stops_early_on_vollstaendig():
    """Loop stops before max_rounds when vollstaendig=True."""
    from services.react_engine import ReActEngine

    call_count = [0]

    def mock_evaluate(query, results):
        call_count[0] += 1
        if call_count[0] >= 2:
            return {'vollstaendig': True, 'fehlende_aspekte': []}
        return {'vollstaendig': False, 'fehlende_aspekte': ['Aspekt A']}

    async def mock_parallel_search(queries):
        return [[{'text': f'result for {q}', 'chunk_id': f'cid_{q}_{call_count[0]}'}] for q in queries]

    with patch('services.react_engine.evaluate_completeness', side_effect=mock_evaluate):
        from services.search_service import HybridSearchService
        engine = ReActEngine()
        with patch.object(HybridSearchService, 'parallel_search', new=mock_parallel_search):
            result = asyncio.run(engine.run_loop('Was ist § 242 BGB?'))

    assert result['vollstaendig'] is True, 'Muss True sein wenn vollstaendig'
    assert result['rounds'] < ReActEngine.MAX_ROUNDS, 'Soll vor max_rounds stoppen'


def test_run_loop_max_5_rounds():
    """Loop never exceeds MAX_ROUNDS=5."""
    from services.react_engine import ReActEngine

    def mock_evaluate(query, results):
        return {'vollstaendig': False, 'fehlende_aspekte': ['fehlender Aspekt']}

    async def mock_parallel_search(queries):
        return [[{'text': 'chunk', 'chunk_id': 'x'}] for _ in queries]

    with patch('services.react_engine.evaluate_completeness', side_effect=mock_evaluate):
        from services.search_service import HybridSearchService
        engine = ReActEngine()
        with patch.object(HybridSearchService, 'parallel_search', new=mock_parallel_search):
            result = asyncio.run(engine.run_loop('Testfrage'))

    assert result['rounds'] <= 5, f'Mehr als 5 Runden: {result["rounds"]}'


def test_run_loop_accumulates_results():
    """Results accumulate across rounds."""
    from services.react_engine import ReActEngine

    round_counter = [0]

    def mock_evaluate(query, results):
        round_counter[0] += 1
        if round_counter[0] >= 3:
            return {'vollstaendig': True, 'fehlende_aspekte': []}
        return {'vollstaendig': False, 'fehlende_aspekte': [f'aspekt_{round_counter[0]}']}

    async def mock_parallel_search(queries):
        return [[{'text': f'chunk_{q}', 'chunk_id': f'{q}_{round_counter[0]}'}] for q in queries]

    with patch('services.react_engine.evaluate_completeness', side_effect=mock_evaluate):
        from services.search_service import HybridSearchService
        engine = ReActEngine()
        with patch.object(HybridSearchService, 'parallel_search', new=mock_parallel_search):
            result = asyncio.run(engine.run_loop('Frage'))

    assert len(result['accumulated_chunks']) > 1, 'Chunks aus mehreren Runden müssen akkumuliert werden'


def test_run_loop_uses_fehlende_aspekte_as_queries():
    """Follow-up queries come from fehlende_aspekte."""
    from services.react_engine import ReActEngine

    captured_queries = []
    call_count = [0]

    def mock_evaluate(query, results):
        call_count[0] += 1
        if call_count[0] >= 2:
            return {'vollstaendig': True, 'fehlende_aspekte': []}
        return {'vollstaendig': False, 'fehlende_aspekte': ['spezifischer Aspekt X']}

    async def mock_parallel_search(queries):
        captured_queries.append(list(queries))
        return [[{'text': 'chunk', 'chunk_id': f'c{i}_{call_count[0]}'}] for i, _ in enumerate(queries)]

    with patch('services.react_engine.evaluate_completeness', side_effect=mock_evaluate):
        from services.search_service import HybridSearchService
        engine = ReActEngine()
        with patch.object(HybridSearchService, 'parallel_search', new=mock_parallel_search):
            result = asyncio.run(engine.run_loop('Ursprungsfrage'))

    assert len(captured_queries) >= 2, 'Mindestens 2 Suchrunden erwartet'
    assert 'spezifischer Aspekt X' in captured_queries[1], \
        f'2. Runde muss fehlende_aspekte nutzen, war: {captured_queries[1]}'
