import asyncio
import os
from collections import Counter
from datetime import datetime
from typing import List, Dict, Set
from crawlers.bgh_backfill_crawler import BghBackfillCrawler
from crawlers.drucksachen_crawler import DrucksachenCrawler
from crawlers.gesetze_crawler import GesetzeImInternetCrawler
from crawlers.openjur_crawler import OpenJurCrawler
from crawlers.ris_crawler import DEFAULT_FIRM_RECHTSGEBIETE, FEEDS, PRIORITY_COURTS, RisCrawler
from services.database_service import DatabaseService
from services.embedding_service import EmbeddingService, MockEmbeddingService
from services.neo4j_service import Neo4jService
from services.notification_service import notify
from services.paragraph_parser import extract_references
from scripts.import_to_neo4j import create_paragraph_node, create_simple_relationships
import hashlib
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

NEO4J_URI = os.getenv("NEO4J_URI", "bolt://localhost:7687")
NEO4J_USER = os.getenv("NEO4J_USER", "neo4j")
NEO4J_PASSWORD = os.getenv("NEO4J_PASSWORD", "lexwolf123")

# Zeitfenster für die Urteils-Crawls: BVerfG/BGH (RisCrawler.PRIORITY_COURTS) sind für
# praktisch jedes Mandat relevant und werden deshalb über ein deutlich größeres Fenster
# gecrawlt als die übrigen Bundesgerichte.
RIS_DAYS = int(os.getenv("LEXWOLF_RIS_DAYS", "7"))
RIS_DAYS_PRIORITY = int(os.getenv("LEXWOLF_RIS_DAYS_PRIORITY", "30"))

# Das RIS-Feed für BGH/BVerfG deckt nur ein rollendes Fenster von ca. 1 Monat ab.
# bundesgerichtshof.de erlaubt eine Volltextsuche weit in die Vergangenheit (über
# Jahre, tausende Treffer) — damit wird der Index zusätzlich historisch mit
# BGH-Urteilen aufgefüllt, die über das RIS-Feed-Fenster hinausgehen.
# P2b-1: Backfill jetzt 3 Jahre (1095 Tage), damit die wichtigsten Leitentscheidungen
# der letzten Amtsperiode im Index liegen.
BGH_BACKFILL_DAYS = int(os.getenv("LEXWOLF_BGH_BACKFILL_DAYS", "1095"))
BGH_BACKFILL_MAX_RESULTS = int(os.getenv("LEXWOLF_BGH_BACKFILL_MAX_RESULTS", "500"))

# Mindestanzahl §§, für die drucksachen_crawler eine Gesetzesbegründung liefern soll (T#291).
BEGRUENDUNGEN_TOP_N = int(os.getenv("LEXWOLF_BEGRUENDUNGEN_TOP_N", "50"))

# Tiefe statt Breite (T#292): wie viele der nach echter Nutzung am häufigsten
# vorkommenden Praxisgebiete zusätzlich zu BVerfG/BGH das große Zeitfenster
# (RIS_DAYS_PRIORITY) bekommen, statt alle Kanzlei-Rechtsgebiete gleich (RIS_DAYS)
# zu behandeln.
TOP_N_DEEP_FIELDS = int(os.getenv("LEXWOLF_TOP_N_DEEP_FIELDS", "1"))

# Gerichts-Rechtsgebiet (ris_crawler.FEEDS) je Gericht, umgekehrt nachschlagbar.
LEGAL_FIELD_TO_COURT = {legal_field: court for court, (_url, legal_field) in FEEDS.items()}

# Die im Desktop-Client bei der Mandatsaufnahme wählbaren Rechtsgebiete
# (desktop/Services/MandatAufnahme.cs: MandatAufnahme.Rechtsgebiete), gemappt auf die
# Gerichts-Rechtsgebiete aus ris_crawler.FEEDS. "Familienrecht – …"-Varianten werden
# per Präfix erkannt (wie MandatDaten.IstFamilienrecht). "Sonstiges" bleibt unmapped.
MANDAT_RECHTSGEBIET_TO_COURT_FIELD = {
    "Arbeitsrecht": "Arbeitsrecht",
    "Sozialrecht": "Sozialrecht",
    "Verwaltungsrecht": "Verwaltungsrecht",
    "Mietrecht": "Zivilrecht",
    "Verkehrsrecht": "Zivilrecht",
    "Allgemeines Zivilrecht": "Zivilrecht",
    "Erbrecht": "Zivilrecht",
    "Strafrecht": "Zivilrecht",
}

