"""Task #290: Urteils-Crawler erweitern (BGH/BVerfG priorisiert, Rechtsgebiet-Filter,
Parent-Child-Chunking wie bei Gesetzen, M7)."""
import os
import sys
import pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent.parent / "backend"))

from crawlers.ris_crawler import RisCrawler, PRIORITY_COURTS, DEFAULT_FIRM_RECHTSGEBIETE, FEEDS
from crawlers.openjur_crawler import OpenJurCrawler


class TestPrioritaetUndRechtsgebietFilter:
    def test_bverfg_und_bgh_sind_priority_courts(self):
        assert PRIORITY_COURTS == {"BVerfG", "BGH"}

    def test_priority_courts_nicht_durch_rechtsgebiet_gefiltert(self, monkeypatch):
        """BVerfG/BGH müssen auch dann gecrawlt werden, wenn ihr Rechtsgebiet
        nicht im (ggf. leeren) Kanzlei-Filter enthalten ist."""
        crawler = RisCrawler()
        gecrawlte_courts = []

        def fake_fetch_feed(url):
            for court, (feed_url, _field) in FEEDS.items():
                if feed_url == url:
                    gecrawlte_courts.append(court)
            return []

        monkeypatch.setattr(crawler, "_fetch_feed", fake_fetch_feed)
        crawler.crawl_decisions(days=1, days_priority=30, legal_fields=set(), limit=None)

        assert "BVerfG" in gecrawlte_courts
        assert "BGH" in gecrawlte_courts

    def test_nicht_passende_rechtsgebiete_werden_uebersprungen(self, monkeypatch):
        """BFH (Steuerrecht) und BPatG (Patentrecht) gehören nicht zu den
        Standard-Praxisgebieten der Kanzlei und sollen nicht gecrawlt werden."""
        crawler = RisCrawler()
        gecrawlte_courts = []

        def fake_fetch_feed(url):
            for court, (feed_url, _field) in FEEDS.items():
                if feed_url == url:
                    gecrawlte_courts.append(court)
            return []

        monkeypatch.setattr(crawler, "_fetch_feed", fake_fetch_feed)
        crawler.crawl_decisions(
            days=1, days_priority=30,
            legal_fields=DEFAULT_FIRM_RECHTSGEBIETE, limit=None,
        )

        assert "BFH" not in gecrawlte_courts
        assert "BPatG" not in gecrawlte_courts
        # Passende Rechtsgebiete + priority courts bleiben drin
        assert set(gecrawlte_courts) == {"BVerfG", "BGH", "BVerwG", "BAG", "BSG"}

    def test_ohne_filter_werden_alle_gerichte_gecrawlt(self, monkeypatch):
        crawler = RisCrawler()
        gecrawlte_courts = []

        def fake_fetch_feed(url):
            for court, (feed_url, _field) in FEEDS.items():
                if feed_url == url:
                    gecrawlte_courts.append(court)
            return []

        monkeypatch.setattr(crawler, "_fetch_feed", fake_fetch_feed)
        crawler.crawl_decisions(days=1, days_priority=30, legal_fields=None, limit=None)

        assert set(gecrawlte_courts) == set(FEEDS.keys())

    def test_priority_courts_bekommen_groesseres_zeitfenster(self, monkeypatch):
        """Die an _fetch_feed übergebene URL sagt nichts über das Zeitfenster aus,
        deshalb wird hier direkt der cutoff über die Anzahl zurückgegebener
        Items pro Gericht verifiziert: ein Item älter als `days` aber jünger als
        `days_priority` darf nur bei priority_courts durchkommen."""
        from datetime import datetime, timedelta

        crawler = RisCrawler()
        old_date = (datetime.now() - timedelta(days=10)).date()

        def fake_fetch_feed(url):
            return [{"title": "Testfall", "link": "https://example.com/x",
                     "pub_date": old_date, "doc_id": "1"}]

        monkeypatch.setattr(crawler, "_fetch_feed", fake_fetch_feed)
        monkeypatch.setattr(crawler, "_fetch_fulltext", lambda link: "Dummy-Text. Noch ein Satz.")

        chunks = crawler.crawl_decisions(days=1, days_priority=30, legal_fields=None, limit=None)
        courts_mit_chunks = {c["court"] for c in chunks}

        # days=1 schließt das 10 Tage alte Urteil für reguläre Gerichte aus,
        # days_priority=30 lässt es für BVerfG/BGH durch.
        assert "BVerfG" in courts_mit_chunks
        assert "BGH" in courts_mit_chunks
        assert "BFH" not in courts_mit_chunks
        assert "BPatG" not in courts_mit_chunks


