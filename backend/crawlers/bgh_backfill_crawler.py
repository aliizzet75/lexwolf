"""Dedizierter Backfill-Crawler für den Bundesgerichtshof (BGH).

Liefert systematisch BGH-Entscheidungen der letzten N Tage aus der offiziellen
bundesgerichtshof.de-Volltextsuche als PDF. Die Quelle ist urheberrechtlich
unbedenklich (amtliche Rechtsprechung), hat kein CAPTCHA und erlaubt eine
Suche weit in die Vergangenheit.

Anders als der OpenJur-Crawler, der bundesgerichtshof.de nur als Fallback
nutzt und begrenzte Treffer liefert, iteriert dieser Crawler Seiten- und
Zeitfenster, bis er entweder `max_results` erreicht oder keine weiteren
Treffer mehr findet.
"""

import hashlib
import io
import logging
import re
import time
from datetime import datetime, timedelta
from typing import Dict, List, Optional, Set

import requests
from bs4 import BeautifulSoup
from pypdf import PdfReader

logger = logging.getLogger(__name__)

BASE_URL = "https://www.bundesgerichtshof.de"
SEARCH_PATH = "/SiteGlobals/Forms/Suche/EntscheidungssucheBGH_Formular.html"

# Regex für Aktenzeichen. Beispiele: "I ZR 148/22", "VIa ZR 1066/22",
# "2 StR 87/22". Erlaubt Bindestrichvarianten aus dem PDF-Link.
CASE_NUMBER_RE = re.compile(
    r"([IVX]+(?:[a-z])?\s+StR\s+\d+[-/]\d+|"
    r"[IVX]+(?:[a-z])?\s+ZR\s+\d+[-/]\d+|"
    r"[IVX]+(?:[a-z])?\s+ZB\s+\d+[-/]\d+|"
    r"[IVX]+(?:[a-z])?\s+Wu\s+\d+[-/]\d+|"
    r"[IVX]+(?:[a-z])?\s+AR\s+\d+[-/]\d+)"
)

RESULTS_PER_PAGE = 50


def _split_into_chunks(text: str, size: int = 2000) -> List[str]:
    """Gruppiert Sätze zu ~size-Zeichen-Chunks ohne Satz mittendrin zu kappen.

    Kopie der Strategie aus RisCrawler, damit beide Crawler konsistente
    Chunks erzeugen.
    """
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