# Grobe Stichwort-Klassifikation für die Chat-Historie (search_results.query, M16) —
# Suchanfragen haben kein Rechtsgebiet-Feld, deshalb Keyword-Heuristik statt exaktem Match.
CHAT_KEYWORDS_TO_COURT_FIELD = {
    "Arbeitsrecht":     ["kündigung", "arbeitsvertrag", "abmahnung", "abfindung", "betriebsrat"],
    "Sozialrecht":      ["sozialhilfe", "arbeitslosengeld", "alg ii", "jobcenter", "rente", "pflegegrad"],
    "Verwaltungsrecht": ["bescheid", "behörde", "verwaltungsakt", "widerspruch", "baugenehmigung"],
    "Zivilrecht":       ["miete", "mieter", "vermieter", "scheidung", "unterhalt", "sorgerecht",
                          "erbe", "testament", "verkehrsunfall", "fahrerlaubnis", "kaufvertrag"],
}


def rank_legal_fields_by_usage(database_service: DatabaseService) -> List[str]:
    """Rangfolge der Gerichts-Rechtsgebiete nach TATSÄCHLICHER Nutzung (M16), absteigend.

    Zwei echte Nutzungsdaten-Quellen werden kombiniert:
      - Mandanten-Akten: mandanten.rechtsgebiet (sobald das Feld serverseitig existiert,
        siehe derive_firm_legal_fields).
      - Chat-Historie: search_results.query — die tatsächlich gestellten Suchanfragen,
        grob per Stichwort auf ein Gerichts-Rechtsgebiet klassifiziert.

    Liefert eine leere Liste, wenn (noch) keine Nutzungsdaten vorhanden sind — dann bleibt
    es beim Gleichbehandlungs-Fallback über DEFAULT_FIRM_RECHTSGEBIETE."""
    from sqlalchemy import text as sqltxt
    usage = Counter()

    try:
        db = database_service.SessionLocal()
        try:
            result = db.execute(sqltxt(
                "SELECT rechtsgebiet, COUNT(*) FROM mandanten "
                "WHERE rechtsgebiet IS NOT NULL GROUP BY rechtsgebiet"
            ))
            for rechtsgebiet, count in result:
                if not rechtsgebiet:
                    continue
                field = ("Zivilrecht" if rechtsgebiet.startswith("Familienrecht")
                         else MANDAT_RECHTSGEBIET_TO_COURT_FIELD.get(rechtsgebiet))
                if field:
                    usage[field] += count
        finally:
            db.close()
    except Exception as e:
        logger.info(f"Keine Akte-Nutzungsdaten (mandanten.rechtsgebiet): {e}")

    try:
        db = database_service.SessionLocal()
        try:
            result = db.execute(sqltxt("SELECT query FROM search_results WHERE query IS NOT NULL"))
            for (query,) in result:
                q = query.lower()
                for field, keywords in CHAT_KEYWORDS_TO_COURT_FIELD.items():
                    if any(kw in q for kw in keywords):
                        usage[field] += 1
        finally:
            db.close()
    except Exception as e:
        logger.info(f"Keine Chat-Historie-Nutzungsdaten (search_results.query): {e}")

    ranked = [field for field, _count in usage.most_common()]
    if ranked:
        logger.info(f"Rechtsgebiete nach echter Nutzung priorisiert: {usage.most_common()}")
    return ranked


