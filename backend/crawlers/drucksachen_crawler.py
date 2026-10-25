"""Crawler für Gesetzesbegründungen aus Bundestags-Drucksachen (DIP-API, dip.bundestag.de).

Liefert die "Einzelbegründung" ("Zu § X ...") aus Gesetzentwürfen zu bereits im
Index vorhandenen §§. Die Begründungen werden als eigenständige Chunks
gespeichert und im Neo4j-Graph per VERWEIST_AUF mit der jeweiligen Norm
verknüpft (Referenz, kein Duplikat des Normtextes) — genau wie alle anderen
§-Zitate im Graph (siehe services/paragraph_parser.extract_references).
"""
import os
import re
import time
import hashlib
import logging
import requests
from typing import List, Dict, Optional, Tuple

logger = logging.getLogger(__name__)

DIP_API_BASE = "https://search.dip.bundestag.de/api/v1"

# Öffentlicher DIP-Demo-Key (gültig bis Mai 2027), siehe
# https://dip.bundestag.de/über-dip/hilfe/api — per Env DIP_API_KEY überschreibbar
# (z.B. mit einem persönlichen Key von parlamentsdokumentation@bundestag.de).
DEFAULT_API_KEY = "R2BZaee.DjdCyihKZMf8AOjtScubP2EVydegzjmBIQ"

# Abkürzung -> amtlicher Gesetzesname für die DIP-Titelsuche (f.titel).
# Nur Gesetze in dieser Map werden als Kandidaten für die Begründungssuche
# herangezogen, weil ohne amtlichen Namen keine sinnvolle DIP-Titelsuche möglich ist.
GESETZ_FULLNAME = {
    "BGB": "Bürgerlichen Gesetzbuchs",
    "StGB": "Strafgesetzbuchs",
    "ZPO": "Zivilprozessordnung",
    "StPO": "Strafprozessordnung",
    "HGB": "Handelsgesetzbuchs",
    "GG": "Grundgesetzes",
    "AO": "Abgabenordnung",
    "EStG": "Einkommensteuergesetzes",
    "KStG": "Körperschaftsteuergesetzes",
    "GewStG": "Gewerbesteuergesetzes",
    "KWG": "Kreditwesengesetzes",
    "GenG": "Genossenschaftsgesetzes",
    "InvStG": "Investmentsteuergesetzes",
    "VwGO": "Verwaltungsgerichtsordnung",
    "PatG": "Patentgesetzes",
    "TVG": "Tarifvertragsgesetzes",
    "GVG": "Gerichtsverfassungsgesetzes",
    "VAG": "Versicherungsaufsichtsgesetzes",
    "WpHG": "Wertpapierhandelsgesetzes",
    "EnWG": "Energiewirtschaftsgesetzes",
    "UrhG": "Urheberrechtsgesetzes",
    "MarkenG": "Markengesetzes",
    "GmbHG": "GmbH-Gesetzes",
    "AktG": "Aktiengesetzes",
    "BetrVG": "Betriebsverfassungsgesetzes",
    "ArbGG": "Arbeitsgerichtsgesetzes",
    "KSchG": "Kündigungsschutzgesetzes",
    "BUrlG": "Bundesurlaubsgesetzes",
    "TzBfG": "Teilzeit- und Befristungsgesetzes",
    "UWG": "Gesetzes gegen den unlauteren Wettbewerb",
    "GWB": "Gesetzes gegen Wettbewerbsbeschränkungen",
    "BauGB": "Baugesetzbuchs",
    "WEG": "Wohnungseigentumsgesetzes",
    "InsO": "Insolvenzordnung",
    "RVG": "Rechtsanwaltsvergütungsgesetzes",
    "FamFG": "Gesetzes über das Verfahren in Familiensachen",
    "AGG": "Allgemeinen Gleichbehandlungsgesetzes",
}

# "Zu § 616", "Zu § 40 b EStG" (PDF-Extraktion fügt bei Buchstaben-Suffixen z.T. ein
# Leerzeichen ein, z.B. "40 b" statt "40b") — Beginn eines Begründungsabschnitts.
#
# Bewusst NICHT unterstützt: Artikel-zitierende Gesetze (GG, EGBGB, …). "Zu Artikel N"
# ist in Begründungen strukturell zweideutig — es kann Artikel N des GEÄNDERTEN Gesetzes
# meinen ("Zu Nummer 1 (Artikel 3)") oder Artikel N des ÄNDERNDEN Gesetzentwurfs selbst
# (z.B. dessen eigene, meist bedeutungslose "Artikel 3: Inkrafttreten"-Klausel). Eine
# Testabfrage hat gezeigt, dass das zu falschen Zuordnungen führt (z.B. "Begründung zu
# Art. 3 Abs. 1 GG" landete fälschlich bei der Inkrafttretens-Klausel der Novelle statt
# beim Gleichheitssatz). Ohne verlässliche Disambiguierung werden solche §§ ausgelassen,
# um keine falsch zugeordneten Begründungen zu speichern.
_ZU_PARAGRAPH_RE = re.compile(r'Zu\s+§\s*(\d+)\s?([a-zA-Z])?\b')

