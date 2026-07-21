"""
Task #180: Search-Tuning §535/§307/§1601 BGB — vollstaendigkeits_score ≥ 90%
Prüft: Vektoren der betroffenen Chunks vorhanden + results_171.json Vollständigkeit ≥ 90%
"""
import json
import os
import socket
import pytest

# Support both Docker (aligator) and local environment paths
_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_LOCAL_ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"
_RESULTS = f"{_ROOT}/tests/quality/results_171.json"
_LOCAL_RESULTS = f"{_LOCAL_ROOT}/tests/quality/results_171.json"

CHUNK_IDS_535 = [105790, 105791, 105792, 105793, 105794, 105795, 105806]
CHUNK_IDS_307 = [104103, 104104, 104105, 104106, 104107, 104108]
CHUNK_IDS_1601 = [111809, 111814]
ALL_CHUNKS = CHUNK_IDS_535 + CHUNK_IDS_307 + CHUNK_IDS_1601


def _pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_chunks_haben_vektoren():
    import psycopg2
    c = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = c.cursor()
    cur.execute(
        "SELECT id, title, vector IS NULL as no_vec FROM legal_chunks WHERE id = ANY(%s)",
        (ALL_CHUNKS,),
    )
    rows = cur.fetchall()
    c.close()
    assert len(rows) >= len(ALL_CHUNKS) * 0.8, f"Nur {len(rows)}/{len(ALL_CHUNKS)} Chunks gefunden"
    fehlend = [str(r[0]) for r in rows if r[2]]
    assert not fehlend, f"Chunks ohne Vektor: {', '.join(fehlend)}"


def test_vollstaendigkeits_score_90pct():
    # Try both paths (Docker and local)
    _results = _LOCAL_RESULTS if os.path.exists(_LOCAL_RESULTS) else _RESULTS
    if not os.path.exists(_results):
        pytest.skip(f"results_171.json fehlt — quality-run ausführen: python3 {_LOCAL_ROOT}/tests/quality/run_quality_171.py")
    with open(_results, encoding="utf-8") as f:
        r = json.load(f)
    score = r.get("metriken", {}).get("vollstaendigkeits_score", 0)
    assert score >= 0.90, f"vollstaendigkeits_score {score:.1%} < 90% — Search-Tuning unvollständig"
