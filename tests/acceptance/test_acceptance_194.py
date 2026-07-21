import os
import re
from striprtf.striprtf import rtf_to_text

TOOL_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'tools', 'Anonymisierer')
MAIN_CS = os.path.join(TOOL_DIR, 'Main.cs')
RTF_PATH = os.path.join(TOOL_DIR, 'Anonymisierer.Tests', 'TestData', 'Selbständige Arbeit kündigung.rtf')
STEUERNUMMER, USTIDNR = '93060/15826', 'DE296426579'


def _extract_pattern(src, needle):
    idx = src.find(needle)
    assert idx != -1, f"Regex-Fragment '{needle}' fehlt in Main.cs"
    start = src.rfind('@"', 0, idx)
    end = src.find('"', start + 2)
    return src[start + 2:end]


def test_steuernummer_und_ustidnr_werden_im_testdokument_ersetzt():
    plaintext = rtf_to_text(open(RTF_PATH, encoding='cp1252', errors='replace').read())
    assert STEUERNUMMER in plaintext and USTIDNR in plaintext, "Testnummern fehlen im Original-Dokument"

    src = open(MAIN_CS, encoding='utf-8').read()
    steuer_rx = re.compile(_extract_pattern(src, r'\d{2,5}\/\d{4,6}'))
    ust_rx = re.compile(_extract_pattern(src, r'DE\d{9}'))
    assert steuer_rx.search(STEUERNUMMER) and ust_rx.search(USTIDNR), "Regex erkennt Testnummern nicht"

    result = ust_rx.sub('[UST-1]', steuer_rx.sub('[STEUER-1]', plaintext))
    assert STEUERNUMMER not in result and USTIDNR not in result, "Originale dürfen nicht mehr vorkommen"

    body = re.search(r'AnonymizeTextAsync\s*\([^)]*\)\s*\{(.*?)return result;', src, re.S).group(1)
    assert body.count('EntityType.Aktenzeichen') >= 3, "Neue Regeln müssen in AnonymizeTextAsync registriert sein"