def derive_firm_legal_fields(database_service: DatabaseService) -> Set[str]:
    """Leitet die Rechtsgebiete der Kanzlei aus Mandant-/Akte-Daten ab (M16).

    Die mandanten-Tabelle hat aktuell kein Rechtsgebiet-Feld (nur Name/E-Mail/
    Aktenzeichen — das Rechtsgebiet wird bislang nur lokal im Desktop-Client bei der
    Mandatsaufnahme erfasst, siehe desktop/Services/MandatAufnahme.cs). Sobald ein
    solches Feld serverseitig existiert, wird es hier automatisch genutzt; bis dahin
    greift der Fallback auf die Standard-Praxisgebiete der Kanzlei."""
    try:
        db = database_service.SessionLocal()
        from sqlalchemy import text as sqltxt
        result = db.execute(sqltxt("SELECT DISTINCT rechtsgebiet FROM mandanten WHERE rechtsgebiet IS NOT NULL"))
        fields = {row[0] for row in result if row[0]}
        db.close()
        if fields:
            logger.info(f"Rechtsgebiete aus Mandant-Daten abgeleitet: {fields}")
            return fields
    except Exception as e:
        logger.info(f"Kein Rechtsgebiet-Feld in mandanten-Tabelle ({e}) — nutze Standard-Praxisgebiete")
    return set(DEFAULT_FIRM_RECHTSGEBIETE)


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
        self.bgh_backfill_crawler = BghBackfillCrawler()
        self.openjur_crawler = OpenJurCrawler()
        self.drucksachen_crawler = DrucksachenCrawler()
        self.embedding_service = EmbeddingService()
        self.database_service = DatabaseService()
        self.firm_legal_fields = derive_firm_legal_fields(self.database_service)

        # Tiefe statt Breite (T#292): die laut echter Nutzung (Akten + Chat-Historie)
        # am häufigsten gebrauchten Rechtsgebiete bekommen zusätzlich zu BVerfG/BGH
        # das größere Zeitfenster — statt alle Kanzlei-Rechtsgebiete gleich zu behandeln.
        ranked_fields = rank_legal_fields_by_usage(self.database_service)
        deep_fields = ranked_fields[:TOP_N_DEEP_FIELDS]
        extra_priority_courts = {
            LEGAL_FIELD_TO_COURT[f] for f in deep_fields if f in LEGAL_FIELD_TO_COURT
        }
        self.priority_courts = PRIORITY_COURTS | extra_priority_courts
        if extra_priority_courts:
            logger.info(
                f"Nach echter Nutzung priorisierte Gerichte (zusätzlich zu "
                f"BVerfG/BGH): {extra_priority_courts} (Rechtsgebiete: {deep_fields})"
            )

    async def crawl_all_sources(self, limit_per_source: int = None) -> Dict:
        """Crawl all legal sources, store in pgvector, then update Neo4j.
        Gibt Statistik-Dict zurück (für Erfolgs-/Fehler-Benachrichtigung)."""
        logger.info("Starting legal database crawl...")
        all_new_chunks = []
        stats = {"gesetze_neu": 0, "rechtsprechung_neu": 0, "begruendungen_neu": 0, "errors": []}

        logger.info("Crawling gesetze-im-internet.de...")
        try:
            law_chunks = self.gesetze_crawler.crawl_laws(limit=limit_per_source)
            logger.info(f"Generated {len(law_chunks)} chunks from laws")
            new_chunks = await self.process_and_store_chunks(law_chunks)
            all_new_chunks.extend(new_chunks)
            stats["gesetze_neu"] = len(new_chunks)
        except Exception as e:
            logger.error(f"Error crawling gesetze-im-internet.de: {e}")
            stats["errors"].append(f"gesetze-im-internet.de: {e}")

        logger.info(
            f"Crawling rechtsprechung-im-internet.de "
            f"(priorisierte Gerichte {self.priority_courts}, Fenster {RIS_DAYS_PRIORITY} Tage; "
            f"übrige Gerichte {RIS_DAYS} Tage, gefiltert auf Kanzlei-Rechtsgebiete {self.firm_legal_fields})..."
        )
        try:
            decision_chunks = self.ris_crawler.crawl_decisions(
                days=RIS_DAYS,
                days_priority=RIS_DAYS_PRIORITY,
                legal_fields=self.firm_legal_fields,
                priority_courts=self.priority_courts,
                limit=limit_per_source,
            )
            logger.info(f"Generated {len(decision_chunks)} chunks from decisions")
            new_chunks = await self.process_and_store_chunks(decision_chunks)
            all_new_chunks.extend(new_chunks)
            stats["rechtsprechung_neu"] = len(new_chunks)
        except Exception as e:
            logger.error(f"Error crawling rechtsprechung-im-internet.de: {e}")
            stats["errors"].append(f"rechtsprechung-im-internet.de: {e}")

        logger.info(
            f"Crawling bundesgerichtshof.de (BGH-Backfill über das RIS-Feed-Fenster "
            f"hinaus, {BGH_BACKFILL_DAYS} Tage, max. {BGH_BACKFILL_MAX_RESULTS} Urteile) "
            f"über den dedizierten BGH-Backfill-Crawler..."
        )
        try:
            bgh_backfill_chunks = self.bgh_backfill_crawler.crawl_decisions(
                days=BGH_BACKFILL_DAYS,
                max_results=BGH_BACKFILL_MAX_RESULTS,
                limit=limit_per_source,
            )
            logger.info(f"Generated {len(bgh_backfill_chunks)} chunks from BGH-Backfill")
            new_chunks = await self.process_and_store_chunks(bgh_backfill_chunks)
            all_new_chunks.extend(new_chunks)
            stats["rechtsprechung_neu"] += len(new_chunks)
        except Exception as e:
            logger.error(f"Error crawling bundesgerichtshof.de (Backfill): {e}")
            stats["errors"].append(f"bundesgerichtshof.de-Backfill: {e}")

        logger.info(
            f"Crawling dip.bundestag.de (Gesetzesbegründungen zu den "
            f"{BEGRUENDUNGEN_TOP_N} häufigsten §§ im Index)..."
        )
        try:
            neo4j_svc = Neo4jService(uri=NEO4J_URI, user=NEO4J_USER, password=NEO4J_PASSWORD)
            if neo4j_svc.connect():
                try:
                    begruendung_chunks = self.drucksachen_crawler.crawl_begruendungen(
                        neo4j_svc,
                        database_service=self.database_service,
                        limit=BEGRUENDUNGEN_TOP_N,
                    )
                finally:
                    neo4j_svc.close()
                logger.info(f"Generated {len(begruendung_chunks)} chunks from Gesetzesbegründungen")
                new_chunks = await self.process_and_store_chunks(begruendung_chunks)
                all_new_chunks.extend(new_chunks)
                stats["begruendungen_neu"] = len(new_chunks)
            else:
                logger.warning("Neo4j nicht erreichbar — drucksachen_crawler übersprungen")
                stats["errors"].append("dip.bundestag.de: Neo4j nicht erreichbar")
        except Exception as e:
            logger.error(f"Error crawling dip.bundestag.de: {e}")
            stats["errors"].append(f"dip.bundestag.de: {e}")

        if all_new_chunks:
            logger.info(f"Updating Neo4j with {len(all_new_chunks)} new chunks...")
            try:
                update_neo4j(all_new_chunks)
            except Exception as e:
                logger.error(f"Neo4j update failed: {e}")
                stats["errors"].append(f"Neo4j-Update: {e}")

        stats["chunks_neu_gesamt"] = len(all_new_chunks)
        logger.info("Legal database crawl completed")
        return stats

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
    # Ohne LEXWOLF_CRAWL_LIMIT: voller Lauf über den ganzen Gesetzeskatalog
    # (chunk_hash-Dedup macht unveränderte Gesetze billig). Für einen schnellen
    # manuellen Testlauf: LEXWOLF_CRAWL_LIMIT=5 setzen.
    limit_env = os.getenv("LEXWOLF_CRAWL_LIMIT")
    limit = int(limit_env) if limit_env else None
    crawler = LegalDatabaseCrawler()
    start = datetime.utcnow()

    try:
        stats = await crawler.crawl_all_sources(limit_per_source=limit)
    except Exception as e:
        duration_min = (datetime.utcnow() - start).total_seconds() / 60
        logger.error(f"Crawl komplett fehlgeschlagen: {e}")
        notify(
            "LexWolf-Crawl fehlgeschlagen",
            f"Dauer: {duration_min:.1f} Min\nDer Crawl ist mit einer unbehandelten "
            f"Ausnahme abgebrochen:\n{e}",
            is_error=True,
        )
        raise

    duration_min = (datetime.utcnow() - start).total_seconds() / 60
    if stats["errors"]:
        notify(
            "LexWolf-Crawl mit Fehlern abgeschlossen",
            f"Dauer: {duration_min:.1f} Min\n"
            f"Neue Chunks: {stats['chunks_neu_gesamt']} "
            f"(Gesetze: {stats['gesetze_neu']}, Rechtsprechung: {stats['rechtsprechung_neu']})\n"
            f"Fehler ({len(stats['errors'])}):\n- " + "\n- ".join(stats["errors"]),
            is_error=True,
        )
    else:
        notify(
            "LexWolf-Crawl erfolgreich",
            f"Dauer: {duration_min:.1f} Min\n"
            f"Neue Chunks: {stats['chunks_neu_gesamt']} "
            f"(Gesetze: {stats['gesetze_neu']}, Rechtsprechung: {stats['rechtsprechung_neu']}, "
            f"Gesetzesbegründungen: {stats['begruendungen_neu']})",
            is_error=False,
        )


if __name__ == "__main__":
    asyncio.run(main())
