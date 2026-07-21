import socket, os
import numpy as np
import pytest

CANDIDATE = os.getenv("LEGAL_EMBEDDING_MODEL", "sentence-transformers/paraphrase-multilingual-mpnet-base-v2")
QUERY = "Betriebszugehörigkeit Kündigungsfrist"


def _pg_ok():
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_legal_model_ranks_622_bgb_in_top5():
    import psycopg2
    from sentence_transformers import SentenceTransformer

    c = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = c.cursor()
    cur.execute("SELECT id, text FROM legal_chunks WHERE text ILIKE '%§ 622%' OR text ILIKE '%§622%' LIMIT 10")
    targets = cur.fetchall()
    cur.execute("SELECT id, text FROM legal_chunks WHERE (text ILIKE '%Kündigung%' OR text ILIKE '%Kündigungsschutz%' OR text ILIKE '%Arbeitnehmer%') AND NOT (text ILIKE '%§ 622%' OR text ILIKE '%§622%') ORDER BY id LIMIT 300")
    noise = cur.fetchall()
    c.close()
    assert targets, "Keine §622-BGB-Chunks in DB — Testdaten fehlen"

    model = SentenceTransformer(CANDIDATE)
    chunks = targets + noise
    q_vec = model.encode(QUERY, normalize_embeddings=True)
    vecs = model.encode([ch[1][:500] for ch in chunks], normalize_embeddings=True, batch_size=16)
    scores = np.dot(vecs, q_vec)
    ranked_ids = [chunks[i][0] for i in np.argsort(scores)[::-1]]
    target_ids = {ch[0] for ch in targets}
    best_rank = next(r + 1 for r, cid in enumerate(ranked_ids) if cid in target_ids)
    assert best_rank <= 5, f"§622 BGB auf Rang {best_rank} (erwartet ≤5) — {CANDIDATE} verbessert Ranking nicht ausreichend"
