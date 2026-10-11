"""
Re-indexiert BGB-Chunks mit feinerem Absatz-Splitting.
Löscht alle bestehenden BGB-Chunks und crawlt BGB neu.
"""
import sys
import os
import hashlib
import logging

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger(__name__)

import psycopg2
from crawlers.gesetze_crawler import GesetzeImInternetCrawler
from services.embedding_service import EmbeddingService

DB_URL = os.getenv("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")


def get_conn():
    return psycopg2.connect(DB_URL)


def delete_bgb_chunks(conn):
    with conn.cursor() as cur:
        cur.execute("DELETE FROM legal_chunks WHERE tags='bgb'")
        deleted = cur.rowcount
    conn.commit()
    logger.info(f"Gelöscht: {deleted} BGB-Chunks")
    return deleted


def save_chunk(conn, chunk: dict, vector: list):
    """Speichert einen Chunk direkt via psycopg2 (umgeht Dedup-Hash-Check)."""
    with conn.cursor() as cur:
        # document_id: Hol oder erzeuge das Dokument
        cur.execute("SELECT id FROM legal_documents WHERE url = %s LIMIT 1", (chunk["url"],))
        row = cur.fetchone()
        if row:
            doc_id = row[0]
        else:
            cur.execute(
                """INSERT INTO legal_documents (title, content, url, source, document_type, created_at, updated_at)
                   VALUES (%s, %s, %s, %s, %s, NOW(), NOW()) RETURNING id""",
                (chunk["title"], "", chunk["url"], chunk["source"], chunk["document_type"])
            )
            doc_id = cur.fetchone()[0]

        cur.execute(
            """INSERT INTO legal_chunks
               (document_id, text, vector, title, court, case_number, date,
                legal_field, tags, chunk_hash, is_parent, created_at)
               VALUES (%s, %s, %s::vector, %s, %s, %s, %s, %s, %s, %s, FALSE, NOW())""",
            (
                doc_id,
                chunk["text"],
                "[" + ",".join(str(x) for x in vector) + "]",
                chunk["title"],
                chunk.get("court", ""),
                chunk.get("case_number", ""),
                None,
                chunk.get("legal_field", ""),
                chunk["tags"],
                chunk["chunk_hash"],
            )
        )
    conn.commit()


def main():
    logger.info("=== BGB Re-Indexierung gestartet ===")

    conn = get_conn()
    logger.info("DB-Verbindung OK")

    # 1. Alte BGB-Chunks löschen
    delete_bgb_chunks(conn)

    # 2. BGB crawlen (nur BGB)
    crawler = GesetzeImInternetCrawler()
    logger.info("Lade BGB-TOC …")
    all_laws = crawler._get_toc()
    bgb_laws = [l for l in all_laws if l["abk"] == "bgb"]
    if not bgb_laws:
        logger.error("BGB nicht im TOC gefunden!")
        sys.exit(1)

    bgb_law = bgb_laws[0]
    logger.info(f"BGB gefunden: {bgb_law['title']} → {bgb_law['link']}")

    chunks = crawler._download_chunks(bgb_law)
    logger.info(f"Chunks nach Splitting: {len(chunks)}")

    # Größenverteilung prüfen
    over_2000 = sum(1 for c in chunks if len(c["text"]) > 2000)
    logger.info(f"Chunks > 2000 Zeichen: {over_2000}")

    # 3. Embeddings generieren und speichern
    embedding_svc = EmbeddingService()
    logger.info("Generiere Embeddings (das dauert einige Minuten) …")

    BATCH_SIZE = 64
    saved = 0
    for batch_start in range(0, len(chunks), BATCH_SIZE):
        batch = chunks[batch_start:batch_start + BATCH_SIZE]
        texts = [c["text"] for c in batch]
        try:
            vectors = embedding_svc.generate_embeddings_batch(texts)
        except Exception as e:
            logger.error(f"Embedding-Fehler Batch {batch_start}: {e}")
            vectors = [[0.0] * 768] * len(batch)

        for chunk, vector in zip(batch, vectors):
            try:
                save_chunk(conn, chunk, vector)
                saved += 1
            except Exception as e:
                logger.error(f"DB-Fehler Chunk '{chunk.get('title','?')}': {e}")

        if (batch_start + BATCH_SIZE) % 500 < BATCH_SIZE:
            logger.info(f"  {min(batch_start + BATCH_SIZE, len(chunks))}/{len(chunks)} verarbeitet …")

    conn.close()
    logger.info(f"=== BGB Re-Indexierung abgeschlossen: {saved}/{len(chunks)} gespeichert ===")

    # 4. Verifikation
    conn2 = get_conn()
    with conn2.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags='bgb'")
        total = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags='bgb' AND title ILIKE '%1601%'")
        cnt_1601 = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags='bgb' AND title ILIKE '%535%'")
        cnt_535 = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags='bgb' AND LENGTH(text) > 2000")
        over2000 = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags='bgb' AND vector IS NULL")
        no_vec = cur.fetchone()[0]
    conn2.close()

    print("\n=== DOD-Verifikation ===")
    print(f"Total BGB-Chunks: {total}")
    print(f"§1601 Chunks: {cnt_1601} (required >= 3) {'✅' if cnt_1601 >= 3 else '❌'}")
    print(f"§535  Chunks: {cnt_535}  (required >= 8) {'✅' if cnt_535 >= 8 else '❌'}")
    print(f"Chunks > 2000 Zeichen: {over2000} (required = 0) {'✅' if over2000 == 0 else '❌'}")
    print(f"Chunks ohne Vector: {no_vec} (required = 0) {'✅' if no_vec == 0 else '❌'}")


if __name__ == "__main__":
    main()
