"""Crawler für gesetze-im-internet.de via XML-TOC + XML-ZIP API."""
import io
import re
import time
import zipfile
import hashlib
import logging
import requests
import xml.etree.ElementTree as ET
from typing import List, Dict, Optional
from datetime import datetime

logger = logging.getLogger(__name__)

TOC_URL    = "https://www.gesetze-im-internet.de/gii-toc.xml"
PRIORITÄT  = [
    "kschg", "bgb", "zpo", "stgb", "gg", "hgb", "stpo", "bao", "sgb_1",
    "arbgg", "mabv", "insolvenzordnung", "gmbhg", "aktg", "urhg", "markg",
]


# Mapgesetz abbreviations to legal fields for proper categorization
GESetz_TO_FIELD = {
    "bgb": "Zivilrecht",
    "kschg": "Arbeitsrecht",
    "stgb": "Strafrecht",
    "zpo": "Zivilprozessrecht",
    "stpo": "Strafprozessrecht",
    "gg": "Verfassungsrecht",
    "hgb": "Handelsrecht",
    "gmbhg": "Gesellschaftsrecht",
    "aktg": "Aktienrecht",
    "urhg": "Urheberrecht",
    "patg": "Patentrecht",
    "markg": "Markenrecht",
    "arbgg": "Arbeitsrecht",
    "sgb_1": "Sozialrecht",
    "sgb_2": "Sozialrecht",
    "sgb_3": "Sozialrecht",
    "sgb_4": "Sozialrecht",
    "sgb_5": "Sozialrecht",
    "sgb_8": "Sozialrecht",
    "sgb_9": "Sozialrecht",
    "sgb_10": "Sozialrecht",
    "sgb_11": "Sozialrecht",
    "sgb_12": "Sozialrecht",
    "sgb_13": "Sozialrecht",
    "sgb_14": "Sozialrecht",
    "sgb_15": "Sozialrecht",
    "sgb_18": "Sozialrecht",
    "sgb_20": "Sozialrecht",
    "sgb_22": "Sozialrecht",
    "sgb_23": "Sozialrecht",
    "sgb_24": "Sozialrecht",
    "sgb_25": "Sozialrecht",
    "sgb_26": "Sozialrecht",
    "sgb_27": "Sozialrecht",
    "sgb_28": "Sozialrecht",
    "sgb_29": "Sozialrecht",
    "sgb_30": "Sozialrecht",
    "sgb_31": "Sozialrecht",
    "sgb_32": "Sozialrecht",
    "sgb_33": "Sozialrecht",
    "sgb_34": "Sozialrecht",
    "sgb_35": "Sozialrecht",
    "sgb_36": "Sozialrecht",
    "sgb_37": "Sozialrecht",
    "sgb_38": "Sozialrecht",
    "sgb_39": "Sozialrecht",
    "sgb_40": "Sozialrecht",
    "sgb_41": "Sozialrecht",
    "sgb_42": "Sozialrecht",
    "sgb_43": "Sozialrecht",
    "sgb_44": "Sozialrecht",
    "sgb_45": "Sozialrecht",
    "sgb_46": "Sozialrecht",
    "sgb_47": "Sozialrecht",
    "sgb_48": "Sozialrecht",
    "sgb_49": "Sozialrecht",
    "sgb_50": "Sozialrecht",
    "sgh": "Sozialrecht",
    "vg": "Verwaltungsrecht",
    "vfg": "Verwaltungsrecht",
    "burlg": "Arbeitsrecht",
    "tzfbg": "Arbeitsrecht",
    "burlg": "Arbeitsrecht",
    "burlg": "Arbeitsrecht",
}


