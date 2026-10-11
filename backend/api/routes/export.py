import io
import os
import re
import subprocess
import tempfile
from typing import Any, Dict

from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.shared import Cm, Pt
from fastapi import APIRouter, HTTPException
from fastapi.responses import StreamingResponse

from api.routes.kanzlei import add_briefkopf_to_document, _render_text

router = APIRouter(prefix="/api/export", tags=["export"])


def _add_page_number(section) -> None:
    """Fügt der übergebenen Sektion eine einfache Seitenzahl in der Fußzeile hinzu."""
    from docx.oxml.ns import qn

    footer = section.footer
    paragraph = footer.paragraphs[0] if footer.paragraphs else footer.add_paragraph()
    paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER

    run = paragraph.add_run()
    fldChar1 = run._element.makeelement(qn("w:fldChar"), {})
    fldChar1.set(qn("w:fldCharType"), "begin")

    instrText = run._element.makeelement(qn("w:instrText"), {})
    instrText.text = "PAGE"

    fldChar2 = run._element.makeelement(qn("w:fldChar"), {})
    fldChar2.set(qn("w:fldCharType"), "end")

    run._element.append(fldChar1)
    run._element.append(instrText)
    run._element.append(fldChar2)


def _template_dir() -> str:
    """Gibt das Verzeichnis der DOCX-Templates zurück."""
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(here, "..", "..", "templates", "docx"))


def _load_template(typ: str) -> Document:
    """Lädt ein Template-Dokument für den gegebenen Schriftsatztyp."""
    valid_types = {
        "klage",
        "klageerwiderung",
        "widerspruch",
        "antrag",
        "anwaltsschreiben",
        "mahnschreiben",
    }
    if typ not in valid_types:
        raise HTTPException(
            status_code=400,
            detail=f"Ungültiger Schriftsatztyp: {typ}. Möglich: {', '.join(sorted(valid_types))}",
        )
    template_path = os.path.join(_template_dir(), f"{typ}.docx")
    if not os.path.exists(template_path):
        raise HTTPException(
            status_code=500,
            detail=f"Template nicht gefunden: {template_path}",
        )
    return Document(template_path)


def _render_template_paragraphs(document: Document, text: str, metadaten: Dict[str, Any]) -> None:
    """Ersetzt Platzhalter in allen Absätzen und Tabellenzellen des Templates."""
    for paragraph in document.paragraphs:
        if any(ph in paragraph.text for ph in _PLACEHOLDERS):
            paragraph.text = _render_placeholder_text(paragraph.text, text, metadaten)
    for table in document.tables:
        for row in table.rows:
            for cell in row.cells:
                for paragraph in cell.paragraphs:
                    if any(ph in paragraph.text for ph in _PLACEHOLDERS):
                        paragraph.text = _render_placeholder_text(paragraph.text, text, metadaten)


_PLACEHOLDERS = (
    "{DATUM}",
    "{AKTENZEICHEN}",
    "{MANDANT}",
    "{MANDANT_ADRESSE}",
    "{BEKLAGTER}",
    "{BEKLAGTER_ADRESSE}",
    "{GEGNER}",
    "{GEGNER_ADRESSE}",
    "{SCHULDNER}",
    "{SCHULDNER_ADRESSE}",
    "{EMPFAENGER}",
    "{EMPFAENGER_ADRESSE}",
    "{GERICHT}",
    "{ANTRAEGE}",
    "{BEGRUENDUNG}",
    "{BETREFF}",
    "{KANZLEI}",
    "{ANWALT}",
)


def _render_placeholder_text(paragraph_text: str, text: str, metadaten: Dict[str, Any]) -> str:
    """Ersetzt Platzhalter in einem einzelnen Absatz."""
    today = _render_text("{DATUM}", metadaten)
    result = paragraph_text
    result = result.replace("{DATUM}", today)
    result = result.replace("{AKTENZEICHEN}", str(metadaten.get("aktenzeichen") or ""))
    result = result.replace("{MANDANT}", str(metadaten.get("mandant") or ""))
    result = result.replace("{MANDANT_ADRESSE}", str(metadaten.get("mandant_adresse") or ""))
    result = result.replace("{BEKLAGTER}", str(metadaten.get("beklagter") or ""))
    result = result.replace("{BEKLAGTER_ADRESSE}", str(metadaten.get("beklagter_adresse") or ""))
    result = result.replace("{GEGNER}", str(metadaten.get("gegner") or ""))
    result = result.replace("{GEGNER_ADRESSE}", str(metadaten.get("gegner_adresse") or ""))
    result = result.replace("{SCHULDNER}", str(metadaten.get("schuldner") or ""))
    result = result.replace("{SCHULDNER_ADRESSE}", str(metadaten.get("schuldner_adresse") or ""))
    result = result.replace("{EMPFAENGER}", str(metadaten.get("empfaenger") or ""))
    result = result.replace("{EMPFAENGER_ADRESSE}", str(metadaten.get("empfaenger_adresse") or ""))
    result = result.replace("{GERICHT}", str(metadaten.get("gericht") or ""))
    result = result.replace("{BETREFF}", str(metadaten.get("betreff") or ""))
    result = result.replace("{KANZLEI}", str(metadaten.get("kanzlei") or ""))
    result = result.replace("{ANWALT}", str(metadaten.get("anwalt") or ""))

    # {ANTRAEGE} und {BEGRUENDUNG} werden aus dem Haupttext abgeleitet
    if "{ANTRAEGE}" in result or "{BEGRUENDUNG}" in result:
        antraege_text, begruendung_text = _split_antraege_begruendung(text)
        result = result.replace("{ANTRAEGE}", antraege_text)
        result = result.replace("{BEGRUENDUNG}", begruendung_text)

    return result


