import json
import os
import socket
import pytest

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf.disabled"
_RESULTS = f"{_ROOT}/tests/quality/results_171.json"


def _svc_ok(port):
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", port))
    s.close()
    return r == 0


SKIP_INFRA = pytest.mark.skipif(
    not (_svc_ok(5432) and _svc_ok(11434)),
    reason="PostgreSQL (5432) oder Ollama (11434) nicht erreichbar",
)


@SKIP_INFRA
def test_dod_gate_m12_vollstaendigkeit_90pct():
    if not os.path.exists(_RESULTS):
        pytest.skip(f"results_171.json fehlt — bitte quality-run ausführen: cd {_ROOT} && python3 tests/quality/run_quality_171.py")
    with open(_RESULTS, encoding="utf-8") as f:
        r = json.load(f)
    assert len(r.get("ergebnisse", [])) >= 20, "Weniger als 20 Fragen getestet"
    m = r.get("metriken", {})
    assert m.get("vollstaendigkeits_score", 0) >= 0.90, f"Vollständigkeit {m.get('vollstaendigkeits_score', 0):.1%} < 90% (DOD)"
    assert m.get("halluzinations_rate", 1) <= 0.05, f"Halluzination {m.get('halluzinations_rate', 1):.1%} > 5% (DOD)"
    for e in r.get("ergebnisse", []):
        pars = e.get("erwartete_paragraphen", [])
        score = e.get("judge_score", 5)
        if any(p in ["§1601 BGB", "§1603 BGB"] for p in pars):
            assert score >= 3, f"§1601/§1603 Score={score} für: {e['frage'][:60]}"
        if any(p in ["§535 BGB", "§307 BGB"] for p in pars):
            assert score >= 3, f"§535/§307 Score={score} für: {e['frage'][:60]}"