class GesetzeImInternetCrawler:
    """Lädt deutsche Gesetze als XML-ZIP von gesetze-im-internet.de."""

    def __init__(self):
        self.session = requests.Session()
        self.session.headers["User-Agent"] = "LexWolf/1.0 (legal research tool)"

    def _map_gesetz_to_field(self, abk: str) -> str:
        """Mapsgesetz abbreviation to legal field."""
        key = abk.lower().replace("_", "_")
        return GESetz_TO_FIELD.get(key, "")

    def _get_toc(self) -> List[Dict]:
        """Lädt den kompletten Gesetzeskatalog (6000+ Gesetze)."""
        resp = self.session.get(TOC_URL, timeout=30)
        resp.raise_for_status()
        root = ET.fromstring(resp.content)
        laws = []
        for item in root.findall(".//item"):
            title = (item.findtext("title") or "").strip()
            link  = (item.findtext("link")  or "").strip()
            if title and link and link.endswith("xml.zip"):
                # Abkürzung aus URL ableiten, z.B. .../kschg/xml.zip → kschg
                abk = link.rstrip("/xml.zip").rsplit("/", 1)[-1]
                laws.append({"title": title, "link": link, "abk": abk})
        return laws

    def _make_chunk(self, law: Dict, title: str, text: str, nr: str) -> Dict:
        """Erzeugt ein Chunk-Dict mit allen Pflichtfeldern."""
        return {
            "text":          text,
            "title":         title,
            "paragraph_nr":  nr,
            "gesetz":        law["abk"].upper(),
            "legal_field":   self._map_gesetz_to_field(law["abk"]),
            "court":         "",
            "case_number":   "",
            "date":          None,
            "tags":          law["abk"],
            "source":        "gesetze-im-internet.de",
            "url":           law["link"],
            "chunk_hash":    hashlib.md5(text.encode()).hexdigest(),
            "document_type": "law",
        }

    @staticmethod
    def _split_sentences(text: str) -> List[str]:
        """Teilt Text an Satzenden auf (Punkte nach Wörtern/Ziffern)."""
        parts = re.split(r'(?<=[a-zäöüA-ZÄÖÜ0-9])\.\s+', text)
        result = []
        for p in parts:
            p = p.strip()
            if p:
                result.append(p if p.endswith('.') else p + '.')
        return result if len(result) > 1 else [text]

    def _norm_chunks(self, law: Dict, nr: str, head: str, body: str) -> List[Dict]:
        """Gibt alle Sub-Chunks einer Norm zurück: Parent + Absatz + Satz-Ebene."""
        result = []
        full_title = f"{nr} {head}".strip() or law["title"]

        # Parent-Chunk (voller Text, max 2000 Zeichen)
        parent_text = f"{full_title}\n{body}".strip()
        if len(parent_text) <= 2000:
            result.append(self._make_chunk(law, full_title, parent_text, nr))
        else:
            # Body zu lang → wird über Absatz-Ebene abgedeckt
            pass

        # Absatz-Ebene: teile bei (1), (2), …
        absatz_iter = list(re.finditer(r'\(\d+\)', body))
        if absatz_iter:
            for idx, match in enumerate(absatz_iter):
                start = match.start()
                end = absatz_iter[idx + 1].start() if idx + 1 < len(absatz_iter) else len(body)
                abs_body = body[start:end].strip()
                abs_nr = match.group(0)
                abs_title = f"{full_title} {abs_nr}"
                abs_text = f"{abs_title}\n{abs_body}".strip()

                if len(abs_text) <= 2000:
                    result.append(self._make_chunk(law, abs_title, abs_text, nr))
                    # Satz-Ebene innerhalb des Absatzes
                    sentences = self._split_sentences(abs_body)
                    if len(sentences) > 1:
                        for s_idx, sent in enumerate(sentences, 1):
                            s_title = f"{abs_title} S.{s_idx}"
                            s_text = f"{s_title}\n{sent}".strip()
                            result.append(self._make_chunk(law, s_title, s_text, nr))
                else:
                    # Absatz selbst zu lang → nur Satz-Ebene
                    sentences = self._split_sentences(abs_body)
                    for s_idx, sent in enumerate(sentences, 1):
                        s_title = f"{abs_title} S.{s_idx}"
                        s_text = f"{s_title}\n{sent}".strip()
                        if len(s_text) <= 2000:
                            result.append(self._make_chunk(law, s_title, s_text, nr))

        return result

    def _download_chunks(self, law: Dict) -> List[Dict]:
        """Lädt XML-ZIP eines Gesetzes und extrahiert Paragraphen als Chunks.

        Erzeugungsstrategie pro Norm:
          1. Parent-Chunk (voller Normtext)
          2. Je Absatz (d+) einen eigenen Chunk
          3. Je Satz innerhalb mehrsätziger Absätze einen eigenen Chunk
          4. Kontext-Fenster: (Norm[i-1] + Norm[i]) als zusammengesetzter Chunk
        """
        try:
            resp = self.session.get(law["link"], timeout=30)
            resp.raise_for_status()

            # Alle Normen sammeln, um Kontext-Fenster zu ermöglichen
            file_norms: List[tuple] = []
            with zipfile.ZipFile(io.BytesIO(resp.content)) as zf:
                for name in zf.namelist():
                    if not name.endswith(".xml"):
                        continue
                    try:
                        tree = ET.fromstring(zf.read(name))
                    except ET.ParseError:
                        continue
                    for norm in tree.iter("norm"):
                        nr   = norm.findtext(".//enbez") or ""
                        head = norm.findtext(".//titel") or ""
                        body_parts = [
                            (e.text or "").strip()
                            for e in norm.iter()
                            if e.tag in ("Content", "text", "P")
                            and e.text and e.text.strip()
                        ]
                        body = " ".join(body_parts).strip()
                        if body:
                            file_norms.append((nr, head, body))

            chunks: List[Dict] = []
            seen_hashes: set = set()

            def add(c: Dict):
                if c["chunk_hash"] not in seen_hashes:
                    seen_hashes.add(c["chunk_hash"])
                    chunks.append(c)

            for i, (nr, head, body) in enumerate(file_norms):
                # 1–3: Parent + Absatz + Satz
                for c in self._norm_chunks(law, nr, head, body):
                    add(c)

                # 4: Kontext-Fenster mit vorhergehender Norm
                if i > 0:
                    prev_nr, prev_head, prev_body = file_norms[i - 1]
                    ctx_title = f"{prev_nr}/{nr} Kontext"
                    ctx_body  = (
                        f"{prev_nr} {prev_head}\n{prev_body}\n\n"
                        f"{nr} {head}\n{body}"
                    ).strip()
                    if len(ctx_body) <= 2000:
                        add(self._make_chunk(law, ctx_title, ctx_body, nr))

            return chunks
        except Exception as e:
            logger.warning(f"Fehler bei {law['abk']}: {e}")
            return []

    def crawl_laws(self, limit: Optional[int] = None) -> List[Dict]:
        """
        Crawlt Gesetze. Prioritäts-Gesetze (KSchG, BGB …) zuerst,
        dann alle weiteren bis limit erreicht.
        """
        logger.info("Lade Gesetzeskatalog von gesetze-im-internet.de …")
        all_laws = self._get_toc()
        logger.info(f"Katalog: {len(all_laws)} Gesetze verfügbar")

        # Prioritäts-Gesetze vorne
        prio  = [l for l in all_laws if l["abk"] in PRIORITÄT]
        rest  = [l for l in all_laws if l["abk"] not in PRIORITÄT]
        laws  = prio + rest
        if limit:
            laws = laws[:limit]

        all_chunks: List[Dict] = []
        for i, law in enumerate(laws):
            logger.info(f"  [{i+1}/{len(laws)}] {law['abk']} — {law['title'][:60]}")
            chunks = self._download_chunks(law)
            logger.info(f"    → {len(chunks)} Chunks")
            all_chunks.extend(chunks)
            time.sleep(0.3)

        logger.info(f"gesetze-im-internet.de: {len(all_chunks)} Chunks total")
        return all_chunks
