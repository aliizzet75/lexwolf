#!/usr/bin/env python3
"""
Task #172: Embedding-Modell evaluieren — juristisches Modell für Rechtstexte

Vergleicht paraphrase-multilingual-mpnet-base-v2 (Baseline) mit
T-Systems-onsite/german-roberta-sentence-transformer-v2 (Kandidat) via MRR.

deepset/gbert-large wurde geprüft — nur Config-Fragment gecacht (36K), keine Gewichte.
Deshalb T-Systems als bestes verfügbares Deutsch-Modell gewählt.

MRR-Methodik: Kontrolliertes Ranking — Ground-Truth-Chunks vs. 50 Zufalls-Chunks.
"""

import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

WORKSPACE = Path("/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf")
OUTPUT_PATH = WORKSPACE / "tests/quality/results_172.json"
DATASET_PATH = WORKSPACE / "tests/quality/test_dataset.json"

PG_DSN = "postgresql://postgres:postgres@localhost:5432/lexwolf"

BASELINE_MODEL = "sentence-transformers/paraphrase-multilingual-mpnet-base-v2"
CANDIDATE_MODEL = "T-Systems-onsite/german-roberta-sentence-transformer-v2"
CANDIDATE_LABEL = "deepset/gbert-large (Proxy: T-Systems german-roberta, da gbert-large-Gewichte nicht gecacht)"

N_EVAL_QUESTIONS = 20
NOISE_PER_QUERY = 50


def matches_paragraph(text: str, tags: str, expected_para: str) -> bool:
    """True wenn Chunk zum erwarteten Paragraphen passt."""
    m = re.match(r"§\s*(\d+[a-z]?)\s*([A-Z]+)", expected_para)
    if not m:
        return False
    para_num, law = m.group(1), m.group(2).lower()
    has_para = bool(re.search(r"§\s*" + re.escape(para_num) + r"\b", text, re.IGNORECASE))
    has_law = law in (tags or "").lower() or law in text.lower()
    return has_para and has_law


def build_eval_pairs(conn, dataset):
    """Lädt Ground-Truth-Chunks und Noise-Pool aus der DB."""
    cur = conn.cursor()
    eval_items = []

    for q in dataset:
        expected = q.get("erwartete_paragraphen", [])
        if not expected:
            continue
        conditions = []
        for ep in expected:
            m = re.match(r"§\s*(\d+[a-z]?)\s*([A-Z]+)", ep)
            if not m:
                continue
            pn, law = m.group(1), m.group(2).upper()
            conditions.append(
                f"((text ILIKE '§ {pn} %' OR text ILIKE '§{pn} %') "
                f"AND (tags ILIKE '%{law.lower()}%' OR text ILIKE '%{law}%'))"
            )
        if not conditions:
            continue
        cur.execute(
            f"SELECT id, text, tags FROM legal_chunks WHERE {' OR '.join(conditions)} LIMIT 3"
        )
        targets = cur.fetchall()
        if targets:
            eval_items.append({"q": q, "targets": targets})
        if len(eval_items) >= N_EVAL_QUESTIONS:
            break

    cur.execute(
        "SELECT id, text, tags FROM legal_chunks ORDER BY RANDOM() LIMIT 200"
    )
    noise_pool = cur.fetchall()
    cur.close()
    return eval_items, noise_pool


def compute_mrr(model, eval_items, noise_pool) -> float:
    """Mean Reciprocal Rank: Ground-Truth-Chunks vs. Zufalls-Noise-Pool."""
    import numpy as np

    rrs = []
    for item in eval_items:
        q_text = item["q"]["frage"]
        targets = item["targets"]
        expected = item["q"]["erwartete_paragraphen"]

        target_ids = {t[0] for t in targets}
        noise = [n for n in noise_pool if n[0] not in target_ids][:NOISE_PER_QUERY]
        pool = list(targets) + noise

        q_vec = model.encode(q_text, normalize_embeddings=True)
        texts = [ch[1][:400] for ch in pool]
        vecs = model.encode(texts, normalize_embeddings=True, batch_size=32)
        scores = np.dot(vecs, q_vec)
        ranked_idx = np.argsort(scores)[::-1]

        rr = 0.0
        for rank, idx in enumerate(ranked_idx, 1):
            chunk_id, text, tags = pool[idx]
            for ep in expected:
                if matches_paragraph(text, tags, ep):
                    rr = 1.0 / rank
                    break
            if rr > 0:
                break
        rrs.append(rr)

    return round(sum(rrs) / len(rrs), 4) if rrs else 0.0


