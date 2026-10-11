import io
import os
import re
from datetime import date
from typing import Optional

from docx import Document
from fastapi import APIRouter, File, HTTPException, UploadFile

router = APIRouter(prefix="/api/kanzlei", tags=["kanzlei"])

# In-Memory-Speicher für den hochgeladenen Briefkopf (pro Prozess).
# Für Multi-Instance-Betrieb sollte hier eine DB oder ein Shared-Volume folgen.
_BRIEFKOPF_STORAGE: dict = {"docx": None, "png": None}


def _briefkopf_dir() -> str:
    """Verzeichnis für persistent gespeicherte Briefkopf-Templates."""
    here = os.path.dirname(os.path.abspath(__file__))
    base = os.path.abspath(os.path.join(here, "..", "..", "..", "data", "briefkoepfe"))
    os.makedirs(base, exist_ok=True)
    return base


def _briefkopf_docx_path() -> str:
    return os.path.join(_briefkopf_dir(), "briefkopf.docx")


def _briefkopf_png_path() -> str:
    return os.path.join(_briefkopf_dir(), "briefkopf.png")


def _render_text(text: str, metadaten: dict) -> str:
    """Ersetzt Platzhalter im Text durch Metadaten."""
    today = date.today().strftime("%d.%m.%Y")
    result = text.replace("{DATUM}", metadaten.get("datum") or today)
    result = result.replace("{AKTENZEICHEN}", str(metadaten.get("aktenzeichen") or ""))
    result = result.replace("{MANDANT}", str(metadaten.get("mandant") or ""))
    return result


@router.post("/briefkopf")
async def upload_briefkopf(file: UploadFile = File(...)):
    """
    Lädt ein Kanzlei-Briefkopf-Template hoch (DOCX oder PNG).
    Der Briefkopf wird beim nächsten Export als erste Seite/Kopfzeile verwendet.
    """
    allowed_docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    allowed_png = "image/png"
    content_type = file.content_type or ""
    filename = file.filename or ""

    if content_type == allowed_docx or filename.lower().endswith(".docx"):
        target_path = _briefkopf_docx_path()
        _BRIEFKOPF_STORAGE["png"] = None
    elif content_type == allowed_png or filename.lower().endswith(".png"):
        target_path = _briefkopf_png_path()
        _BRIEFKOPF_STORAGE["docx"] = None
    else:
        raise HTTPException(
            status_code=400,
            detail="Nur DOCX oder PNG als Briefkopf-Template erlaubt.",
        )

    content = await file.read()
    with open(target_path, "wb") as f:
        f.write(content)

    if target_path.endswith(".docx"):
        _BRIEFKOPF_STORAGE["docx"] = target_path
    else:
        _BRIEFKOPF_STORAGE["png"] = target_path

    return {"status": "ok", "filename": filename, "type": os.path.splitext(target_path)[1][1:]}


def get_briefkopf_docx_path() -> Optional[str]:
    """Gibt den Pfad zum gespeicherten DOCX-Briefkopf zurück, falls vorhanden."""
    mem = _BRIEFKOPF_STORAGE.get("docx")
    if mem and os.path.exists(mem):
        return mem
    default = _briefkopf_docx_path()
    if os.path.exists(default):
        _BRIEFKOPF_STORAGE["docx"] = default
        return default
    return None


def get_briefkopf_png_path() -> Optional[str]:
    """Gibt den Pfad zum gespeicherten PNG-Briefkopf zurück, falls vorhanden."""
    mem = _BRIEFKOPF_STORAGE.get("png")
    if mem and os.path.exists(mem):
        return mem
    default = _briefkopf_png_path()
    if os.path.exists(default):
        _BRIEFKOPF_STORAGE["png"] = default
        return default
    return None


def add_briefkopf_to_document(document: Document, metadaten: dict) -> None:
    """
    Fügt den gespeicherten Briefkopf dem Dokument hinzu.
    DOCX-Briefköpfe werden als erste Seite eingefügt;
    PNG-Briefköpfe werden in die Kopfzeile der ersten Seite gesetzt.
    """
    docx_path = get_briefkopf_docx_path()
    png_path = get_briefkopf_png_path()

    if docx_path and os.path.exists(docx_path) and os.path.getsize(docx_path) > 0:
        try:
            _add_docx_briefkopf(document, docx_path, metadaten)
        except Exception:
            # Ungültiges Template -> Fallback ohne Briefkopf
            pass
    elif png_path and os.path.exists(png_path) and os.path.getsize(png_path) > 0:
        try:
            _add_png_briefkopf(document, png_path)
        except Exception:
            pass


def _replace_placeholders_in_document(document: Document, metadaten: dict) -> None:
    """Ersetzt Platzhalter in allen Absätzen und Tabellen des Dokuments."""
    for paragraph in document.paragraphs:
        if any(ph in paragraph.text for ph in ("{DATUM}", "{AKTENZEICHEN}", "{MANDANT}")):
            paragraph.text = _render_text(paragraph.text, metadaten)
    for table in document.tables:
        for row in table.rows:
            for cell in row.cells:
                for paragraph in cell.paragraphs:
                    if any(ph in paragraph.text for ph in ("{DATUM}", "{AKTENZEICHEN}", "{MANDANT}")):
                        paragraph.text = _render_text(paragraph.text, metadaten)


def _add_docx_briefkopf(document: Document, docx_path: str, metadaten: dict) -> None:
    """Kopiert alle Elemente aus einem DOCX-Briefkopf in das Zieldokument."""
    template = Document(docx_path)
    # Platzhalter im Briefkopf selbst ersetzen
    _replace_placeholders_in_document(template, metadaten)

    # Sektionseinstellungen aus dem Template übernehmen (z. B. Ränder, falls definiert)
    if template.sections:
        src_section = template.sections[0]
        dst_section = document.sections[0]
        try:
            dst_section.page_width = src_section.page_width
            dst_section.page_height = src_section.page_height
            dst_section.left_margin = src_section.left_margin
            dst_section.right_margin = src_section.right_margin
            dst_section.top_margin = src_section.top_margin
            dst_section.bottom_margin = src_section.bottom_margin
        except Exception:
            pass

    for element in template.element.body:
        document.element.body.insert(0, element)


def _add_png_briefkopf(document: Document, png_path: str) -> None:
    """Fügt ein PNG-Briefkopf-Bild in die Kopfzeile der ersten Seite ein."""
    section = document.sections[0]
    header = section.header
    paragraph = header.paragraphs[0] if header.paragraphs else header.add_paragraph()
    paragraph.alignment = 1  # zentriert
    run = paragraph.add_run()
    run.add_picture(png_path, width=None)
