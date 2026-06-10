import json
import os
import socket
import sys
import pytest

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_DISABLED = "/data/.openclaw/workspace-codex/projects/lexwolf.disabled"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST_DISABLED = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf.disabled"
_ROOT = (
    _BASE if os.path.isdir(_BASE) else
    _BASE_DISABLED if os.path.isdir(_BASE_DISABLED) else
    _BASE_HOST if os.path.isdir(_BASE_HOST) else
    _BASE_HOST_DISABLED
)
sys.path.insert(0, os.path.join(_ROOT, "backend"))

DATASET_PATH = f"{_ROOT}/tests/quality/test_dataset.json"
RESULTS_PATH = f"{_ROOT}/tests/quality/results.json"
SCRIPT_PATH = f"{_ROOT}/tests/quality/run_live_quality_test.py"
JUDGE_PATH = f"{_ROOT}/backend/llm_judge.py"


def _pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


def _neo4j_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 7687))
    s.close()
    return r == 0


def test_script_exists():
    assert os.path.exists(SCRIPT_PATH), f"run_live_quality_test.py fehlt: {SCRIPT_PATH}"


def test_llm_judge_exists_und_importierbar():
    assert os.path.exists(JUDGE_PATH), f"llm_judge.py fehlt: {JUDGE_PATH}"
    import llm_judge
    assert callable(llm_judge.judge_answer)


def test_judge_answer_gibt_korrektes_format():
    import llm_judge
    result = llm_judge.judge_answer("Testfrage?", "§626 BGB regelt fristlose Kündigung.", ["§626 BGB"])
    assert set(result.keys()) >= {"score", "begruendung", "korrekte_quellen", "verifiziert"}
    assert 1 <= result["score"] <= 5
    assert isinstance(result["korrekte_quellen"], list)


def test_dataset_100_fragen():
    assert os.path.exists(DATASET_PATH), f"test_dataset.json fehlt"
    with open(DATASET_PATH, encoding="utf-8") as f:
        data = json.load(f)
    assert len(data) >= 100, f"Nur {len(data)} Fragen, 100 erwartet"


def test_results_metriken_dod():
    if not os.path.exists(RESULTS_PATH):
        pytest.skip("results.json noch nicht erzeugt (Test-Run ausstehend)")
    with open(RESULTS_PATH, encoding="utf-8") as f:
        r = json.load(f)
    ergebnisse = r.get("ergebnisse", [])
    if len(ergebnisse) < 100:
        pytest.skip(f"Test-Run noch nicht vollständig: {len(ergebnisse)}/100 Fragen")
    m = r.get("metriken", {})
    assert m.get("vollstaendigkeits_score", 0) >= 0.75, f"Vollständigkeit {m.get('vollstaendigkeits_score')} < 0.75"
    assert m.get("halluzinations_rate", 1) <= 0.05, f"Halluzination {m.get('halluzinations_rate')} > 0.05"
    assert m.get("quellen_genauigkeit", 0) >= 0.90, f"Quellen-Genauigkeit {m.get('quellen_genauigkeit')} < 0.90"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
@pytest.mark.skipif(not _neo4j_ok(), reason="Neo4j nicht erreichbar auf port 7687")
def test_db_verbindungen():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks")
    count = cur.fetchone()[0]
    conn.close()
    assert count > 0, "legal_chunks Tabelle ist leer"
