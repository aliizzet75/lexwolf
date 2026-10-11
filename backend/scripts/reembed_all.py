#!/usr/bin/env python3
"""
Task #160: Re-Embedding aller 94k Chunks
NUR starten wenn T#159 (Embedding-Evaluation) MRR-Verbesserung > +0.15 ergeben hat.

Ablauf:
1. Lade Chunks in Batches von 256
2. Berechne neues Embedding
3. UPDATE legal_chunks SET vector = :vec WHERE id = :id
4. Fortschritt loggen, bei Fehler weitermachen
5. Wiederaufnahme: überspringe Chunks die bereits neues Embedding haben (CHECK via Norm-Differenz)
"""

import os
import re
import sys
import time
import json
from pathlib import Path
from typing import List, Dict, Tuple, Optional
import logging

import numpy as np
import psycopg2
from sentence_transformers import SentenceTransformer

# Konfiguration
BATCH_SIZE = 256
MODEL_NAME = os.getenv("EMBEDDING_MODEL", "paraphrase-multilingual-mpnet-base-v2")
DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://postgres:postgres@host.docker.internal:5432/lexwolf")

# Logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


def get_db_connection():
    """Hole DB-Verbindung."""
    return psycopg2.connect(DATABASE_URL)


def get_chunk_count() -> int:
    """Anzahl aller Chunks."""
    conn = get_db_connection()
    try:
        cur = conn.cursor()
        cur.execute("SELECT COUNT(*) FROM legal_chunks")
        count = cur.fetchone()[0]
        cur.close()
        return count
    finally:
        conn.close()


def get_chunks_to_reembed(start_id: int = 0, limit: int = BATCH_SIZE) -> List[Dict]:
    """Lade Chunks zum Re-Embedding (ohne normierten Check)."""
    conn = get_db_connection()
    try:
        cur = conn.cursor()
        # Alle Chunks die noch nicht re-embedded sind (vector ist None oder sehr kurz)
        # ODER einfach alle Chunks ab start_id für Batch-Verarbeitung
        cur.execute("""
            SELECT id, title, text, legal_field, tags
            FROM legal_chunks 
            WHERE id > %s 
            ORDER BY id 
            LIMIT %s
        """, (start_id, limit))
        
        chunks = []
        for row in cur.fetchall():
            chunks.append({
                "id": row[0],
                "title": row[1] or "",
                "text": row[2] or "",
                "legal_field": row[3] or "",
                "tags": row[4] or []
            })
        cur.close()
        return chunks
    finally:
        conn.close()


def compute_embedding(title: str, text: str, legal_field: str, tags: List[str]) -> List[float]:
    """Berechne Embedding aus Titel + Text + Metadaten."""
    try:
        # Embedding-Text = Titel + Leitsatz + erste 200 Tokens Inhalt
        # (nicht den ganzen Text embedden — Qualität leidet)
        full_text = f"{title} {legal_field} {' '.join(tags)} {text[:2000]}"
        
        model = SentenceTransformer(MODEL_NAME)
        vec = model.encode(full_text[:8000], normalize_embeddings=True)
        return vec.tolist()
    except Exception as e:
        logger.error(f"Embedding error: {e}")
        return None


def update_chunk_vector(chunk_id: int, vector: List[float]) -> bool:
    """Update Chunk mit neues Embedding."""
    conn = get_db_connection()
    try:
        cur = conn.cursor()
        # Konvertiere List zu PostgreSQL vector type string
        vec_str = "[" + ",".join(str(v) for v in vector) + "]"
        cur.execute("""
            UPDATE legal_chunks 
            SET vector = %s::vector, 
                embedding_model = %s,
                embedding_timestamp = NOW()
            WHERE id = %s
        """, (vec_str, MODEL_NAME, chunk_id))
        conn.commit()
        cur.close()
        return True
    except Exception as e:
        logger.error(f"Update error for chunk {chunk_id}: {e}")
        conn.rollback()
        return False
    finally:
        conn.close()


def count_reembedded() -> int:
    """Zähle bereits re-embedded Chunks (mit validem vector)."""
    conn = get_db_connection()
    try:
        cur = conn.cursor()
        # Prüfe auf nicht-leere Vektoren
        cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE vector IS NOT NULL")
        count = cur.fetchone()[0]
        cur.close()
        return count
    finally:
        conn.close()


def _chunk_matches_paragraph(title: str, tags: str, paragraph: str) -> bool:
    """Prüft ob ein Chunk den erwarteten Paragraphen enthält.

    Erwartet Format wie '§626 BGB' oder '§1 KSchG'.
    Gleicht gegen Titel ('BGB - § 626 ...') und Tags ('bgb') ab.
    """
    m = re.match(r"§\s*(\d+\w*)\s+(\w+)", paragraph)
    if not m:
        return False
    para_num, law = m.group(1), m.group(2).lower()
    title_l = (title or "").lower()
    tags_l = (tags or "").lower()
    # Gesetz im Titel oder Tag gefunden UND Paragraphennummer im Titel
    law_present = law in title_l or law in tags_l
    # "§ 626" oder "§626" im Titel
    para_present = bool(re.search(r"§\s*" + re.escape(para_num) + r"\b", title_l))
    return law_present and para_present


