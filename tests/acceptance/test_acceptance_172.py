import json
import os
import socket
import pytest

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_RESULTS = f"{_ROOT}/tests/quality/results_172.json"


def _port_ok(port):
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", port)); s.close(); return r == 0


SKIP_INFRA = pytest.mark.skipif(
    not _port_ok(5432),
    reason="PostgreSQL nicht erreichbar auf port 5432",
)


@SKIP_INFRA
def test_evaluation_results_exist():
    if not os.path.exists(_RESULTS):
        pytest.skip(
            f"results_172.json fehlt — bitte ausführen: "
            f"cd {_ROOT} && python3 tests/quality/run_eval_172.py"
        )
    with open(_RESULTS, encoding="utf-8") as f:
        r = json.load(f)
    assert "baseline_mrr" in r, "Kein baseline_mrr (paraphrase-multilingual-mpnet-base-v2)"
    assert "candidate_mrr" in r, "Kein candidate_mrr (gbert-large o.ä.)"
    assert "empfehlung" in r, "Keine Empfehlung JA/NEIN im Ergebnis"


@SKIP_INFRA
def test_mrr_comparison_meaningful():
    if not os.path.exists(_RESULTS):
        pytest.skip("results_172.json fehlt — erst run_eval_172.py ausführen")
    with open(_RESULTS, encoding="utf-8") as f:
        r = json.load(f)
    assert r.get("n_fragen", 0) >= 20, f"Weniger als 20 Fragen ausgewertet: {r.get('n_fragen')}"
    baseline = r["baseline_mrr"]
    candidate = r["candidate_mrr"]
    delta = candidate - baseline
    empfehlung = r["empfehlung"]
    if delta > 0.15:
        assert empfehlung == "JA", f"Delta={delta:.3f} > 0.15 aber Empfehlung='{empfehlung}'"
    else:
        assert empfehlung == "NEIN", f"Delta={delta:.3f} ≤ 0.15 aber Empfehlung='{empfehlung}'"
