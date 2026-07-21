import os
import re
import zipfile

TOOL_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'tools', 'Anonymisierer')
TESTDATA = os.path.join(TOOL_DIR, 'Anonymisierer.Tests', 'TestData', 'Dilara Frau Ruck.odt')
MAIN_CS = os.path.join(TOOL_DIR, 'Main.cs')


def test_rx_betrag_matches_ganzzahl_vor_eur():
    src = open(MAIN_CS, encoding='utf-8').read()
    m = re.search(r'_rxBetrag\s*=\s*\n?\s*new\(@"(.*?)",', src, re.S)
    assert m, "_rxBetrag Definition nicht gefunden in Main.cs"
    pattern = m.group(1)
    rx = re.compile(pattern)
    assert rx.search('219 EUR'), "_rxBetrag erkennt '219 EUR' nicht"
    assert rx.search('800 EUR'), "_rxBetrag erkennt '800 EUR' nicht"
    assert rx.search('219 €'), "_rxBetrag erkennt '219 €' nicht"
    assert rx.search('1.234,56'), "_rxBetrag erkennt bestehendes Komma-Format nicht mehr"


def test_dilara_odt_zwei_gleiche_betraege_gleiche_ersetzung():
    assert os.path.exists(TESTDATA), f"Testdatei fehlt: {TESTDATA}"
    with zipfile.ZipFile(TESTDATA) as z:
        content_xml = z.read('content.xml').decode('utf-8')
    fragments = re.findall(r'<text:(?:p|h|span)[^>]*>(.*?)</text:(?:p|h|span)>', content_xml, re.S)
    plaintext = '\n'.join(re.sub(r'<[^>]+>', '', f).strip() for f in fragments if f.strip())

    treffer_219 = re.findall(r'219\s*€', plaintext)
    treffer_800 = re.findall(r'800\s*€', plaintext)
    assert len(treffer_219) == 2, f"Erwartet 2x '219 €' im Dokument, gefunden: {len(treffer_219)}"
    assert len(treffer_800) == 1, f"Erwartet 1x '800 €' im Dokument, gefunden: {len(treffer_800)}"
