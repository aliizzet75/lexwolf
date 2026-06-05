import pytest, os, sys
from unittest.mock import MagicMock
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../backend"))

MODULE_PATH = "/data/.openclaw/workspace-codex/projects/lexwolf/backend/services/react_engine.py"

def module_available():
    return os.path.exists(MODULE_PATH)

def post_verify_available():
    if not module_available():
        return False
    import importlib.util
    spec = importlib.util.spec_from_file_location("re_mod", MODULE_PATH)
    mod = importlib.util.load_module_from_spec(spec) if hasattr(importlib.util, 'load_module_from_spec') else None
    try:
        from services.react_engine import post_verify
        return True
    except ImportError:
        return False

@pytest.mark.skipif(not module_available(), reason="react_engine.py nicht vorhanden")
class TestPostVerify:
    def setup_method(self):
        from services.react_engine import post_verify
        self.fn = post_verify

    def _session(self, existing_ids):
        s = MagicMock()
        def fb(**kw):
            cid = str(kw.get('id', kw.get('id', '')))
            m = MagicMock()
            m.first.return_value = MagicMock() if cid in [str(i) for i in existing_ids] else None
            return m
        s.query.return_value.filter_by.side_effect = fb
        return s

    def test_post_verify_exists(self):
        from services import react_engine
        assert hasattr(react_engine, 'post_verify'), "post_verify nicht in react_engine"

    def test_existing_chunk_verified_true(self):
        session = self._session([1])
        result = self.fn({"aussagen": [{"text": "A", "quellen": [1]}]}, session=session)
        assert result["aussagen"][0]["verified"] is True

    def test_missing_chunk_verified_false(self):
        session = self._session([])
        result = self.fn({"aussagen": [{"text": "A", "quellen": [999]}]}, session=session)
        assert result["aussagen"][0]["verified"] is False

    def test_hallucination_rate_all_verified(self):
        session = self._session([1, 2])
        aussagen = [{"text": "T", "quellen": [1]}, {"text": "U", "quellen": [2]}]
        result = self.fn({"aussagen": aussagen}, session=session)
        assert result.get("hallucination_rate") == 0.0

    def test_hallucination_rate_half(self):
        session = self._session([1])
        aussagen = [{"text": "T", "quellen": [1]}, {"text": "U", "quellen": [999]}]
        result = self.fn({"aussagen": aussagen}, session=session)
        assert result.get("hallucination_rate") == 0.5

    def test_empty_aussagen(self):
        result = self.fn({"aussagen": []})
        assert result.get("hallucination_rate") == 0.0
