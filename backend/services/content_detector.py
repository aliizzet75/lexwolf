import io
import logging
import xml.etree.ElementTree as ET
from pathlib import Path

logger = logging.getLogger(__name__)

GAEB_EXTENSIONS = {".x81", ".x83", ".gaeb"}
LV_KEYWORDS = {"ausschreibung", "leistungsverzeichnis", "vergabe"}


def _extract_pdf_text(content: bytes) -> str:
    try:
        import pypdf
        reader = pypdf.PdfReader(io.BytesIO(content))
        parts = []
        for page in reader.pages:
            text = page.extract_text()
            if text:
                parts.append(text)
        return " ".join(parts)
    except Exception as e:
        logger.debug("PDF text extraction failed: %s", e)
        return ""


def _contains_lv_keyword(text: str) -> bool:
    lowered = text.lower()
    return any(kw in lowered for kw in LV_KEYWORDS)


def _is_gaeb_xml(content: bytes) -> bool:
    if not content:
        return False
    try:
        root = ET.fromstring(content)
        tag = root.tag.lower()
        return "gaeb" in tag or "award" in tag or "boq" in tag
    except ET.ParseError:
        return False


def is_ausschreibung(filename: str, content: bytes) -> bool:
    """Return True if the file is an Ausschreibung/LV document."""
    suffix = Path(filename).suffix.lower()

    if suffix in GAEB_EXTENSIONS:
        return True

    if suffix == ".pdf":
        text = _extract_pdf_text(content)
        return _contains_lv_keyword(text)

    # For any other format try text decoding first, then GAEB XML
    if _is_gaeb_xml(content):
        return True

    try:
        text = content.decode("utf-8", errors="ignore")
    except Exception:
        return False

    return _contains_lv_keyword(text)
