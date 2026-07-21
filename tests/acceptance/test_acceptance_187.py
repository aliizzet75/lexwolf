"""Acceptance-Test T#187: IFileReader für .docx, .pdf, .txt, .eml als Plaintext"""
import os
import pytest

DESKTOP = os.path.join(os.path.dirname(__file__), "../../desktop")
CSPROJ = os.path.join(DESKTOP, "LexWolf.csproj")
SERVICES = os.path.join(DESKTOP, "Services")


def _find_cs(name_fragments):
    """Sucht .cs-Datei mit einem der Namensteile in Services/ oder Services/Readers/."""
    search_dirs = [SERVICES, os.path.join(SERVICES, "Readers")]
    for d in search_dirs:
        if not os.path.isdir(d):
            continue
        for f in os.listdir(d):
            if any(frag.lower() in f.lower() for frag in name_fragments) and f.endswith(".cs"):
                return os.path.join(d, f)
    return None


def test_csproj_hat_documentformat_openxml():
    assert "DocumentFormat.OpenXml" in open(CSPROJ).read(), \
        "NuGet DocumentFormat.OpenXml fehlt in LexWolf.csproj"


def test_csproj_hat_pdfpig():
    assert "PdfPig" in open(CSPROJ).read(), \
        "NuGet PdfPig fehlt in LexWolf.csproj"


def test_ifilereader_interface_existiert():
    p = _find_cs(["IFileReader"])
    assert p and os.path.isfile(p), f"IFileReader.cs fehlt in {SERVICES}"


def test_ifilereader_hat_string_methode():
    p = _find_cs(["IFileReader"])
    if not (p and os.path.isfile(p)):
        pytest.skip("IFileReader.cs noch nicht erstellt")
    content = open(p).read()
    assert "string" in content, "IFileReader muss eine string-Rückgabemethode definieren"


def test_docx_reader_existiert_und_nutzt_openxml():
    p = _find_cs(["DocxReader", "DocxFileReader"])
    assert p and os.path.isfile(p), f"DocxReader.cs fehlt in {SERVICES}"
    assert "OpenXml" in open(p).read() or "DocumentFormat" in open(p).read(), \
        "DocxReader muss DocumentFormat.OpenXml verwenden"


def test_pdf_reader_existiert_und_nutzt_pdfpig():
    p = _find_cs(["PdfReader", "PdfFileReader"])
    assert p and os.path.isfile(p), f"PdfReader.cs fehlt in {SERVICES}"
    assert "PdfPig" in open(p).read() or "UglyToad" in open(p).read(), \
        "PdfReader muss PdfPig (UglyToad.PdfPig) verwenden"


def test_txt_eml_reader_existiert():
    p = _find_cs(["TxtReader", "EmlReader", "TextFileReader", "PlaintextReader"])
    assert p and os.path.isfile(p), \
        f"TxtReader.cs / EmlReader.cs fehlt in {SERVICES}"