# Omnibus-Gesetzentwürfe ändern oft mehrere Gesetze in einem Dokument; jeder Abschnitt
# beginnt mit "Zu Artikel N (Änderung des ...)". Diese Marker grenzen die Blöcke ab, die
# tatsächlich zum gesuchten Gesetz gehören — verhindert, dass eine Begründung zu § 3 eines
# fremden Gesetzes versehentlich dem gesuchten Gesetz zugeordnet wird.
_BLOCK_HEADER_RE = re.compile(r'Zu\s+Artikel\s+\d+[a-zA-Z]?\s*\(([^)]{3,120})\)')

_MIN_SEGMENT_LEN = 60
_MAX_SEGMENT_LEN = 4000

_STOPWORDS = {"des", "der", "die", "das", "den", "dem", "und", "zur", "zum"}


def _base_nr(nr: str) -> str:
    """Reduziert eine Paragraph-Nr wie '30 Abs. 1' oder 'Art. 12 Abs. 1' auf die Basisnummer
    (inkl. Buchstaben-Suffix, z.B. '40b')."""
    nr = (nr or "").strip()
    m = re.match(r'(?:Art\.?\s*)?(\d+)\s?([a-zA-Z])?\b', nr)
    if not m:
        return nr
    return f"{m.group(1)}{m.group(2) or ''}"


def _normalize(s: str) -> str:
    """Normalisiert einen Gesetzesnamen für robusten Teilstring-Vergleich (Groß-/Klein-
    schreibung, einfache deutsche Deklinationsendungen)."""
    s = s.lower()
    s = re.sub(r'[^a-zäöüß\s]', ' ', s)
    words = []
    for w in s.split():
        if w in _STOPWORDS:
            continue
        for suf in ("es", "en", "er", "s"):
            if w.endswith(suf) and len(w) - len(suf) >= 4:
                w = w[: -len(suf)]
                break
        words.append(w)
    return " ".join(words)


def _relevant_blocks(text: str, fullname: str, gesetz: str = "") -> List[Tuple[int, int]]:
    """Liefert Text-Spannen, die laut 'Zu Artikel N (Änderung des ...)'-Überschrift zum
    gesuchten Gesetz gehören. Die Überschrift nennt das Gesetz entweder ausgeschrieben
    ("Änderung des Einkommensteuergesetzes") oder nur als Abkürzung ("(EStG)") — beides
    wird akzeptiert. Ohne solche Überschriften betrifft die Drucksache nur ein Gesetz
    (Single-Subject-Bill) — dann ist der ganze Text relevant."""
    headers = list(_BLOCK_HEADER_RE.finditer(text))
    if not headers:
        return [(0, len(text))]
    keyword = _normalize(fullname)
    abbr_re = re.compile(rf'\b{re.escape(gesetz)}\b', re.IGNORECASE) if gesetz else None
    spans = []
    for i, h in enumerate(headers):
        start = h.start()
        end = headers[i + 1].start() if i + 1 < len(headers) else len(text)
        header_text = h.group(1)
        matches_fullname = keyword and keyword in _normalize(header_text)
        matches_abbr = abbr_re and abbr_re.search(header_text)
        if matches_fullname or matches_abbr:
            spans.append((start, end))
    return spans


def _extract_begruendung_segments(text: str, base_nr: str, fullname: str = "", gesetz: str = "") -> List[str]:
    """Extrahiert alle 'Zu § <nr> ...'-Begründungsabschnitte zu einer Norm, beschränkt auf
    die zum gesuchten Gesetz gehörenden Text-Blöcke (siehe _relevant_blocks)."""
    segments: List[str] = []
    for span_start, span_end in _relevant_blocks(text, fullname, gesetz):
        sub = text[span_start:span_end]
        matches = list(_ZU_PARAGRAPH_RE.finditer(sub))
        for i, m in enumerate(matches):
            nr = f"{m.group(1)}{m.group(2) or ''}"
            if nr != base_nr:
                continue
            start = m.start()
            end = matches[i + 1].start() if i + 1 < len(matches) else min(len(sub), start + _MAX_SEGMENT_LEN)
            body = sub[start:end].strip()
            if len(body) > _MAX_SEGMENT_LEN:
                body = body[:_MAX_SEGMENT_LEN].rstrip() + " …"
            if len(body) >= _MIN_SEGMENT_LEN:
                segments.append(body)
    return segments


