import pytest, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../backend"))

MODULE_PATH = "/data/.openclaw/workspace-codex/projects/lexwolf/backend/services/react_engine.py"

def module_available():
    return os.path.exists(MODULE_PATH)

@pytest.mark.skipif(not module_available(), reason="react_engine.py nicht vorhanden")
class TestCalculateConfidence:
    def setup_method(self):
        from services.react_engine import calculate_confidence
        self.fn = calculate_confidence

    CHUNKS = [
        {"id": "c1", "text": "§1 BGB Urlaubsanspruch", "score": 0.9},
        {"id": "c2", "text": "§2 BGB Werktage", "score": 0.7},
        {"id": "c3", "text": "§3 BGB Regelung", "score": 0.5},
    ]

    def test_returns_float(self):
        r = self.fn({"text": "Test", "quellen": ["c1"]}, self.CHUNKS)
        assert isinstance(r, float)

    def test_range_zero_to_one(self):
        r = self.fn({"text": "Test", "quellen": ["c1", "c2"]}, self.CHUNKS)
        assert 0.0 <= r <= 1.0

    def test_more_sources_higher_score(self):
        low = self.fn({"text": "Test", "quellen": ["c1"]}, self.CHUNKS)
        high = self.fn({"text": "Test", "quellen": ["c1", "c2", "c3"]}, self.CHUNKS)
        assert high >= low

    def test_higher_relevance_higher_score(self):
        chunks_low = [{"id": "c1", "text": "Text", "score": 0.1}]
        chunks_high = [{"id": "c1", "text": "Text", "score": 0.9}]
        low = self.fn({"text": "Test", "quellen": ["c1"]}, chunks_low)
        high = self.fn({"text": "Test", "quellen": ["c1"]}, chunks_high)
        assert high >= low

    def test_no_sources_returns_low_score(self):
        r = self.fn({"text": "Test", "quellen": []}, self.CHUNKS)
        assert r < 0.5

    def test_empty_chunks_returns_zero(self):
        r = self.fn({"text": "Test", "quellen": []}, [])
        assert r == 0.0
