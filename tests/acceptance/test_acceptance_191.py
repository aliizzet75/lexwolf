import os
import re
import zipfile

TOOL_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'tools', 'Anonymisierer')
TESTDATA = os.path.join(TOOL_DIR, 'Anonymisierer.Tests', 'TestData', 'Dilara Frau Ruck.odt')

MAIN_CS = os.path.join(TOOL_DIR, 'Main.cs')
FILEREADERS_CS = os.path.join(TOOL_DIR, 'Services', 'FileReaders.cs')
FILETREE_VM_CS = os.path.join(TOOL_DIR, 'ViewModels', 'FileTreeViewModel.cs')


def test_read_odt_document_implemented_in_main_cs():
    src = open(MAIN_CS, encoding='utf-8').read()
    assert 'ReadOdtDocument' in src, "ReadOdtDocument fehlt in Main.cs"
    assert 'ZipFile' in src and 'content.xml' in src, "ODT muss als ZIP mit content.xml gelesen werden"


def test_odt_wired_into_file_reader_dispatch():
    filereaders_src = open(FILEREADERS_CS, encoding='utf-8').read()
    assert 'OdtReader' in filereaders_src, "OdtReader fehlt in FileReaders.cs"
    assert '".odt"' in filereaders_src, "OdtReader muss .odt behandeln"

    filetree_src = open(FILETREE_VM_CS, encoding='utf-8').read()
    assert '.odt' in filetree_src, ".odt fehlt in unterstützten Dateiendungen (FileTreeViewModel)"


def test_dilara_odt_extracts_name_datum_betrag():
    assert os.path.exists(TESTDATA), f"Testdatei fehlt: {TESTDATA}"

    with zipfile.ZipFile(TESTDATA) as z:
        content_xml = z.read('content.xml').decode('utf-8')

    fragments = re.findall(r'<text:(?:p|h|span)[^>]*>(.*?)</text:(?:p|h|span)>', content_xml, re.S)
    plaintext = '\n'.join(re.sub(r'<[^>]+>', '', f).strip() for f in fragments if f.strip())

    assert 'Erkol' in plaintext, "Name fehlt im extrahierten Plaintext"
    assert re.search(r'\d{2}\.\d{2}\.\d{4}', plaintext), "Datum fehlt im extrahierten Plaintext"
    assert re.search(r'\d+\s*€', plaintext), "Betrag fehlt im extrahierten Plaintext"
