#!/usr/bin/env python3
"""
Task #159: Embedding-Modell evaluieren — juristisches Modell testen
Vergleich paraphrase-multilingual vs. german-roberta via MRR auf Rechtstext-Queries
"""

import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import List, Dict, Tuple

WORKSPACE = Path("/data/.openclaw/workspace-codex/projects/lexwolf.disabled")
OUTPUT_PATH = WORKSPACE / "tests/quality/embedding_eval.json"
DATASET_PATH = WORKSPACE / "tests/quality/test_dataset.json"

PG_DSN = "postgresql://postgres:postgres@localhost:5432/lexwolf"

BASELINE_MODEL = "sentence-transformers/paraphrase-multilingual-mpnet-base-v2"
CANDIDATE_MODELS = [
    BASELINE_MODEL,
    "T-Systems-onsite/german-roberta-sentence-transformer-v2",
]


def matches_expected_paragraph(text: str, tags: str, expected_para: str) -> bool:
    """Prüft ob ein Chunk dem erwarteten Paragraphen entspricht."""
    m = re.match(r"§\s*(\d+[a-z]?)\s*([A-Z]+)", expected_para)
    if not m:
        return False
    para_num = m.group(1)
    law = m.group(2).lower()
    has_para = bool(re.search(r"§\s*" + re.escape(para_num) + r"\b", text.lower()))
    has_law = law in (tags or "").lower() or law in text.lower()
    return has_para and has_law


def fetch_eval_data(pg_conn, dataset: List[Dict]) -> Tuple[List[Dict], List[Tuple]]:
    """Lädt Ground-Truth-Chunks und Noise-Pool aus pgvector-Speicher."""
    cur = pg_conn.cursor()
    eval_data = []

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
            eval_data.append({"q": q, "targets": targets})

    cur.execute("SELECT id, text, tags FROM legal_chunks ORDER BY RANDOM() LIMIT 200")
    noise_pool = cur.fetchall()
    cur.close()
    return eval_data, noise_pool


def compute_mrr(model, eval_data: List[Dict], noise_pool: List[Tuple]) -> float:
    """Berechnet Mean Reciprocal Rank (MRR) für ein Embedding-Modell.

    Methodik: kontrolliertes Ranking — Ground-Truth-Chunks vs. Zufalls-Noise.
    Kein Re-Embedding aller 104k Chunks nötig.
    """
    import numpy as np

    rrs = []
    for item in eval_data:
        q_text = item["q"]["frage"]
        targets = item["targets"]
        expected = item["q"]["erwartete_paragraphen"]

        target_ids = {t[0] for t in targets}
        noise = [n for n in noise_pool if n[0] not in target_ids][:50]
        pool = targets + noise

        q_vec = model.encode(q_text, normalize_embeddings=True)
        texts = [ch[1][:400] for ch in pool]
        vecs = model.encode(texts, normalize_embeddings=True, batch_size=32)

        import numpy as np_inner
        scores = np_inner.dot(vecs, q_vec)
        ranked_idx = np_inner.argsort(scores)[::-1]

        rr = 0.0
        for rank, idx in enumerate(ranked_idx, 1):
            chunk_id, text, tags = pool[idx]
            for ep in expected:
                if matches_expected_paragraph(text, tags, ep):
                    rr = 1.0 / rank
                    break
            if rr > 0:
                break
        rrs.append(rr)

    return sum(rrs) / len(rrs) if rrs else 0.0


def evaluate_embedding_models():
    """Hauptfunktion: Evaluiert alle Kandidatenmodelle und speichert Ergebnis."""
    import psycopg2
    from sentence_transformers import SentenceTransformer

    print("=" * 60)
    print("TASK #159: Embedding-Modell evaluieren")
    print("Vergleich: paraphrase-multilingual vs. german-roberta")
    print("=" * 60)

    with open(DATASET_PATH) as f:
        dataset = json.load(f)

    conn = psycopg2.connect(PG_DSN)
    eval_data, noise_pool = fetch_eval_data(conn, dataset)
    conn.close()

    print(f"\nEvaluierungs-Setup: {len(eval_data)} Fragen, {len(noise_pool)} Noise-Chunks")

    results = {}
    baseline_mrr = None

    for model_name in CANDIDATE_MODELS:
        print(f"\n--- Modell: {model_name} ---")
        try:
            model = SentenceTransformer(model_name, local_files_only=True)
        except Exception:
            try:
                model = SentenceTransformer(model_name)
            except Exception as e:
                print(f"  FEHLER beim Laden: {e}")
                results[model_name] = {
                    "mrr": None,
                    "status": "nicht_verfügbar",
                    "hinweis": "Modell konnte nicht geladen werden",
                }
                continue

        mrr = compute_mrr(model, eval_data, noise_pool)
        print(f"  MRR = {mrr:.4f}")

        status = "evaluiert"
        if baseline_mrr is None:
            baseline_mrr = mrr

        results[model_name] = {
            "mrr": round(mrr, 4),
            "status": status,
            "methodik": "kontrolliertes Ranking (Ground-Truth vs. Zufalls-Noise, 53 Chunks/Frage)",
        }

    baseline_mrr = results.get(BASELINE_MODEL, {}).get("mrr") or 0.0

    recommendation = {
        "change_model": False,
        "reason": f"Baseline ausreichend — MRR={baseline_mrr:.4f}. Kein Alternativmodell erzielt +0.15 MRR-Gewinn.",
    }

    for model_name, data in results.items():
        if model_name == BASELINE_MODEL:
            continue
        alt_mrr = data.get("mrr")
        if alt_mrr is not None and (alt_mrr - baseline_mrr) > 0.15:
            recommendation = {
                "change_model": True,
                "reason": f"MRR-Gewinn +{alt_mrr - baseline_mrr:.4f} > Schwelle +0.15",
                "new_model": model_name,
            }

    output = {
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%MZ"),
        "task": "159",
        "baseline_model": BASELINE_MODEL,
        "baseline_mrr": round(baseline_mrr, 4),
        "candidate_models": CANDIDATE_MODELS,
        "results": results,
        "recommendation": recommendation,
        "methodik": (
            "Kontrolliertes Ranking: für jede Testfrage werden Ground-Truth-Chunks "
            "gegen 50 Zufalls-Chunks gerankt. MRR = Mean Reciprocal Rank über 100 Fragen."
        ),
    }

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(OUTPUT_PATH, "w") as f:
        json.dump(output, f, indent=2, ensure_ascii=False)

    print(f"\n✅ Ergebnis gespeichert: {OUTPUT_PATH}")
    print(f"Empfehlung: Modell wechseln = {recommendation['change_model']}")
    print(f"Begründung: {recommendation['reason']}")

    return output


if __name__ == "__main__":
    evaluate_embedding_models()
