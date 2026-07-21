import os
import re

TOOL_DIR = os.path.join(os.path.dirname(__file__), '..', '..', 'tools', 'Anonymisierer')
MAINWINDOW_CS = os.path.join(TOOL_DIR, 'MainWindow.xaml.cs')
TESTDATA = os.path.join(TOOL_DIR, 'Anonymisierer.Tests', 'TestData')

TESTDATEIEN = [
    '31_Erkol_Mietvertrag.pdf',
    'Selbständige Arbeit kündigung.rtf',
    'Dilara Frau Ruck.rtf',
    'KFZ Anmeldung Ali Erkol.pdf',
    'Dilara Frau Ruck.odt',
]


def test_testdateien_vorhanden():
    for name in TESTDATEIEN:
        assert os.path.exists(os.path.join(TESTDATA, name)), f"Testdatei fehlt: {name}"


def _export_click_body():
    src = open(MAINWINDOW_CS, encoding='utf-8').read()
    m = re.search(r'OnExportClicked\([^)]*\)\s*\{(.*)$', src, re.S)
    assert m, "OnExportClicked() nicht gefunden in MainWindow.xaml.cs"
    return m.group(1)


def test_export_schreibt_anon_txt_neben_original():
    body = _export_click_body()
    assert '_anon.txt' in body, "Export erzeugt keine <originalname>_anon.txt Datei"
    assert 'Path.GetDirectoryName(file.Path)' in body, \
        "_anon.txt muss im selben Ordner wie das Original geschrieben werden (nicht im _anonymisiert-Zielbaum)"


def test_export_schreibt_utf8():
    body = _export_click_body()
    assert 'Encoding.UTF8' in body, "Ausgabe der _anon.txt muss explizit UTF-8 kodiert werden"