def _split_antraege_begruendung(text: str) -> tuple:
    """Versucht, Anträge von Begründung im Text zu trennen."""
    antraege_text = ""
    begruendung_text = text.strip()

    # Trennmarker: "Begründung:" oder "Begründung" als eigene Zeile
    marker_match = re.search(r"(?im)^\s*Begründung:?\s*$", begruendung_text)
    if marker_match:
        antraege_text = begruendung_text[: marker_match.start()].strip()
        begruendung_text = begruendung_text[marker_match.end():].strip()
    else:
        # Fallback: Text ohne Überschriften als Anträge, Rest als Begründung
        lines = begruendung_text.splitlines()
        antrags_lines = []
        begruendung_lines = []
        for i, line in enumerate(lines):
            stripped = line.strip().lower()
            if stripped.startswith("antrag") or stripped.startswith("anträge"):
                antrags_lines.extend(lines[i + 1:])
                begruendung_lines = []
                break
            else:
                begruendung_lines.append(line)
        if antrags_lines:
            antraege_text = "\n".join(antrags_lines).strip()
            begruendung_text = "\n".join(begruendung_lines).strip()
        else:
            # Ohne klare Trennung: ganzer Text als Begründung, Anträge leer
            antraege_text = ""
            begruendung_text = text.strip()

    return antraege_text, begruendung_text


def _build_document(text: str, metadaten: Dict[str, Any]) -> Document:
    """Baut das formatierte DOCX-Dokument."""
    typ = metadaten.get("schriftsatz_typ")

    if typ:
        # Template-basierter Export
        document = _load_template(typ)

        # Sektionseinstellungen normalisieren
        section = document.sections[0]
        section.left_margin = Cm(2.5)
        section.right_margin = Cm(2.5)
        section.top_margin = Cm(2.5)
        section.bottom_margin = Cm(2.5)

        style = document.styles["Normal"]
        font = style.font
        font.name = "Times New Roman"
        font.size = Pt(12)

        add_briefkopf_to_document(document, metadaten)
        _render_template_paragraphs(document, text, metadaten)
        _add_page_number(section)
        return document

    # Standard-Layout ohne Template
    document = Document()
    section = document.sections[0]
    section.left_margin = Cm(2.5)
    section.right_margin = Cm(2.5)
    section.top_margin = Cm(2.5)
    section.bottom_margin = Cm(2.5)

    style = document.styles["Normal"]
    font = style.font
    font.name = "Times New Roman"
    font.size = Pt(12)

    # Briefkopf einfügen, falls vorhanden
    add_briefkopf_to_document(document, metadaten)

    # Seitenzahlen in Fußzeile
    _add_page_number(section)

    # Optional: Metadaten als Kopfblock, falls vorhanden
    if metadaten:
        meta_lines = []
        for key in ("gericht", "aktenzeichen", "datum", "betreff"):
            value = metadaten.get(key)
            if value:
                meta_lines.append(str(value))
        if meta_lines:
            for line in meta_lines:
                p = document.add_paragraph(line)
                p.paragraph_format.space_after = Pt(0)
                p.paragraph_format.space_before = Pt(0)
            document.add_paragraph()

    # Haupttext: Leerzeilen als neue Absätze behandeln
    for paragraph_text in re.split(r"\n\n+", text.strip()):
        for line in paragraph_text.split("\n"):
            line = line.strip()
            if not line:
                continue
            document.add_paragraph(line)
        # Nur einen Absatz pro Block ausgeben; Zeilenumbrüche innerhalb eines
        # Blocks werden zu einzelnen Absätzen, da juristische Schriftsätze
        # normalerweise harte Absätze verwenden.

    return document