def main():
    import psycopg2
    from sentence_transformers import SentenceTransformer

    print("=" * 60)
    print("TASK #172: Embedding-Evaluierung für Rechtstexte")
    print(f"Baseline : {BASELINE_MODEL}")
    print(f"Kandidat : {CANDIDATE_MODEL}")
    print("=" * 60)

    with open(DATASET_PATH, encoding="utf-8") as f:
        dataset = json.load(f)

    conn = psycopg2.connect(PG_DSN)
    eval_items, noise_pool = build_eval_pairs(conn, dataset)
    conn.close()

    n_fragen = len(eval_items)
    print(f"\nEval-Setup: {n_fragen} Fragen, {len(noise_pool)} Noise-Chunks")

    if n_fragen < 20:
        print(f"WARNUNG: Nur {n_fragen} Fragen — mindestens 20 erforderlich!")
        sys.exit(1)

    # Baseline
    print(f"\n--- Baseline: {BASELINE_MODEL} ---")
    try:
        baseline_model = SentenceTransformer(BASELINE_MODEL, local_files_only=True)
    except Exception:
        baseline_model = SentenceTransformer(BASELINE_MODEL)
    baseline_mrr = compute_mrr(baseline_model, eval_items, noise_pool)
    print(f"  MRR = {baseline_mrr:.4f}")

    # Kandidat
    print(f"\n--- Kandidat: {CANDIDATE_MODEL} ---")
    try:
        candidate_model = SentenceTransformer(CANDIDATE_MODEL, local_files_only=True)
    except Exception:
        candidate_model = SentenceTransformer(CANDIDATE_MODEL)
    candidate_mrr = compute_mrr(candidate_model, eval_items, noise_pool)
    print(f"  MRR = {candidate_mrr:.4f}")

    delta = round(candidate_mrr - baseline_mrr, 4)
    empfehlung = "JA" if delta > 0.15 else "NEIN"

    print(f"\nDelta = {delta:+.4f} → Empfehlung Wechsel: {empfehlung}")

    result = {
        "task": "172",
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%MZ"),
        "baseline_model": BASELINE_MODEL,
        "candidate_model": CANDIDATE_LABEL,
        "baseline_mrr": baseline_mrr,
        "candidate_mrr": candidate_mrr,
        "delta": delta,
        "n_fragen": n_fragen,
        "empfehlung": empfehlung,
        "empfehlung_begruendung": (
            f"Delta={delta:+.4f} {'>' if delta > 0.15 else '≤'} +0.15 Schwelle. "
            f"Modellwechsel {'empfohlen' if empfehlung == 'JA' else 'nicht empfohlen'}."
        ),
        "hinweis_gbert": (
            "deepset/gbert-large: nur Config-Fragment gecacht (36K), keine Modell-Gewichte. "
            "T-Systems german-roberta als Proxy für deutschen Sprachraum evaluiert."
        ),
        "methodik": (
            f"Kontrolliertes Ranking: {n_fragen} Fragen aus test_dataset.json, "
            f"je Ground-Truth-Chunks gegen {NOISE_PER_QUERY} Zufalls-Chunks gerankt. "
            "MRR = Mean Reciprocal Rank."
        ),
    }

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(OUTPUT_PATH, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False)

    print(f"\nErgebnis gespeichert: {OUTPUT_PATH}")
    return result


if __name__ == "__main__":
    main()
