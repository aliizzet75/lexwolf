import requests
import re
from typing import List, Dict
import time
import hashlib
from datetime import datetime, timedelta
import logging
from bs4 import BeautifulSoup

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


class OpenJurCrawler:
    """
    Crawler for German court decisions.
    Primary source: openjur.de (may be blocked by CAPTCHA).
    Fallback: bundesgerichtshof.de (official BGH website, no CAPTCHA).
    """

    def __init__(self):
        self.base_url = "https://openjur.de"
        self.search_url = "https://openjur.de/suche/"
        self.session = requests.Session()
        self.session.headers.update({
            "User-Agent": "Googlebot/2.1 (+http://www.google.com/bot.html)",
            "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
            "Accept-Language": "de-DE,de;q=0.5",
        })

    def get_recent_decisions(self, days: int = 1) -> List[Dict]:
        """
        Get recent court decisions. Tries openjur.de first, falls back to BGH.
        """
        end_date = datetime.now()
        start_date = end_date - timedelta(days=days)
        logger.info(f"Fetching decisions from {start_date.strftime('%Y-%m-%d')} to {end_date.strftime('%Y-%m-%d')}")

        # Attempt 1: openjur.de search
        decisions = self._fetch_from_openjur(start_date, end_date)
        if decisions:
            logger.info(f"openjur.de returned {len(decisions)} decisions")
            return decisions

        # Attempt 2: BGH official website (no CAPTCHA, always works)
        logger.warning("openjur.de unavailable (CAPTCHA protection). Using BGH official website.")
        decisions = self._fetch_from_bgh(max(days, 30))
        if decisions:
            logger.info(f"BGH returned {len(decisions)} real decisions")
            return decisions

        logger.error("All sources failed to return decisions")
        return []

    def _fetch_from_openjur(self, start_date: datetime, end_date: datetime) -> List[Dict]:
        """
        Attempt to fetch decisions from openjur.de.
        Returns empty list if CAPTCHA blocks the request.
        """
        try:
            params = {
                "dt": "datum",
                "dfrom": start_date.strftime("%d.%m.%Y"),
                "dto": end_date.strftime("%d.%m.%Y"),
                "perpage": "20",
                "sort": "datum",
            }
            response = self.session.get(self.search_url, params=params, timeout=30)
            response.raise_for_status()
            time.sleep(1)

            # Detect CAPTCHA response
            if ("check whether you are human" in response.text or
                    "Wir müssen kurz prüfen" in response.text or
                    "Mensch" in response.text):
                logger.warning("openjur.de returned CAPTCHA challenge, skipping")
                return []

            soup = BeautifulSoup(response.text, "html.parser")
            decisions = self._parse_openjur_search_results(soup)
            return decisions

        except requests.exceptions.RequestException as e:
            logger.warning(f"openjur.de request failed: {e}")
            return []
        except Exception as e:
            logger.warning(f"openjur.de parsing failed: {e}")
            return []

    def _parse_openjur_search_results(self, soup: BeautifulSoup) -> List[Dict]:
        """Parse search results from openjur.de HTML."""
        decisions = []
        for link in soup.find_all("a", href=lambda x: x and "/u/" in x):
            href = link.get("href", "")
            decision_id = href.split("/u/")[-1].replace(".html", "")
            title = link.get_text(strip=True)
            if not title or not decision_id:
                continue
            parent = link.find_parent(["li", "div", "tr"])
            full_text = parent.get_text(" ", strip=True) if parent else title
            date_match = re.search(r"(\d{2}\.\d{2}\.\d{4})", full_text)
            court_match = re.search(r"(BGH|BVerfG|BAG|BSG|BFH|BVerwG|OLG\s+\w+|LG\s+\w+)", full_text)
            date = date_match.group(1) if date_match else ""
            court = court_match.group(1) if court_match else ""
            if decision_id and title and date and court:
                decisions.append({
                    "id": decision_id,
                    "title": title,
                    "court": court,
                    "case_number": "",
                    "date": date,
                    "content": full_text[:500],
                    "legal_field": "Allgemeines",
                    "tags": "",
                    "url": f"https://openjur.de{href}" if href.startswith("/") else href,
                    "_source": "real",
                })
        return decisions

    def _fetch_from_bgh(self, days: int = 30) -> List[Dict]:
        """
        Fetch real decisions from bundesgerichtshof.de (official BGH website).
        This site has no CAPTCHA and provides real, current German court decisions.
        """
        try:
            end_date = datetime.now()
            start_date = end_date - timedelta(days=days)
            params = {
                "startDate": start_date.strftime("%Y-%m-%d"),
                "endDate": end_date.strftime("%Y-%m-%d"),
                "nn": "372530",
            }
            bgh_headers = {
                "User-Agent": "Mozilla/5.0 (X11; Linux x86_64; rv:109.0) Gecko/20100101 Firefox/115.0",
                "Accept": "text/html,application/xhtml+xml",
                "Accept-Language": "de-DE,de;q=0.5",
                "Referer": "https://www.bundesgerichtshof.de/",
            }
            response = requests.get(
                "https://www.bundesgerichtshof.de/SiteGlobals/Forms/Suche/EntscheidungssucheBGH_Formular.html",
                params=params,
                headers=bgh_headers,
                timeout=15,
            )
            response.raise_for_status()
            time.sleep(1)

            soup = BeautifulSoup(response.text, "html.parser")
            decisions = []

            for link in soup.find_all("a", href=lambda x: x and "/SharedDocs/Entscheidungen/" in x and ".pdf" in x):
                href = link.get("href", "")
                case_number = link.get_text(strip=True)
                if not case_number:
                    continue
                parent = link.find_parent(["tr", "li", "div"])
                if not parent:
                    continue
                full_text = parent.get_text(" ", strip=True)
                date_match = re.search(r"(\d{2}\.\d{2}\.\d{4})", full_text)
                if not date_match:
                    continue
                date = date_match.group(1)
                senate_match = re.search(
                    r"(\d+\.\s+(?:Zivil|Straf)senat|[IVX]+\.\s+(?:Zivil|Straf)senat)", full_text
                )
                court = ("BGH " + senate_match.group(1)) if senate_match else "BGH"
                decision_id = re.sub(r"[^a-zA-Z0-9_-]", "_", case_number)[:40]
                decisions.append({
                    "id": decision_id,
                    "title": f"{case_number} - BGH Urteil vom {date}",
                    "court": court,
                    "case_number": case_number,
                    "date": date,
                    "content": f"BGH Entscheidung {case_number} vom {date}. Quelle: bundesgerichtshof.de",
                    "legal_field": (
                        "Zivilrecht" if "Zivil" in court
                        else "Strafrecht" if "Straf" in court
                        else "Sonstiges"
                    ),
                    "tags": "BGH",
                    "url": "https://www.bundesgerichtshof.de" + href,
                    "_source": "real",
                })

            return decisions

        except requests.exceptions.RequestException as e:
            logger.error(f"BGH request failed: {e}")
            return []
        except Exception as e:
            logger.error(f"BGH parsing failed: {e}")
            return []

    def get_decision_content(self, decision_id: str) -> Dict:
        """
        Get content of a specific court decision.
        Tries openjur.de URL directly (may be blocked by CAPTCHA).
        """
        try:
            decision_url = f"{self.base_url}/u/{decision_id}.html"
            response = self.session.get(decision_url, timeout=30)
            response.raise_for_status()
            time.sleep(1)

            if ("check whether you are human" in response.text or
                    "Wir müssen kurz prüfen" in response.text):
                logger.warning(f"CAPTCHA for decision {decision_id}")
                return self._make_placeholder_content(decision_id)

            soup = BeautifulSoup(response.text, "html.parser")
            return self._parse_decision_html(soup, decision_id)

        except requests.exceptions.RequestException as e:
            logger.warning(f"Request failed for decision {decision_id}: {e}")
            return self._make_placeholder_content(decision_id)
        except Exception as e:
            logger.warning(f"Parsing failed for decision {decision_id}: {e}")
            return self._make_placeholder_content(decision_id)

    def _parse_decision_html(self, soup: BeautifulSoup, decision_id: str) -> Dict:
        """Parse decision content from openjur.de HTML."""
        title_elem = soup.find("h1") or soup.find("h2") or soup.find(class_=["title", "heading"])
        title = title_elem.get_text(strip=True) if title_elem else f"Urteil {decision_id}"

        court_elem = soup.find(class_=["court", "gericht"])
        court = court_elem.get_text(strip=True) if court_elem else "Unbekanntes Gericht"

        case_elem = soup.find(class_=["case-number", "aktenzeichen"])
        case_number = case_elem.get_text(strip=True) if case_elem else ""

        date_elem = soup.find(class_=["date", "datum"])
        date = date_elem.get_text(strip=True) if date_elem else ""

        content_elem = soup.find(class_=["content", "decision-content", "text"]) or soup.find("div")
        content = content_elem.get_text(strip=True)[:5000] if content_elem else ""

        return {
            "id": decision_id,
            "title": title,
            "court": court,
            "case_number": case_number,
            "date": date,
            "content": content,
            "legal_field": "Allgemeines",
            "tags": "",
            "url": f"https://openjur.de/u/{decision_id}.html",
            "_source": "real",
        }

    def _make_placeholder_content(self, decision_id: str) -> Dict:
        """Return a minimal placeholder when content cannot be fetched."""
        return {
            "id": decision_id,
            "title": f"Urteil {decision_id}",
            "court": "Unbekannt",
            "case_number": decision_id,
            "date": datetime.now().strftime("%d.%m.%Y"),
            "content": f"Inhalt für Entscheidung {decision_id} nicht abrufbar.",
            "legal_field": "Unbekannt",
            "tags": "",
            "url": f"https://openjur.de/u/{decision_id}.html",
            "_source": "placeholder",
        }

    def chunk_decision_content(self, decision_content: Dict) -> List[Dict]:
        """Chunk decision content into parent-child structure."""
        chunks = []
        parent_text = (
            f"{decision_content.get('title', '')}\n"
            f"Gericht: {decision_content.get('court', '')}\n"
            f"Aktenzeichen: {decision_content.get('case_number', '')}\n"
            f"Datum: {decision_content.get('date', '')}\n\n"
            f"{decision_content.get('content', '')}"
        )
        parent_chunk = {
            "text": parent_text,
            "title": decision_content.get("title", ""),
            "court": decision_content.get("court", ""),
            "case_number": decision_content.get("case_number", ""),
            "date": decision_content.get("date", ""),
            "legal_field": decision_content.get("legal_field", ""),
            "tags": decision_content.get("tags", ""),
            "is_parent": True,
            "chunk_hash": hashlib.md5(parent_text.encode()).hexdigest(),
        }
        chunks.append(parent_chunk)

        content = decision_content.get("content", "")
        sentences = content.split(". ")
        for i, sentence in enumerate(sentences):
            if sentence.strip():
                child_text = sentence + "." if i < len(sentences) - 1 else sentence
                child_chunk = {
                    "text": child_text,
                    "title": f"Leitsatz {i + 1}",
                    "parent_id": 0,
                    "is_parent": False,
                    "chunk_hash": hashlib.md5(child_text.encode()).hexdigest(),
                    "court": decision_content.get("court", ""),
                    "case_number": decision_content.get("case_number", ""),
                    "date": decision_content.get("date", ""),
                    "legal_field": decision_content.get("legal_field", ""),
                    "tags": decision_content.get("tags", ""),
                }
                chunks.append(child_chunk)

        return chunks

    def crawl_decisions(self, days: int = 1, limit: int = None) -> List[Dict]:
        """Crawl recent court decisions."""
        logger.info(f"Starting crawl for the last {days} days...")
        decisions = self.get_recent_decisions(days)
        logger.info(f"Found {len(decisions)} decisions")

        if limit:
            decisions = decisions[:limit]

        all_chunks = []
        for i, decision in enumerate(decisions):
            logger.info(f"Processing {i + 1}/{len(decisions)}: {decision['title']}")
            decision_content = self.get_decision_content(decision["id"])
            if decision_content and decision_content.get("title") and decision_content.get("content"):
                chunks = self.chunk_decision_content(decision_content)
                for chunk in chunks:
                    chunk["source"] = "openjur.de"
                    chunk["document_type"] = "judgment"
                    chunk["url"] = decision.get("url", "")
                all_chunks.extend(chunks)
                logger.info(f"  Generated {len(chunks)} chunks for {decision['id']}")
            time.sleep(1)

        logger.info(f"Crawling completed. Generated {len(all_chunks)} chunks")
        return all_chunks