@router.post("/docx")
async def export_docx(payload: Dict[str, Any]):
    """
    Exportiert einen Schriftsatz als .docx mit juristischer Standardformatierung:
    - Schriftart Times New Roman 12pt
    - Seitenränder 2,5 cm
    - Automatische Seitenzahlen in der Fußzeile
    """
    text = payload.get("text", "")
    metadaten = payload.get("metadaten") or {}
    text = _render_text(text, metadaten)

    document = _build_document(text, metadaten)

    buffer = io.BytesIO()
    document.save(buffer)
    buffer.seek(0)

    filename = "schriftsatz.docx"
    if metadaten.get("aktenzeichen"):
        safe_az = re.sub(r"[^\w\-]", "_", str(metadaten["aktenzeichen"]))
        filename = f"schriftsatz_{safe_az}.docx"

    return StreamingResponse(
        buffer,
        media_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        headers={"Content-Disposition": f'attachment; filename="{filename}"'},
    )


@router.post("/pdf")
async def export_pdf(payload: Dict[str, Any]):
    """
    Exportiert einen Schriftsatz als druckfertiges PDF.
    Konvertiert das DOCX intern mit LibreOffice headless nach PDF.
    """
    text = payload.get("text", "")
    metadaten = payload.get("metadaten") or {}
    text = _render_text(text, metadaten)

    document = _build_document(text, metadaten)

    # LibreOffice-Befehl ermitteln
    with tempfile.TemporaryDirectory() as tmpdir:
        docx_path = os.path.join(tmpdir, "schriftsatz.docx")
        document.save(docx_path)

        # Erst LibreOffice headless probieren; falls nicht verfügbar,
        # Fallback auf WeasyPrint über HTML-Zwischenschritt.
        office_cmd = None
        for cmd in ("soffice", "libreoffice"):
            if subprocess.run(["which", cmd], capture_output=True).returncode == 0:
                office_cmd = cmd
                break

        pdf_path = os.path.join(tmpdir, "schriftsatz.pdf")

        if office_cmd:
            result = subprocess.run(
                [
                    office_cmd,
                    "--headless",
                    "--convert-to",
                    "pdf",
                    "--outdir",
                    tmpdir,
                    docx_path,
                ],
                capture_output=True,
                text=True,
                timeout=60,
            )
            if result.returncode != 0:
                raise HTTPException(
                    status_code=500,
                    detail=f"LibreOffice PDF-Konvertierung fehlgeschlagen: {result.stderr}",
                )
            if not os.path.exists(pdf_path):
                raise HTTPException(
                    status_code=500,
                    detail="PDF-Datei wurde von LibreOffice nicht erzeugt.",
                )
            with open(pdf_path, "rb") as f:
                pdf_bytes = f.read()
        else:
            try:
                pdf_bytes = _pdf_with_embedded_fonts(document, tmpdir)
            except Exception as exc:
                raise HTTPException(
                    status_code=500,
                    detail=f"PDF-Konvertierung mit eingebetteten Fonts fehlgeschlagen: {exc}",
                ) from exc

    buffer = io.BytesIO(pdf_bytes)
    buffer.seek(0)

    filename = "schriftsatz.pdf"
    if metadaten.get("aktenzeichen"):
        safe_az = re.sub(r"[^\w\-]", "_", str(metadaten["aktenzeichen"]))
        filename = f"schriftsatz_{safe_az}.pdf"

    return StreamingResponse(
        buffer,
        media_type="application/pdf",
        headers={"Content-Disposition": f'attachment; filename="{filename}"'},
    )


