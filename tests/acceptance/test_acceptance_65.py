import pytest
from pathlib import Path

DOC = Path('/data/.openclaw/workspace-codex/projects/lexwolf/docs/desktop_tech_decision.md')
CRITERIA = ['Entwicklungsaufwand', 'Outlook', 'Anonymisierung', 'beA']
OPTIONS = ['Electron', 'WPF']
AUFWAND_KEYWORDS = ['Wochen', 'Monate', 'Stunden', 'h ', 'PT', 'Tage']


def test_dokument_existiert():
    assert DOC.exists(), f'desktop_tech_decision.md fehlt: {DOC}'


def test_beide_optionen_vorhanden():
    txt = DOC.read_text()
    for opt in OPTIONS:
        assert opt in txt, f'Option {opt} fehlt im Dokument'


def test_alle_kriterien_bewertet():
    txt = DOC.read_text()
    for krit in CRITERIA:
        assert krit in txt, f'Kriterium {krit} fehlt im Dokument'


def test_empfehlung_vorhanden():
    txt = DOC.read_text()
    assert any(w in txt for w in ['Empfehlung', 'empfehlen', 'empfohlen', 'Fazit', 'Entscheidung']),         'Keine klare Empfehlung im Dokument'


def test_aufwand_schaetzung_vorhanden():
    txt = DOC.read_text()
    has_aufwand = any(k in txt for k in AUFWAND_KEYWORDS)
    assert has_aufwand, f'Aufwand-Schätzung fehlt (erwartet eines von: {AUFWAND_KEYWORDS})'


def test_begruendung_vorhanden():
    txt = DOC.read_text()
    assert any(w in txt for w in ['weil', 'da ', 'wegen', 'Grund', 'aufgrund', 'da\n', 'Begründung']),         'Begründung für Empfehlung fehlt'
