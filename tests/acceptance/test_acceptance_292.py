"""Task #292: Rechtsgebiet-Priorisierung — Crawler auf die Praxisgebiete des
Test-Anwalts fokussieren (Tiefe statt Breite).

Leitet die tatsächlich genutzten Rechtsgebiete aus Mandanten-Akten- und
Chat-Historie-Daten (M16) ab und lässt main_crawler die am häufigsten genutzten
Gebiete zusätzlich zu BVerfG/BGH mit dem größeren Zeitfenster (priority_courts)
crawlen, statt alle Kanzlei-Rechtsgebiete gleich zu behandeln.

Läuft gegen die echte Postgres-DB (localhost:5432/lexwolf) — Testdaten werden in
search_results eingefügt und am Ende wieder gelöscht.
"""
import os
import sys
import pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent.parent / "backend"))
os.environ.setdefault("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")

import pytest
from sqlalchemy import text as sqltxt

from crawlers.main_crawler import (
    rank_legal_fields_by_usage,
    derive_firm_legal_fields,
    LEGAL_FIELD_TO_COURT,
    MANDAT_RECHTSGEBIET_TO_COURT_FIELD,
    CHAT_KEYWORDS_TO_COURT_FIELD,
    TOP_N_DEEP_FIELDS,
)
from crawlers.ris_crawler import PRIORITY_COURTS, DEFAULT_FIRM_RECHTSGEBIETE, FEEDS
from services.database_service import DatabaseService

MARKER = "T292-TESTQUERY-"


@pytest.fixture(scope="module")
def db_service():
    try:
        svc = DatabaseService()
        db = svc.SessionLocal()
        db.execute(sqltxt("SELECT 1"))
        db.close()
    except Exception as e:
        pytest.skip(f"Postgres nicht erreichbar: {e}")
    yield svc


@pytest.fixture(autouse=True)
def _cleanup_test_rows(db_service):
    """Räumt vor UND nach jedem Test alle über MARKER eingefügten Test-Queries weg,
    damit echte Nutzungsdaten in search_results nicht verfälscht werden."""
    def _delete():
        db = db_service.SessionLocal()
        try:
            db.execute(sqltxt("DELETE FROM search_results WHERE query LIKE :m"), {"m": f"{MARKER}%"})
            db.commit()
        finally:
            db.close()
    _delete()
    yield
    _delete()


def _insert_queries(db_service, queries):
    db = db_service.SessionLocal()
    try:
        for q in queries:
            db.execute(sqltxt("INSERT INTO search_results (query, search_type) VALUES (:q, 'dense')"),
                       {"q": q})
        db.commit()
    finally:
        db.close()


class TestMapping:
    """Statische Mappings: Desktop-Rechtsgebiete / Gerichts-Rechtsgebiete müssen
    konsistent mit ris_crawler.FEEDS sein."""

    def test_legal_field_to_court_deckt_alle_feeds_ab(self):
        assert set(LEGAL_FIELD_TO_COURT.values()) == set(FEEDS.keys())

    def test_mandat_mapping_zeigt_nur_auf_bekannte_court_fields(self):
        court_fields = {lf for _c, (_u, lf) in FEEDS.items()}
        for rechtsgebiet, field in MANDAT_RECHTSGEBIET_TO_COURT_FIELD.items():
            assert field in court_fields, f"{rechtsgebiet} -> unbekanntes Rechtsgebiet {field}"

    def test_chat_keywords_zeigen_nur_auf_bekannte_court_fields(self):
        court_fields = {lf for _c, (_u, lf) in FEEDS.items()}
        assert set(CHAT_KEYWORDS_TO_COURT_FIELD.keys()) <= court_fields

    def test_default_firm_rechtsgebiete_unveraendert(self):
        """T#290-Fallback bleibt bestehen, wenn keine Nutzungsdaten vorliegen."""
        assert DEFAULT_FIRM_RECHTSGEBIETE == {"Zivilrecht", "Arbeitsrecht", "Sozialrecht", "Verwaltungsrecht"}