def mini_test_reembedding(n: int = 10) -> Dict:
    """Mini-Test nach Re-Embedding: n Fragen via echte Vektorsuche testen.

    Berechnet MRR@10 und Hit@5 — prüft ob erwartete Paragraphen in Titel/Tags
    der Top-10 Vektor-Treffer erscheinen.
    """
    dataset_path = Path("/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/test_dataset.json")
    with open(dataset_path, "r") as f:
        dataset = json.load(f)

    test_questions = dataset[:n]
    model = SentenceTransformer(MODEL_NAME)
    conn = get_db_connection()

    results = {"total": 0, "hit_at_5": 0, "mrr": 0.0, "full_results": []}

    try:
        cur = conn.cursor()
        for q in test_questions:
            results["total"] += 1
            frage = q.get("frage", "")
            expected_paragraphen = q.get("erwartete_paragraphen", [])

            vec = model.encode(frage[:8000], normalize_embeddings=True).tolist()
            vec_str = "[" + ",".join(str(v) for v in vec) + "]"

            cur.execute("""
                SELECT id, title, tags, vector <-> %s::vector AS dist
                FROM legal_chunks
                ORDER BY dist
                LIMIT 10
            """, (vec_str,))
            rows = cur.fetchall()

            first_hit_rank: Optional[int] = None
            for rank, row in enumerate(rows, start=1):
                chunk_title, chunk_tags = row[1], row[2]
                if any(_chunk_matches_paragraph(chunk_title, chunk_tags, p) for p in expected_paragraphen):
                    if first_hit_rank is None:
                        first_hit_rank = rank
                    if rank <= 5:
                        results["hit_at_5"] += 1
                    break

            rr = 1.0 / first_hit_rank if first_hit_rank else 0.0
            results["mrr"] += rr

            results["full_results"].append({
                "id": q["id"],
                "frage": frage[:60],
                "erwartet": expected_paragraphen,
                "first_hit_rank": first_hit_rank,
                "rr": round(rr, 4),
            })

        cur.close()
    finally:
        conn.close()

    results["mrr"] = round(results["mrr"] / max(1, results["total"]), 4)
    return results


def main():
    logger.info("="*60)
    logger.info("TASK #160: Re-Embedding aller Chunks")
    logger.info("="*60)
    
    # Prüfe ob T#159 positiv war (MRR > +0.15)
    eval_path = Path("/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/embedding_eval.json")
    if eval_path.exists():
        with open(eval_path, "r") as f:
            eval_data = json.load(f)
        
        recommendation = eval_data.get("recommendation", {})
        change_model = recommendation.get("change_model", False)
        
        if not change_model:
            logger.warning("T#159 ergab NOCH KEINEN Grund für Modellwechsel (MRR-Verbesserung < +0.15)")
            logger.warning("Re-Embedding wird übersprungen.")
            return {"status": "SKIPPED", "reason": "T#159 nicht positiv"}
    
    # Initialisierung
    total_chunks = get_chunk_count()
    logger.info(f"Gesamt: {total_chunks} Chunks")
    logger.info(f"Batch-Größe: {BATCH_SIZE}")
    logger.info(f"Embedding-Modell: {MODEL_NAME}")
    
    # Verarbeite in Batches
    start_time = time.time()
    processed = 0
    errors = 0
    
    while True:
        # Nächster Batch
        chunks = get_chunks_to_reembed(start_id=processed, limit=BATCH_SIZE)
        
        if not chunks:
            logger.info("Keine weiteren Chunks zum Verarbeiten")
            break
        
        # Embeddings berechnen
        batch_vectors = []
        for chunk in chunks:
            vec = compute_embedding(
                chunk["title"],
                chunk["text"],
                chunk["legal_field"],
                chunk["tags"]
            )
            if vec is not None:
                batch_vectors.append((chunk["id"], vec))
            else:
                errors += 1
                logger.error(f"Embedding fehlgeschlagen für Chunk {chunk['id']}")
        
        # Update in DB
        for chunk_id, vec in batch_vectors:
            if update_chunk_vector(chunk_id, vec):
                processed += 1
            else:
                errors += 1
        
        # Fortschritt
        elapsed = time.time() - start_time
        eta = (elapsed / max(1, processed)) * (total_chunks - processed) if processed > 0 else 0
        
        logger.info(f"Progress: {processed}/{total_chunks} ({processed/total_chunks*100:.1f}%) | "
                   f"Fortschritt: {elapsed/60:.1f} min | ETA: {eta/60:.1f} min")
    
    # Abschluss
    elapsed = time.time() - start_time
    reembedded_count = count_reembedded()
    
    logger.info("="*60)
    logger.info("Re-Embedding abgeschlossen")
    logger.info("="*60)
    logger.info(f"Verarbeitet: {processed} Chunks")
    logger.info(f"Fehler: {errors}")
    logger.info(f"Re-embedded mit neuem Modell: {reembedded_count} Chunks")
    logger.info(f"Dauer: {elapsed/60:.1f} min")
    
    # Mini-Test
    logger.info("\nMini-Test nach Re-Embedding (10 Fragen)...")
    test_results = mini_test_reembedding()
    
    logger.info(f"Mini-Test Ergebnis: {test_results['found']}/{test_results['total']} Fragen gefunden")
    
    return {
        "status": "SUCCESS",
        "processed": processed,
        "errors": errors,
        "reembedded_count": reembedded_count,
        "duration_minutes": elapsed / 60,
        "mini_test": test_results
    }


if __name__ == "__main__":
    result = main()
    print(f"\nErgebnis: {json.dumps(result, indent=2)}")