class TestParentChildChunking:
    def test_split_into_chunks_schneidet_keinen_satz_ab(self):
        text = "Satz eins ist hier. Satz zwei folgt gleich. " * 100
        chunks = RisCrawler._split_into_chunks(text, size=200)
        assert len(chunks) > 1
        for c in chunks:
            assert c.strip().endswith(".")
            assert len(c) <= 400  # etwas Toleranz, aber keine Mega-Chunks

    def test_make_chunks_erzeugt_parent_und_children(self):
        crawler = RisCrawler()
        fulltext = ("Der Kläger beantragt die Feststellung. Der Beklagte widerspricht dem. " * 60)
        chunks = crawler._make_chunks(
            title="BGH, Urteil vom 21.04.2026, VI ZR 1/26",
            court="BGH",
            legal_field="Zivilrecht",
            fulltext=fulltext,
            link="https://example.com/urteil",
            pub_date=None,
        )
        parents = [c for c in chunks if c["is_parent"]]
        children = [c for c in chunks if not c["is_parent"]]
        assert len(parents) == 1
        assert len(children) >= 2
        for child in children:
            assert child["parent_id"] == 0
            assert child["court"] == "BGH"
            assert child["legal_field"] == "Zivilrecht"
            assert child["document_type"] == "judgment"

    def test_make_chunks_mit_text_ohne_zeilenumbrueche(self):
        """Regression: BeautifulSoup.get_text(' ', strip=True) liefert nie
        Zeilenumbrüche — ein naiver split('\\n') darf nicht mehr zu einem
        einzigen Riesen-Chunk oder zerschnittenen Sätzen führen."""
        crawler = RisCrawler()
        fulltext = "Erster Satz ohne Umbruch. Zweiter Satz ohne Umbruch. " * 80
        assert "\n" not in fulltext
        chunks = crawler._make_chunks(
            title="BVerfG, Beschluss vom 01.01.2026, 1 BvR 1/26",
            court="BVerfG",
            legal_field="Verfassungsrecht",
            fulltext=fulltext,
            link="https://example.com/beschluss",
            pub_date=None,
        )
        children = [c for c in chunks if not c["is_parent"]]
        assert len(children) > 1
        for child in children:
            # kein Chunk darf mitten im Satz enden (außer am Text-Ende)
            assert child["text"].rstrip().endswith(".")


class TestBghBackfillPagination:
    """OpenJurCrawler._fetch_from_bgh paginiert jetzt über die BGH-Trefferliste
    (resultsPerPage + gtp), statt nur die erste Seite (~10 Treffer) zu lesen."""

    def _fake_page(self, n_links, page_no=1):
        offset = page_no * 1000
        links = "".join(
            f'<tr><a href="/SharedDocs/Entscheidungen/DE/Zivilsenate/I_ZS/2026/'
            f'I_ZR_{offset + i}-26.pdf?__blob=publicationFile">I ZR {offset + i}/26</a> '
            f'Urteil vom 0{1 + i % 9}.01.2026, I. Zivilsenat</tr>'
            for i in range(n_links)
        )
        return f"<html><body>{links}</body></html>"

    def test_paginiert_ueber_mehrere_seiten(self, monkeypatch):
        import requests as requests_mod

        pages = {1: self._fake_page(50, page_no=1), 2: self._fake_page(30, page_no=2)}
        calls = []

        class FakeResp:
            def __init__(self, text):
                self.text = text
                self.status_code = 200

            def raise_for_status(self):
                pass

        def fake_get(url, params=None, headers=None, timeout=None):
            page = 2 if (params or {}).get("gtp") else 1
            calls.append(page)
            return FakeResp(pages[page])

        monkeypatch.setattr(requests_mod, "get", fake_get)
        crawler = OpenJurCrawler()
        decisions = crawler._fetch_from_bgh(days=365, max_results=100)

        assert len(calls) == 2  # zweite Seite wurde abgerufen
        assert len(decisions) == 80  # 50 + 30, nicht nur die erste Seite
        assert all(d["_bgh_pdf"] for d in decisions)

    def test_pdf_url_wird_nicht_doppelt_zusammengesetzt(self, monkeypatch):
        import requests as requests_mod

        class FakeResp:
            text = (
                '<html><body><tr><a href="https://www.bundesgerichtshof.de/'
                'SharedDocs/Entscheidungen/DE/Zivilsenate/I_ZS/2026/I_ZR_1-26.pdf'
                '?__blob=publicationFile">I ZR 1/26</a> Urteil vom 01.01.2026, '
                'I. Zivilsenat</tr></body></html>'
            )
            status_code = 200

            def raise_for_status(self):
                pass

        monkeypatch.setattr(requests_mod, "get", lambda *a, **kw: FakeResp())
        crawler = OpenJurCrawler()
        decisions = crawler._fetch_from_bgh(days=30, max_results=5)

        assert len(decisions) == 1
        url = decisions[0]["url"]
        assert url.count("https://www.bundesgerichtshof.de") == 1
