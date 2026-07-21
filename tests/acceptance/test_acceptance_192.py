import os
import re

TOOL_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'tools', 'Anonymisierer')
TESTDATA = os.path.join(TOOL_DIR, 'Anonymisierer.Tests', 'TestData')
KUENDIGUNG = os.path.join(TESTDATA, 'Selbständige Arbeit kündigung.rtf')

MAIN_CS = os.path.join(TOOL_DIR, 'Main.cs')
FILEREADERS_CS = os.path.join(TOOL_DIR, 'Services', 'FileReaders.cs')


def test_read_rtf_document_implemented_in_main_cs():
    src = open(MAIN_CS, encoding='utf-8').read()
    assert 'ReadRtfDocument' in src, "ReadRtfDocument fehlt in Main.cs"
    assert "\\'" in src, "RTF-Hex-Escape-Behandlung (\\'xx) fehlt in ReadRtfDocument"


def test_rtf_wired_into_file_reader_dispatch():
    filereaders_src = open(FILEREADERS_CS, encoding='utf-8').read()
    assert 'RtfReader' in filereaders_src and '".rtf"' in filereaders_src, \
        "RtfReader fehlt/nicht verdrahtet in FileReaders.cs"


def rtf_to_plaintext(raw: bytes) -> str:
    text = raw.decode('cp1252', errors='ignore')
    text = re.sub(r"\\'([0-9a-fA-F]{2})", lambda m: bytes([int(m.group(1), 16)]).decode('cp1252'), text)
    text = text.replace('\\par', '\n')
    text = re.sub(r'\{\\\*.*?\}', '', text, flags=re.S)
    text = re.sub(r'\\[a-zA-Z]+-?\d*\s?', ' ', text)
    return re.sub(r'[{}]', '', text)


def test_kuendigung_rtf_extracts_name_datum_steuernr():
    assert os.path.exists(KUENDIGUNG), f"Testdatei fehlt: {KUENDIGUNG}"
    plaintext = rtf_to_plaintext(open(KUENDIGUNG, 'rb').read())
    assert 'Maysa Erkol' in plaintext, "Name fehlt im extrahierten Plaintext"
    assert '13.10.2019' in plaintext, "Datum fehlt im extrahierten Plaintext"
    assert '93060/15826' in plaintext, "SteuerNr fehlt im extrahierten Plaintext"
