import asyncio
import os
from datetime import datetime
from typing import List, Dict
from crawlers.gesetze_crawler import GesetzeImInternetCrawler
from crawlers.ris_crawler import RisCrawler
from services.embedding_service import EmbeddingService, MockEmbeddingService
from services.database_service import DatabaseService
from services.neo4j_service import Neo4jService
from services.paragraph_parser import extract_references
from scripts.import_to_neo4j import create_paragraph_node, create_simple_relationships
import hashlib
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

NEO4J_URI = os.getenv("NEO4J_URI", "bolt://localhost:7687")
NEO4J_USER = os.getenv("NEO4J_USER", "neo4j")
NEO4J_PASSWORD = os.getenv("NEO4J_PASSWORD", "lexwolf123")


def update_neo4j(new_chunks: List[Dict]):
    """Update Neo4j graph with new chunks using MERGE to prevent duplicates."""
    if not new_chunks:
        return
    svc = Neo4jService(uri=NEO4J_URI, user=NEO4J_USER, password=NEO4J_PASSWORD)
    if not svc.connect():
        logger.warning("Neo4j not reachable — skipping graph update")
        return
    try:
        svc.setup_schema()
        for chunk in new_chunks:
            refs = extract_references(chunk.get("text", ""))
            create_paragraph_node(svc, chunk, refs)
            if refs:
                create_simple_relationships(svc, chunk, refs)
        logger.info(f"Neo4j updated with {len(new_chunks)} chunks")
    except Exception as e:
        logger.error(f"Neo4j update failed: {e}")
    finally:
        svc.close()


class LegalDatabaseCrawler:
    """Main orchestrator for crawling legal databases."""

    def __init__(self):
        self.gesetze_crawler = GesetzeImInternetCrawler()
        self.ris_crawler = RisCrawler()
        self.embedding_service = EmbeddingService()
        self.database_service = DatabaseService()

    async def crawl_all_sources(self, limit_per_source: int = None):
        """Crawl all legal sources, store in pgvector, then update Neo4j."""
        logger.info("Starting legal database crawl...")
        all_new_chunks = []

        logger.info("Crawling gesetze-im-internet.de...")
        try:
            law_chunks = self.gesetze_crawler.crawl_laws(limit=limit_per_source)
            logger.info(f"Generated {len(law_chunks)} chunks from laws")
            new_chunks = await self.process_and_store_chunks(law_chunks)
            all_new_chunks.extend(new_chunks)
        except Exception as e:
            logger.error(f"Error crawling gesetze-im-internet.de: {e}")

        logger.info("Crawling rechtsprechung-im-internet.de (BVerfG, BGH, BAG, BFH, BVerwG, BSG, BPatG)...")
        try:
            decision_chunks = self.ris_crawler.crawl_decisions(days=1, limit=limit_per_source)
            logger.info(f"Generated {len(decision_chunks)} chunks from decisions")
            new_chunks = await self.process_and_store_chunks(decision_chunks)
            all_new_chunks.extend(new_chunks)
        except Exception as e:
            logger.error(f"Error crawling rechtsprechung-im-internet.de: {e}")

        if all_new_chunks:
            logger.info(f"Updating Neo4j with {len(all_new_chunks)} new chunks...")
            update_neo4j(all_new_chunks)

        logger.info("Legal database crawl completed")

    def _embed_and_save(self, chunk: Dict) -> Dict:
        """Embed + store one chunk, return chunk with 'id' set."""
        text = chunk.get("text", "")
        chunk["chunk_hash"] = hashlib.md5(text.encode()).hexdigest()
        chunk["vector"] = self.embedding_service.generate_embedding(text)
        chunk.setdefault("created_at", datetime.utcnow().isoformat())
        saved = self.database_service.save_chunk(chunk, chunk["vector"])
        if saved and "id" in saved:
            chunk["id"] = saved["id"]
        return chunk

    def _known_hashes(self, chunks: List[Dict]) -> set:
        """Holt alle bereits bekannten chunk_hashes aus der DB — ein einziger Query."""
        hashes = {hashlib.md5(c.get("text","").encode()).hexdigest() for c in chunks}
        if not hashes:
            return set()
        try:
            db = self.database_service.SessionLocal()
            from sqlalchemy import text as sqltxt
            result = db.execute(
                sqltxt("SELECT chunk_hash FROM legal_chunks WHERE chunk_hash = ANY(:h)"),
                {"h": list(hashes)}
            )
            known = {row[0] for row in result}
            db.close()
            return known
        except Exception as e:
            logger.warning(f"Hash-Prüfung fehlgeschlagen: {e} — alle Chunks werden verarbeitet")
            return set()

    async def process_and_store_chunks(self, chunks: List[Dict]) -> List[Dict]:
        """Embed + store chunks — überspringt bereits bekannte via chunk_hash."""
        logger.info(f"Processing {len(chunks)} chunks...")

        # Hash-Prüfung VOR Embedding — spart teure Embedding-Calls
        known = self._known_hashes(chunks)
        new_chunks = []
        for c in chunks:
            h = hashlib.md5(c.get("text","").encode()).hexdigest()
            c["chunk_hash"] = h
            if h not in known:
                new_chunks.append(c)

        skipped = len(chunks) - len(new_chunks)
        if skipped:
            logger.info(f"Deduplizierung: {skipped} bereits bekannte Chunks übersprungen, {len(new_chunks)} neu")
        if not new_chunks:
            logger.info("Keine neuen Chunks — fertig.")
            return []

        stored = []
        parents  = [c for c in new_chunks if c.get("is_parent", False)]
        children = [c for c in new_chunks if not c.get("is_parent", False)]
        ordered  = parents + children if parents else new_chunks

        for i, chunk in enumerate(ordered):
            try:
                if not chunk.get("is_parent", True) and chunk.get("parent_id") == 0:
                    for p in parents:
                        if (p.get("url") == chunk.get("url") and
                                p.get("case_number") == chunk.get("case_number") and
                                "id" in p):
                            chunk["parent_id"] = p["id"]
                            break

                self._embed_and_save(chunk)
                stored.append(chunk)

                if (i + 1) % 100 == 0:
                    logger.info(f"Processed {i + 1}/{len(ordered)} chunks")
            except Exception as e:
                logger.error(f"Error processing chunk {i}: {e}")
                continue

        logger.info(f"Completed processing {len(stored)} chunks")
        return stored


async def main():
    crawler = LegalDatabaseCrawler()
    await crawler.crawl_all_sources(limit_per_source=5)


if __name__ == "__main__":
    asyncio.run(main())