class TestRankLegalFieldsByUsage:
    def test_leer_ohne_nutzungsdaten(self, db_service):
        """Ohne echte Nutzungsdaten (Default-Zustand der Test-DB: mandanten und
        search_results ohne T292-Testzeilen) liefert die Rangfolge eine leere Liste —
        main_crawler fällt dann auf den Gleichbehandlungs-Fallback zurück."""
        ranked = rank_legal_fields_by_usage(db_service)
        assert ranked == []

    def test_chat_historie_bestimmt_rangfolge(self, db_service):
        """Mehr Arbeitsrecht- als Sozialrecht-Anfragen in der Chat-Historie ->
        Arbeitsrecht muss vorne stehen (Tiefe für das meistgenutzte Gebiet)."""
        _insert_queries(db_service, [
            f"{MARKER}Wie kündige ich fristlos wegen Abmahnung und Arbeitsvertrag?",
            f"{MARKER}Kündigung durch den Betriebsrat angefochten?",
            f"{MARKER}Abfindung nach Kündigung berechnen",
            f"{MARKER}Pflegegrad widerspruch beim Jobcenter",
        ])
        ranked = rank_legal_fields_by_usage(db_service)
        assert ranked[0] == "Arbeitsrecht"
        assert "Sozialrecht" in ranked

    def test_mehrere_keywords_zaehlen_nur_einmal_pro_query(self, db_service):
        """Eine Query mit mehreren Treffern im selben Rechtsgebiet darf nicht mehrfach
        gezählt werden (sonst würde eine lange Anfrage die Rangfolge verzerren)."""
        _insert_queries(db_service, [
            f"{MARKER}Kündigung, Abmahnung und Abfindung wegen Arbeitsvertrag-Streit",
        ])
        ranked = rank_legal_fields_by_usage(db_service)
        assert ranked.count("Arbeitsrecht") == 1


class TestCrawlerPriorisierung:
    """main_crawler.LegalDatabaseCrawler nutzt die Rangfolge, um zusätzliche
    priority_courts zu bestimmen (Tiefe statt Breite)."""

    def test_ohne_nutzungsdaten_bleiben_nur_bverfg_bgh_priority(self, db_service, monkeypatch):
        from crawlers import main_crawler
        monkeypatch.setattr(main_crawler, "DatabaseService", lambda: db_service)
        monkeypatch.setattr(main_crawler, "EmbeddingService", lambda: object())
        monkeypatch.setattr(main_crawler, "RisCrawler", lambda: object())
        monkeypatch.setattr(main_crawler, "OpenJurCrawler", lambda: object())
        monkeypatch.setattr(main_crawler, "GesetzeImInternetCrawler", lambda: object())
        monkeypatch.setattr(main_crawler, "DrucksachenCrawler", lambda: object())

        crawler = main_crawler.LegalDatabaseCrawler()
        assert crawler.priority_courts == PRIORITY_COURTS

    def test_meistgenutztes_rechtsgebiet_wird_priority_court(self, db_service, monkeypatch):
        """Mit Arbeitsrecht als Top-Gebiet in der Chat-Historie muss BAG
        (Arbeitsrecht-Gericht) zusätzlich priorisiert werden."""
        _insert_queries(db_service, [
            f"{MARKER}Kündigung Arbeitsvertrag Abmahnung",
            f"{MARKER}Abfindung nach betriebsbedingter Kündigung",
        ])
        from crawlers import main_crawler
        monkeypatch.setattr(main_crawler, "DatabaseService", lambda: db_service)
        monkeypatch.setattr(main_crawler, "EmbeddingService", lambda: object())
        monkeypatch.setattr(main_crawler, "RisCrawler", lambda: object())
        monkeypatch.setattr(main_crawler, "OpenJurCrawler", lambda: object())
        monkeypatch.setattr(main_crawler, "GesetzeImInternetCrawler", lambda: object())
        monkeypatch.setattr(main_crawler, "DrucksachenCrawler", lambda: object())

        crawler = main_crawler.LegalDatabaseCrawler()
        assert "BAG" in crawler.priority_courts
        assert crawler.priority_courts >= PRIORITY_COURTS
        assert len(crawler.priority_courts) <= len(PRIORITY_COURTS) + TOP_N_DEEP_FIELDS
