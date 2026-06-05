import io
import pytest
import xml.etree.ElementTree as ET

GAEB_EXTENSIONS = {".x81", ".x83", ".gaeb"}
LV_KEYWORDS = {"ausschreibung", "leistungsverzeichnis", "lv", "vergabe"}


def _import_detector():
    from backend.services.content_detector import is_ausschreibung
    return is_ausschreibung


def test_gaeb_xml_erkannt():
    is_ausschreibung = _import_detector()
    gaeb_xml = b"""<?xml version="1.0"?><GAEB><Award><BoQ><BoQBody/></BoQ></Award></GAEB>"""
    assert is_ausschreibung(filename="test.x81", content=gaeb_xml) is True


def test_gaeb_dateiendung_x83():
    is_ausschreibung = _import_detector()
    assert is_ausschreibung(filename="angebot.x83", content=b"") is True


def test_gaeb_dateiendung_gaeb():
    is_ausschreibung = _import_detector()
    assert is_ausschreibung(filename="projekt.gaeb", content=b"") is True


def test_pdf_mit_lv_keyword():
    is_ausschreibung = _import_detector()
    import pypdf
    buf = io.BytesIO()
    writer = pypdf.PdfWriter()
    page = writer.add_blank_page(width=595, height=842)
    writer.write(buf)
    pdf_bytes = buf.getvalue()
    # Minimal PDF ohne Text → False
    assert is_ausschreibung(filename="dok.pdf", content=pdf_bytes) is False


def test_nicht_ausschreibung():
    is_ausschreibung = _import_detector()
    assert is_ausschreibung(filename="rechnung.pdf", content=b"Rechnung Nr. 42") is False


def test_txt_mit_ausschreibung_keyword():
    is_ausschreibung = _import_detector()
    assert is_ausschreibung(filename="info.txt", content=b"Ausschreibung Strassenbau 2024") is True
