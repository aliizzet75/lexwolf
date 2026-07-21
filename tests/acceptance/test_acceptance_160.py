import json
import os
import sys
import pytest
import psycopg2

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
sys.path.insert(0, os.path.join(_ROOT, "backend"))

EVAL_PATH = os.path.join(_ROOT, "tests/quality/embedding_eval.json")
MRR_THRESHOLD = 0.15
PG_DSN = "postgresql://postgres:postgres@localhost:5432/lexwolf"


def _pg_ok():
    import socket
    s = socket.socket()
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


def _load_eval():
    with open(EVAL_PATH) as f:
        return json.load(f)


def test_t159_precondition_mrr_threshold_not_met():
    """T#160 darf NUR gestartet werden wenn T#159 MRR-Delta > +0.15 ergab.
    Da das Delta negativ ist (change_model=False), ist T#160 geblockt."""
    assert os.path.exists(EVAL_PATH), f"embedding_eval.json fehlt: {EVAL_PATH}"
    data = _load_eval()
    rec = data.get("recommendation", {})
    assert rec.get("change_model") is False, (
        f"T#159 empfiehlt Modellwechsel — T#160 wäre aktiv. Prüfe MRR-Delta."
    )
    results = data.get("results", {})
    baseline_mrr = data["baseline_mrr"]
    best_candidate_mrr = max(
        v["mrr"] for k, v in results.items()
        if k != data["baseline_model"]
    )
    delta = best_candidate_mrr - baseline_mrr
    assert delta < MRR_THRESHOLD, (
        f"MRR-Delta {delta:+.4f} überschreitet Schwelle +{MRR_THRESHOLD} → "
        f"T#160 sollte aktiv sein, aber dieser Test erwartet Blockierung."
    )
    pytest.skip(
        f"VORBEDINGUNG NICHT ERFÜLLT: T#159 MRR-Delta={delta:+.4f} "
        f"(Schwelle: +{MRR_THRESHOLD}). Re-Embedding nicht notwendig."
    )


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_chunk_count_in_db():
    """Stellt sicher dass die aktuelle Chunk-Anzahl bekannt ist (Baseline für T#160)."""
    conn = psycopg2.connect(PG_DSN)
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE vector IS NOT NULL;")
    count = cur.fetchone()[0]
    conn.close()
    assert count > 0, "Keine Chunks mit Vektoren in legal_chunks"
    # Kein Re-Embedding nötig — Wert nur als Referenz erfassen
    print(f"\nAktuelle Chunks mit Embeddings: {count}")