class DrucksachenCrawler:
    """Lädt Gesetzesbegründungen zu bereits indexierten §§ aus Bundestags-Drucksachen (DIP-API)."""

    def __init__(self, api_key: Optional[str] = None):
        self.api_key = api_key or os.getenv("DIP_API_KEY", DEFAULT_API_KEY)
        self.session = requests.Session()
        self.session.headers["User-Agent"] = "LexWolf/1.0 (legal research tool)"
        self._text_cache: Dict[str, List[Dict]] = {}

    def get_top_paragraphs(self, neo4j_service, limit: int = 50, pool_multiplier: int = 6) -> List[Tuple[str, str, int]]:
        """Ermittelt die im Graph am häufigsten zitierten §§ (VERWEIST_AUF-Ziele),
        beschränkt auf Gesetze mit amtlichem Namen (GESETZ_FULLNAME) — sonst ist
        keine DIP-Titelsuche möglich — und auf echte §-Zitate (kein 'Art.', siehe
        Kommentar bei _ZU_PARAGRAPH_RE zur Artikel-Mehrdeutigkeit). pool_multiplier
        liefert einen größeren Kandidaten-Pool, da nicht jede zitierte Norm eine
        Drucksachen-Begründung hat (BGB/StGB etc. stammen z.T. aus der Zeit vor 1949,
        dem Beginn der DIP-Erfassung)."""
        known = list(GESETZ_FULLNAME.keys())
        query = """
            MATCH (src)-[:VERWEIST_AUF]->(t:Paragraph)
            WHERE t.gesetz IN $known AND t.paragraph_nr IS NOT NULL AND t.paragraph_nr <> ""
              AND NOT toLower(t.paragraph_nr) STARTS WITH "art"
            RETURN t.gesetz AS gesetz, t.paragraph_nr AS nr, count(*) AS c
            ORDER BY c DESC LIMIT $lim
        """
        rows = neo4j_service.run_query(query, {"known": known, "lim": limit * pool_multiplier})
        return [(r["gesetz"], r["nr"], r["c"]) for r in rows]

    def _fallback_paragraphs(self, database_service, exclude: set, max_candidates: int = 2000) -> List[Tuple[str, str, int]]:
        """Ergänzender Kandidaten-Pool: alle im Index vorhandenen §§ bekannter Gesetze,
        sortiert nach Chunk-Anzahl. Der Graph enthält nur wenige hundert tatsächlich
        zitierte §§ bekannter Gesetze (VERWEIST_AUF ist auf Querverweise innerhalb von
        Chunk-Texten beschränkt) — das reicht oft nicht aus, um 50 §§ mit tatsächlich
        verfügbarer Drucksachen-Begründung zu finden. Dieser Fallback liefert einen
        deutlich größeren Pool, aus dem die DIP-Verfügbarkeit den eigentlichen Filter bildet."""
        if database_service is None:
            return []
        from sqlalchemy import text as sqltext

        tag_to_gesetz = {abk.lower(): abk for abk in GESETZ_FULLNAME}
        db = database_service.SessionLocal()
        try:
            rows = db.execute(
                sqltext(
                    "SELECT tags, title FROM legal_chunks "
                    "WHERE document_id IN (SELECT id FROM legal_documents WHERE source = 'gesetze-im-internet.de') "
                    "AND tags = ANY(:tags)"
                ),
                {"tags": list(tag_to_gesetz.keys())},
            )
            counts: Dict[Tuple[str, str], int] = {}
            pattern = re.compile(r'^§\s*(\d+[a-zA-Z]?)\s')
            for tags, title in rows:
                if not title or not tags or "/" in title:
                    continue
                m = pattern.match(title.strip())
                if not m:
                    continue
                gesetz = tag_to_gesetz.get(tags)
                if not gesetz:
                    continue
                key = (gesetz, m.group(1))
                if key in exclude:
                    continue
                counts[key] = counts.get(key, 0) + 1
        finally:
            db.close()

        ranked = sorted(counts.items(), key=lambda kv: kv[1], reverse=True)[:max_candidates]
        return [(gesetz, nr, c) for (gesetz, nr), c in ranked]

    def _fetch_gesetzentwuerfe_text(self, gesetz: str, max_docs: int = 30) -> List[Dict]:
        """Lädt (und cached) Volltexte von Gesetzentwürfen, die das gegebene Gesetz ändern."""
        if gesetz in self._text_cache:
            return self._text_cache[gesetz]

        fullname = GESETZ_FULLNAME.get(gesetz)
        docs: List[Dict] = []
        if not fullname:
            self._text_cache[gesetz] = docs
            return docs

        cursor = None
        try:
            while len(docs) < max_docs:
                params = {
                    "f.titel": fullname,
                    "f.drucksachetyp": "Gesetzentwurf",
                    "apikey": self.api_key,
                }
                if cursor:
                    params["cursor"] = cursor
                resp = self.session.get(f"{DIP_API_BASE}/drucksache-text", params=params, timeout=30)
                resp.raise_for_status()
                data = resp.json()
                batch = data.get("documents", [])
                if not batch:
                    break
                docs.extend(batch)
                new_cursor = data.get("cursor")
                if not new_cursor or new_cursor == cursor:
                    break
                cursor = new_cursor
                time.sleep(0.2)
        except Exception as e:
            logger.warning(f"DIP-Abfrage fehlgeschlagen für {gesetz} ({fullname}): {e}")

        docs = docs[:max_docs]
        self._text_cache[gesetz] = docs
        return docs

    def _make_chunk(self, gesetz: str, nr: str, doc: Dict, body: str) -> Dict:
        dokumentnummer = doc.get("dokumentnummer", "")
        # Format muss exakt dem Muster von services.paragraph_parser.extract_references
        # entsprechen ("§ <nr> <GESETZ>"), damit beim Neo4j-Import automatisch eine
        # VERWEIST_AUF-Kante zur Norm angelegt wird (Referenz, kein Duplikat des Normtextes).
        title = f"Begründung zu § {nr} {gesetz} (BT-Drs. {dokumentnummer})"
        text = f"{title}\n{body}".strip()
        url = (doc.get("fundstelle") or {}).get("pdf_url", "")
        return {
            "text": text,
            "title": title,
            "paragraph_nr": nr,
            "gesetz": gesetz,
            "legal_field": "",
            "court": "",
            "case_number": "",
            "date": doc.get("datum"),
            "tags": gesetz.lower(),
            "source": "dip.bundestag.de",
            "url": url,
            "chunk_hash": hashlib.md5(text.encode()).hexdigest(),
            "document_type": "gesetzesbegruendung",
        }

    def crawl_begruendungen(self, neo4j_service, database_service=None, limit: int = 50,
                             max_docs_per_gesetz: int = 30) -> List[Dict]:
        """
        Crawlt Gesetzesbegründungen für die im Graph häufigsten §§.

        Holt pro Gesetz einmalig (gecached) alle verfügbaren Gesetzentwurf-Volltexte
        und sucht darin nach 'Zu § <nr>'-Abschnitten. Bricht ab, sobald für
        mindestens `limit` unterschiedliche §§ eine Begründung gefunden wurde, oder
        der Kandidaten-Pool erschöpft ist.

        Die im Graph zitierten §§ (get_top_paragraphs) haben Vorrang — sie bilden die
        "häufigsten §§" im eigentlichen Sinne (tatsächlich in Chunk-Texten referenziert).
        Reicht dieser Pool nicht aus, um `limit` §§ mit tatsächlich verfügbarer
        Drucksachen-Begründung zu finden, wird er um alle sonstigen §§ bekannter Gesetze
        aus dem Postgres-Index ergänzt (database_service, siehe _fallback_paragraphs).
        """
        candidates = self.get_top_paragraphs(neo4j_service, limit=limit)
        logger.info(f"Kandidaten-Pool: {len(candidates)} häufig zitierte §§ (Ziel: {limit} mit Begründung)")

        if database_service is not None:
            fallback = self._fallback_paragraphs(database_service, exclude={(g, n) for g, n, _ in candidates})
            logger.info(f"Fallback-Pool: +{len(fallback)} weitere §§ bekannter Gesetze aus dem Postgres-Index")
            candidates = candidates + fallback

        all_chunks: List[Dict] = []
        seen_hashes: set = set()
        covered_paragraphs: set = set()

        for gesetz, nr, count in candidates:
            if len(covered_paragraphs) >= limit:
                break

            base_nr = _base_nr(nr)
            fullname = GESETZ_FULLNAME.get(gesetz, "")
            docs = self._fetch_gesetzentwuerfe_text(gesetz, max_docs=max_docs_per_gesetz)
            if not docs:
                continue

            found_for_this_paragraph = False
            for doc in docs:
                text = doc.get("text", "")
                if not text:
                    continue
                for body in _extract_begruendung_segments(text, base_nr, fullname, gesetz):
                    chunk = self._make_chunk(gesetz, nr, doc, body)
                    if chunk["chunk_hash"] in seen_hashes:
                        continue
                    seen_hashes.add(chunk["chunk_hash"])
                    all_chunks.append(chunk)
                    found_for_this_paragraph = True

            if found_for_this_paragraph:
                covered_paragraphs.add((gesetz, nr))
                logger.info(f"  ✓ Begründung gefunden: § {nr} {gesetz} (zitiert {count}x)")

        logger.info(
            f"drucksachen_crawler: {len(all_chunks)} Begründungs-Chunks für "
            f"{len(covered_paragraphs)} §§ ({len(candidates)} Kandidaten geprüft)"
        )
        return all_chunks
