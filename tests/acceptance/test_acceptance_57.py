import sys
import os
import pytest

METRICS_PATH = "/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/metrics.py"
sys.path.insert(0, "/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality")

PROBE = [
    {"verifiziert": True,  "korrekte_quellen": ["§622 BGB"], "erwartete_paragraphen": ["§622 BGB", "§1 KSchG"], "verweigert": False},
    {"verifiziert": False, "korrekte_quellen": [],              "erwartete_paragraphen": ["§558 BGB"],               "verweigert": False},
    {"verifiziert": True,  "korrekte_quellen": ["§1 KSchG"],  "erwartete_paragraphen": ["§1 KSchG"],               "verweigert": True},
]

@pytest.mark.skipif(not os.path.exists(METRICS_PATH), reason="metrics.py nicht vorhanden")
class TestMetrics57:
    def test_halluzinations_rate_range(self):
        from metrics import halluzinations_rate
        r = halluzinations_rate(PROBE)
        assert 0.0 <= r <= 1.0, f"halluzinations_rate={r} ausserhalb 0-1"

    def test_quellen_genauigkeit_range(self):
        from metrics import quellen_genauigkeit
        r = quellen_genauigkeit(PROBE)
        assert 0.0 <= r <= 1.0, f"quellen_genauigkeit={r} ausserhalb 0-1"

    def test_vollstaendigkeits_score_range(self):
        from metrics import vollstaendigkeits_score
        r = vollstaendigkeits_score(PROBE)
        assert 0.0 <= r <= 1.0, f"vollstaendigkeits_score={r} ausserhalb 0-1"

    def test_antwort_verweigerungsrate_range(self):
        from metrics import antwort_verweigerungsrate
        r = antwort_verweigerungsrate(PROBE)
        assert 0.0 <= r <= 1.0, f"antwort_verweigerungsrate={r} ausserhalb 0-1"

    def test_leer_gibt_float(self):
        from metrics import halluzinations_rate, quellen_genauigkeit, vollstaendigkeits_score, antwort_verweigerungsrate
        for fn in [halluzinations_rate, quellen_genauigkeit, vollstaendigkeits_score, antwort_verweigerungsrate]:
            assert isinstance(fn([]), float), f"{fn.__name__}([]) muss float zurueckgeben"
