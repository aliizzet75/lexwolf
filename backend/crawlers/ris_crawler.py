"""Crawler für rechtsprechung-im-internet.de (offiziell, Bundesamt für Justiz).

Quellen:
  BVerfG, BGH, BVerwG, BFH, BAG, BSG, BPatG
  RSS-Feeds täglich aktualisiert, Volltext frei nutzbar.
"""
import hashlib
import logging
import re
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timedelta
from typing import Dict, List, Optional, Set
from email.utils import parsedate_to_datetime

import requests
from bs4 import BeautifulSoup

logger = logging.getLogger(__name__)

BASE   = "https://www.rechtsprechung-im-internet.de"
DETAIL = BASE + "/jportal/portal/page/bsjrsprod"

FEEDS = {
    "BVerfG": (f"{BASE}/jportal/docs/feed/bsjrs-bverfg.xml", "Verfassungsrecht"),
    "BGH":    (f"{BASE}/jportal/docs/feed/bsjrs-bgh.xml",    "Zivilrecht"),
    "BVerwG": (f"{BASE}/jportal/docs/feed/bsjrs-bverwg.xml", "Verwaltungsrecht"),
    "BFH":    (f"{BASE}/jportal/docs/feed/bsjrs-bfh.xml",    "Steuerrecht"),
    "BAG":    (f"{BASE}/jportal/docs/feed/bsjrs-bag.xml",     "Arbeitsrecht"),
    "BSG":    (f"{BASE}/jportal/docs/feed/bsjrs-bsg.xml",     "Sozialrecht"),
    "BPatG":  (f"{BASE}/jportal/docs/feed/bsjrs-bpatg.xml",  "Patentrecht"),
}

# Die beiden höchsten Gerichte: Verfassungsrecht (BVerfG) und die Zivil-/Strafsenate
# des BGH betreffen praktisch jedes Mandat — sie werden immer mit einem größeren
# Zeitfenster gecrawlt, unabhängig vom Rechtsgebiet-Filter unten.
PRIORITY_COURTS = {"BVerfG", "BGH"}

# Praxisgebiete der Kanzlei (Standard-Fallback, siehe main_crawler.derive_firm_legal_fields
# für die Ableitung aus echten Mandant-/Akte-Daten, M16). Abgeleitet aus den Rechtsgebieten,
# die der Anwalt bei der Mandatsaufnahme wählen kann
# (desktop/Services/MandatAufnahme.cs: Familienrecht/Mietrecht/Verkehrsrecht/Allgemeines
# Zivilrecht/Erbrecht -> Zivilrecht-Senate des BGH, Arbeitsrecht -> BAG, Sozialrecht -> BSG,
# Verwaltungsrecht -> BVerwG). Steuerrecht (BFH) und Patentrecht (BPatG) gehören nicht zum
# Praxisportfolio und werden daher nicht mitgecrawlt.
DEFAULT_FIRM_RECHTSGEBIETE = {"Zivilrecht", "Arbeitsrecht", "Sozialrecht", "Verwaltungsrecht"}


