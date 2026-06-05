import pytest, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../backend"))

MODULE = "/data/.openclaw/workspace-codex/projects/lexwolf/backend/services/react_engine.py"
LOW  = [{"id": "c1", "text": "t", "score": 0.05}, {"id": "c2", "text": "t", "score": 0.05}]
HIGH = [{"id": "c1", "text": "t", "score": 0.95}, {"id": "c2", "text": "t", "score": 0.95}]
AUSSAGEN = [{"text": "Anspruch besteht", "quellen": ["c1", "c2"]}]

@pytest.mark.skipif(not os.path.exists(MODULE), reason="react_engine.py nicht vorhanden")
class TestConfidenceRefusal:
    def setup_method(self):
        from services import react_engine as m
        self.mod, self.fn = m, m.check_confidence_refusal

    def test_threshold_readable(self):
        assert isinstance(self.mod.CONFIDENCE_THRESHOLD, float)
        assert 0.0 < self.mod.CONFIDENCE_THRESHOLD <= 1.0

    def test_low_confidence_verweigert(self):
        r = self.fn(AUSSAGEN, LOW)
        assert r is not None and r.get("verweigert") is True

    def test_refusal_has_antwort_and_score(self):
        r = self.fn(AUSSAGEN, LOW)
        assert len(r.get("antwort", "")) > 20
        assert isinstance(r.get("confidence"), float)

    def test_high_confidence_kein_verweigert(self):
        r = self.fn(AUSSAGEN, HIGH)
        assert r is None or r.get("verweigert") is not True