class BghBackfillCrawler:
    """Crawlt historische BGH-Entscheidungen von bundesgerichtshof.de."""

    def __init__(self, base_url: str = BASE_URL, results_per_page: int = RESULTS_PER_PAGE):
        self.base_url = base_url.rstrip("/")
        self.search_url = self.base_url + SEARCH_PATH
        self.results_per_page = results_per_page
        self.session = requests.Session()
        self.session.headers.update({
            "User-Agent": (
                "Mozilla/5.0 (X11; Linux x86_64; rv:109.0) "
                "Gecko/20100101 Firefox/115.0"
            ),
            "Accept": "text/html,application/xhtml+xml",
            "Accept-Language": "de-DE,de;q=0.5",
            "Referer": self.base_url + "/",
        })

    def _fetch_search_page(
        self,
        start_date: datetime,
        end_date: datetime,
        page: int,
    ) -> BeautifulSoup:
        """Lädt eine Seite der BGH-Suchergebnisse und gibt BeautifulSoup zurück."""
        params = {
            "startDate": start_date.strftime("%Y-%m-%d"),
            "endDate": end_date.strftime("%Y-%m-%d"),
            "nn": "372530",  # notwendiger Formularparameter
            "resultsPerPage": str(self.results_per_page),
        }
        if page > 1:
            params["gtp"] = f"565194_list%3D{page}"

        resp = self.session.get(self.search_url, params=params, timeout=30)
        resp.raise_for_status()
        return BeautifulSoup(resp.text, "html.parser")

    def _extract_pdf_links(self, soup: BeautifulSoup) -> List[Dict]:
        """Extrahiert aus Suchergebnis-Seite alle PDF-Entscheidungen."""
        decisions = []
        seen: Set[str] = set()

        links = soup.find_all(
            "a",
            href=lambda x: x and "/SharedDocs/Entscheidungen/" in x and ".pdf" in x,
        )
        for link in links:
            href = link.get("href", "")
            if not href:
                continue
            pdf_url = href if href.startswith("http") else self.base_url + href

            # Aktenzeichen aus Linktext oder URL ableiten
            link_text = link.get_text(strip=True)
            case_number = self._extract_case_number(link_text, href)
            if not case_number:
                # Wenn kein Aktenzeichen erkennbar, überspringen — ohne
                # Aktenzeichen ist die Entscheidung später nicht identifizierbar.
                logger.debug(f"Überspringe PDF ohne erkennbares Aktenzeichen: {href}")
                continue

            # Deduplizierung anhand Aktenzeichen + PDF-URL
            dedup_key = f"{case_number}|{pdf_url}"
            if dedup_key in seen:
                continue
            seen.add(dedup_key)

            parent = link.find_parent(["tr", "li", "div"])
            context = parent.get_text(" ", strip=True) if parent else ""
            date_match = re.search(r"(\d{2}\.\d{2}\.\d{4})", context)
            date_str = date_match.group(1) if date_match else None

            senate_match = re.search(
                r"(\d+\.\s+(?:Zivil|Straf)senat|[IVX]+\.\s+(?:Zivil|Straf)senat)",
                context,
            )
            senate = senate_match.group(1) if senate_match else ""
            court = f"BGH {senate}" if senate else "BGH"

            legal_field = (
                "Zivilrecht" if "Zivil" in court
                else "Strafrecht" if "Straf" in court
                else "Sonstiges"
            )

            # Titel mit Datum, falls bekannt
            title = f"{case_number} - BGH Entscheidung"
            if date_str:
                title += f" vom {date_str}"

            decisions.append({
                "case_number": case_number,
                "date": date_str,
                "court": court,
                "legal_field": legal_field,
                "title": title,
                "pdf_url": pdf_url,
                "source": "bundesgerichtshof.de",
                "document_type": "judgment",
            })

        return decisions

    @staticmethod
    def _extract_case_number(link_text: str, href: str) -> Optional[str]:
        """Extrahiert das Aktenzeichen aus Linktext oder PDF-URL."""
        # Linktext hat oft Form "I ZR 148/22"
        m = CASE_NUMBER_RE.search(link_text)
        if m:
            return m.group(1).replace("-", "/")
        # URL-Ende hat oft Form "I_ZR_148-22.pdf" oder "I_ZR_148_22.pdf"
        filename = href.split("/")[-1].replace(".pdf", "")
        # Ersetze Unterstriche durch Leerzeichen, Bindestriche vor Zifferngruppe durch "/"
        candidate = filename.replace("_", " ")
        candidate = re.sub(r"(\d+)-(\d+)$", r"\1/\2", candidate)
        m = CASE_NUMBER_RE.search(candidate)
        if m:
            return m.group(1).replace("-", "/")
        return None

    def _fetch_pdf_text(self, pdf_url: str) -> str:
        """Lädt PDF und extrahiert Text."""
        try:
            resp = self.session.get(pdf_url, timeout=60)
            resp.raise_for_status()
            if not resp.content:
                return ""
            reader = PdfReader(io.BytesIO(resp.content))
            text = "\n".join((page.extract_text() or "") for page in reader.pages)
            return text.strip()
        except Exception as e:
            logger.warning(f"PDF-Fehler für {pdf_url}: {e}")
            return ""

    def _build_chunks(self, decision: Dict) -> List[Dict]:
        """Erzeugt Parent + Child-Chunks im selben Format wie RisCrawler."""
        text = self._fetch_pdf_text(decision["pdf_url"])
        if not text:
            logger.warning(
                f"Kein Text für {decision['case_number']} extrahiert; "
                "Entscheidung wird übersprungen."
            )
            return []

        case_number = decision["case_number"]
        court = decision["court"]
        date = decision["date"]
        title = decision["title"]
        pdf_url = decision["pdf_url"]

        parent_text = (
            f"{title}\nGericht: {court}\nAktenzeichen: {case_number}\n"
            f"Datum: {date}\n\n{text}"
        )
        parent_hash = hashlib.md5(parent_text.encode()).hexdigest()

        chunks = [{
            "text": parent_text,
            "title": title,
            "court": court,
            "case_number": case_number,
            "date": date,
            "legal_field": decision["legal_field"],
            "tags": "bgh",
            "source": "bundesgerichtshof.de",
            "document_type": "judgment",
            "url": pdf_url,
            "is_parent": True,
            "parent_id": None,
            "chunk_hash": parent_hash,
        }]

        paragraphs = _split_into_chunks(text, 2000)
        for i, para in enumerate(paragraphs):
            child_text = f"{title}\n{para}"
            chunks.append({
                "text": child_text,
                "title": f"{case_number} — Abs. {i + 1}",
                "court": court,
                "case_number": case_number,
                "date": date,
                "legal_field": decision["legal_field"],
                "tags": "bgh",
                "source": "bundesgerichtshof.de",
                "document_type": "judgment",
                "url": pdf_url,
                "is_parent": False,
                "parent_id": 0,
                "chunk_hash": hashlib.md5(child_text.encode()).hexdigest(),
            })

        return chunks

    def crawl_decisions(
        self,
        days: int = 365,
        max_results: Optional[int] = None,
        limit: Optional[int] = None,
    ) -> List[Dict]:
        """Crawlt BGH-Entscheidungen der letzten `days` Tage.

        Args:
            days: Zeitfenster rückwärts ab heute.
            max_results: Maximale Anzahl Entscheidungen (Pagination stoppt danach).
            limit: Alias für max_results; wenn beide gesetzt, gewinnt der kleinere Wert.

        Returns:
            Liste aller Chunks (Parent + Children) für alle erfolgreich
            verarbeiteten Entscheidungen.
        """
        if max_results is None and limit is not None:
            max_results = limit
        if max_results is not None and limit is not None:
            max_results = min(max_results, limit)

        end_date = datetime.now()
        start_date = end_date - timedelta(days=days)

        logger.info(
            f"BGH-Backfill: Suche Entscheidungen vom "
            f"{start_date.strftime('%Y-%m-%d')} bis {end_date.strftime('%Y-%m-%d')}"
        )

        all_decisions: List[Dict] = []
        page = 1
        while True:
            if max_results and len(all_decisions) >= max_results:
                break

            try:
                soup = self._fetch_search_page(start_date, end_date, page)
            except Exception as e:
                logger.error(f"Fehler beim Laden der BGH-Suchseite {page}: {e}")
                break

            decisions = self._extract_pdf_links(soup)
            if not decisions:
                break  # keine weiteren Treffer

            logger.info(
                f"BGH-Backfill Seite {page}: {len(decisions)} PDF-Links gefunden"
            )
            all_decisions.extend(decisions)

            # Stoppe, wenn die Seite nicht voll ist (letzte Seite erreicht)
            if len(decisions) < self.results_per_page:
                break

            page += 1
            time.sleep(0.5)  # höfliche Pause

        if max_results:
            all_decisions = all_decisions[:max_results]

        logger.info(
            f"BGH-Backfill: {len(all_decisions)} Entscheidungen gefunden, "
            "verarbeite PDFs..."
        )

        all_chunks: List[Dict] = []
        for i, decision in enumerate(all_decisions):
            logger.info(
                f"BGH-Backfill {i + 1}/{len(all_decisions)}: "
                f"{decision['case_number']}"
            )
            chunks = self._build_chunks(decision)
            if chunks:
                all_chunks.extend(chunks)
            time.sleep(0.5)

        logger.info(
            f"BGH-Backfill fertig: {len(all_decisions)} Entscheidungen, "
            f"{len(all_chunks)} Chunks"
        )
        return all_chunks