class RisCrawler:
    """Crawlt Gerichtsentscheidungen von rechtsprechung-im-internet.de."""

    def __init__(self):
        self.session = requests.Session()
        self.session.headers["User-Agent"] = "LexWolf/1.0 legal research (+https://lexwolf.de)"

    # ── RSS Feed ──────────────────────────────────────────────────────────────

    def _fetch_feed(self, url: str) -> List[Dict]:
        """Lädt RSS-Feed und gibt Liste von {title, link, pub_date, doc_id} zurück."""
        try:
            resp = self.session.get(url, timeout=20)
            resp.raise_for_status()
            root = ET.fromstring(resp.content)
            items = []
            for item in root.findall(".//item"):
                title   = (item.findtext("title") or "").strip()
                link    = (item.findtext("link")  or "").strip()
                pub_raw = (item.findtext("pubDate") or "").strip()
                guid    = (item.findtext("guid")  or "").strip()
                try:
                    pub_date = parsedate_to_datetime(pub_raw).date() if pub_raw else None
                except Exception:
                    pub_date = None
                items.append({"title": title, "link": link,
                               "pub_date": pub_date, "doc_id": guid})
            return items
        except Exception as e:
            logger.warning(f"Feed-Fehler {url}: {e}")
            return []

    # ── Volltext ──────────────────────────────────────────────────────────────

    def _fetch_fulltext(self, link: str) -> str:
        """Holt HTML-Volltext einer Entscheidung und gibt bereinigten Text zurück."""
        try:
            resp = self.session.get(link, timeout=20)
            resp.raise_for_status()
            soup = BeautifulSoup(resp.text, "html.parser")
            # Hauptinhalt: juris liefert Text in <div class="docLayoutMarginTopMore">
            # oder direkt im body-Text nach Navigation
            for sel in [".docLayoutMarginTopMore", ".content", "#content", "article", "main"]:
                el = soup.select_one(sel)
                if el:
                    return el.get_text(" ", strip=True)
            # Fallback: body ohne Navigation/Footer
            for tag in soup(["nav", "header", "footer", "script", "style"]):
                tag.decompose()
            return soup.get_text(" ", strip=True)[:8000]
        except Exception as e:
            logger.warning(f"Volltext-Fehler {link}: {e}")
            return ""

    # ── Chunking ──────────────────────────────────────────────────────────────

    def _parse_date(self, title: str, pub_date) -> Optional[str]:
        """Extrahiert Datum aus Titel (z.B. 'BGH ..., Urteil vom 21.04.2026, ...')."""
        m = re.search(r'vom\s+(\d{2}\.\d{2}\.\d{4})', title)
        if m:
            return m.group(1)
        return pub_date.strftime("%d.%m.%Y") if pub_date else None

    def _parse_case_number(self, title: str) -> str:
        """Extrahiert Aktenzeichen (letzter Teil nach letztem Komma)."""
        parts = title.rsplit(",", 1)
        return parts[-1].strip() if len(parts) > 1 else ""

    @staticmethod
    def _split_into_chunks(text: str, size: int = 2000) -> List[str]:
        """Gruppiert Sätze zu ~size-Zeichen-Chunks ohne Satz mittendrin zu kappen
        (gleiche Strategie wie bei Gesetzen, M7 — _split_sentences in gesetze_crawler).
        Nötig, weil BeautifulSoup.get_text(' ', strip=True) alle Zeilenumbrüche
        entfernt, ein naiver split('\\n') also nie echte Absätze liefert."""
        sentences = re.split(r'(?<=[a-zäöüA-ZÄÖÜ0-9])\.\s+', text)
        chunks: List[str] = []
        current = ""
        for sent in sentences:
            sent = sent.strip()
            if not sent:
                continue
            if not sent.endswith('.'):
                sent += '.'
            if current and len(current) + len(sent) + 1 > size:
                chunks.append(current.strip())
                current = sent
            else:
                current = f"{current} {sent}".strip()
        if current:
            chunks.append(current.strip())
        return chunks or [text]

    def _make_chunks(self, title: str, court: str, legal_field: str,
                     fulltext: str, link: str, pub_date) -> List[Dict]:
        """Erstellt Parent + Child-Chunks für eine Entscheidung."""
        date_str = self._parse_date(title, pub_date)
        case_nr  = self._parse_case_number(title)

        parent_text = f"{title}\nGericht: {court}\nAktenzeichen: {case_nr}\n\n{fulltext}"
        parent_hash = hashlib.md5(parent_text.encode()).hexdigest()

        parent = {
            "text":        parent_text,
            "title":       title,
            "court":       court,
            "case_number": case_nr,
            "date":        date_str,
            "legal_field": legal_field,
            "tags":        court.lower(),
            "source":      "rechtsprechung-im-internet.de",
            "document_type": "judgment",
            "url":         link,
            "is_parent":   True,
            "parent_id":   None,
            "chunk_hash":  parent_hash,
        }
        chunks = [parent]

        # Child-Chunks: Satzweise zu ~500 Tokens ≈ 2000 Zeichen gruppiert (Parent-Child
        # Chunking wie bei Gesetzen, M7), damit kein Satz an einer Chunk-Grenze zerschnitten wird.
        CHUNK_SIZE = 2000
        paragraphs = self._split_into_chunks(fulltext, CHUNK_SIZE)

        for i, para in enumerate(paragraphs):
            child_text = f"{title}\n{para}"
            chunks.append({
                "text":        child_text,
                "title":       f"{case_nr} — Abs. {i+1}",
                "court":       court,
                "case_number": case_nr,
                "date":        date_str,
                "legal_field": legal_field,
                "tags":        court.lower(),
                "source":      "rechtsprechung-im-internet.de",
                "document_type": "judgment",
                "url":         link,
                "is_parent":   False,
                "parent_id":   0,  # wird nach DB-Insert gesetzt
                "chunk_hash":  hashlib.md5(child_text.encode()).hexdigest(),
            })
        return chunks

    # ── Haupt-Methode ─────────────────────────────────────────────────────────

    def crawl_decisions(self, days: int = 1, limit: Optional[int] = None,
                         days_priority: int = 30,
                         legal_fields: Optional[Set[str]] = None,
                         priority_courts: Optional[Set[str]] = None) -> List[Dict]:
        """
        Crawlt neue Entscheidungen aller Bundesgerichte.

        days            → Zeitfenster für die regulären Gerichte.
        days_priority   → größeres Zeitfenster für priority_courts (BVerfG/BGH per
                           Default), damit diese beiden höchsten Gerichte deutlich
                           besser im Index abgedeckt sind als die übrigen.
        legal_fields    → wenn gesetzt: nur Gerichte crawlen, deren Rechtsgebiet
                           (siehe FEEDS) darin enthalten ist — priority_courts sind
                           davon ausgenommen und werden immer gecrawlt.
        priority_courts → Default: PRIORITY_COURTS (BVerfG, BGH).
        limit           → max. Entscheidungen insgesamt (für Tests).
        """
        priority_courts = PRIORITY_COURTS if priority_courts is None else priority_courts
        all_chunks: List[Dict] = []
        total_decisions = 0

        for court, (feed_url, legal_field) in FEEDS.items():
            if limit and total_decisions >= limit:
                break

            is_priority = court in priority_courts
            if legal_fields is not None and not is_priority and legal_field not in legal_fields:
                logger.info(
                    f"  {court}: übersprungen (Rechtsgebiet '{legal_field}' "
                    f"gehört nicht zu den Kanzlei-Praxisgebieten)"
                )
                continue

            court_days = days_priority if is_priority else days
            cutoff = datetime.now().date() - timedelta(days=court_days)

            logger.info(f"  {court}: lade Feed … (Zeitfenster {court_days} Tage"
                        f"{', priorisiert' if is_priority else ''})")
            items = self._fetch_feed(feed_url)

            # Nur neue Einträge (oder alle wenn days groß genug)
            recent = [i for i in items
                      if i["pub_date"] is None or i["pub_date"] >= cutoff]

            if limit:
                remaining = limit - total_decisions
                recent = recent[:remaining]

            logger.info(f"  {court}: {len(recent)}/{len(items)} Entscheidungen (letzte {court_days} Tage)")

            for item in recent:
                fulltext = self._fetch_fulltext(item["link"])
                if not fulltext:
                    continue
                chunks = self._make_chunks(
                    title=item["title"],
                    court=court,
                    legal_field=legal_field,
                    fulltext=fulltext,
                    link=item["link"],
                    pub_date=item["pub_date"],
                )
                all_chunks.extend(chunks)
                total_decisions += 1
                time.sleep(0.5)  # höfliche Pause

            logger.info(f"  {court}: {len(all_chunks)} Chunks bisher total")

        logger.info(f"RIS-Crawler: {total_decisions} Entscheidungen, {len(all_chunks)} Chunks")
        return all_chunks