def _pdf_with_embedded_fonts(document: Document, tmpdir: str) -> bytes:
    """Erzeugt ein PDF mit vollständig eingebetteten TrueType-Fonts via reportlab.

    Dies ist der beA/ERV-kompatible Pfad, da reportlab die verwendeten
    Schriftarten als Subset in die PDF-Datei einbettet (Pflicht für beA).
    """
    from reportlab.pdfgen import canvas
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.lib.pagesizes import A4
    from reportlab.lib.units import cm

    register_fonts = {
        "regular": "/usr/share/fonts/truetype/liberation/LiberationSerif-Regular.ttf",
        "bold": "/usr/share/fonts/truetype/liberation/LiberationSerif-Bold.ttf",
        "italic": "/usr/share/fonts/truetype/liberation/LiberationSerif-Italic.ttf",
        "bold_italic": "/usr/share/fonts/truetype/liberation/LiberationSerif-BoldItalic.ttf",
    }

    for style, path in register_fonts.items():
        if os.path.exists(path):
            pdfmetrics.registerFont(TTFont(f"LiberationSerif-{style}", path))

    # reportlab erzeugt intern ein /Helvetica-Objekt. Da beA/ERV alle Fonts
    # eingebettet verlangen, ersetzen wir Helvetica durch denselben
    # LiberationSerif-Regular-Font, damit keine nicht-eingebettete Schriftart
    # im PDF verbleibt.
    if os.path.exists(register_fonts["regular"]):
        pdfmetrics.registerFont(TTFont("Helvetica", register_fonts["regular"]))

    buf = io.BytesIO()
    c = canvas.Canvas(buf, pagesize=A4)

    width, height = A4
    margin = 2.5 * cm
    usable_width = width - 2 * margin
    y = height - margin
    page_number = 1

    def new_page():
        nonlocal y, page_number
        c.showPage()
        page_number += 1
        y = height - margin

    def draw_footer():
        c.setFont("LiberationSerif-regular", 12)
        c.drawCentredString(width / 2, margin - 0.8 * cm, str(page_number))

    def set_font(run):
        style = "regular"
        if run.bold and run.italic:
            style = "bold_italic"
        elif run.bold:
            style = "bold"
        elif run.italic:
            style = "italic"
        c.setFont(f"LiberationSerif-{style}", 12)

    for paragraph in document.paragraphs:
        txt = paragraph.text.strip()
        if not txt:
            continue

        lines = []
        if paragraph.runs:
            line = []
            for run in paragraph.runs:
                for word in run.text.split(" "):
                    if word == "":
                        continue
                    line.append((word, run))
                if run.text.endswith(" "):
                    line.append((" ", run))
            current_line = []
            current_width = 0
            for word, run in line:
                if word == " ":
                    current_line.append((" ", run))
                    continue
                style = "regular"
                if run.bold and run.italic:
                    style = "bold_italic"
                elif run.bold:
                    style = "bold"
                elif run.italic:
                    style = "italic"
                c.setFont(f"LiberationSerif-{style}", 12)
                word_width = c.stringWidth(word + " ", f"LiberationSerif-{style}", 12)
                if current_width + word_width > usable_width and current_line:
                    lines.append(current_line)
                    current_line = [(word, run)]
                    current_width = word_width
                else:
                    current_line.append((word, run))
                    current_width += word_width
            if current_line:
                lines.append(current_line)
        else:
            c.setFont("LiberationSerif-regular", 12)
            words = txt.split(" ")
            current_line = []
            current_width = 0
            for word in words:
                word_width = c.stringWidth(word + " ", "LiberationSerif-regular", 12)
                if current_width + word_width > usable_width and current_line:
                    lines.append([(w, None) for w in current_line])
                    current_line = [word]
                    current_width = word_width
                else:
                    current_line.append(word)
                    current_width += word_width
            if current_line:
                lines.append([(w, None) for w in current_line])

        for line in lines:
            if y < margin + 0.5 * cm:
                draw_footer()
                new_page()
            x = margin
            for word, run in line:
                if run is not None:
                    set_font(run)
                    style = "regular"
                    if run.bold and run.italic:
                        style = "bold_italic"
                    elif run.bold:
                        style = "bold"
                    elif run.italic:
                        style = "italic"
                    c.drawString(x, y, word + " ")
                    x += c.stringWidth(word + " ", f"LiberationSerif-{style}", 12)
                else:
                    c.setFont("LiberationSerif-regular", 12)
                    c.drawString(x, y, word + " ")
                    x += c.stringWidth(word + " ", "LiberationSerif-regular", 12)
            y -= 0.6 * cm

    draw_footer()
    c.save()
    return buf.getvalue()


def _docx_to_html(document: Document) -> str:
    """Konvertiert ein python-docx Document in ein minimales HTML für WeasyPrint."""
    lines = []
    lines.append("<!DOCTYPE html>")
    lines.append('<html lang="de"><head><meta charset="UTF-8"><title>Schriftsatz</title></head><body>')

    # Kopfblock / Briefkopf: Absätze vor dem Haupttext rendern
    for paragraph in document.paragraphs:
        txt = paragraph.text.strip()
        if not txt:
            continue
        lines.append(f"<p>{_escape_html(txt)}</p>")

    lines.append("</body></html>")
    return "\n".join(lines)


def _escape_html(text: str) -> str:
    """Escapes & < > für HTML-Ausgabe."""
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def _pdf_css() -> str:
    """CSS für das PDF-Layout: Times New Roman 12pt, 2,5 cm Ränder, Zeilenabstand.

    Verwendet Liberation Serif als Web-Safe Fallback zu Times New Roman,
    weil WeasyPrint die TrueType-Font vollständig einbettet (beA/ERV-konform).
    """
    return """
        @page {
            size: A4;
            margin: 2.5cm 2.5cm 2.5cm 2.5cm;
            @bottom-center { content: counter(page); font-family: "Liberation Serif", "Times New Roman", serif; font-size: 12pt; }
        }
        body { font-family: "Liberation Serif", "Times New Roman", serif; font-size: 12pt; line-height: 1.5; }
        p { margin: 0 0 0.5em 0; text-align: justify; }
    """
