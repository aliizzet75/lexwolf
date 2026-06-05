import pytest
from pathlib import Path

PROTOCOL = Path('/data/.openclaw/workspace-codex/projects/lexwolf/docs/quality_protocol.md')
THRESHOLD = Path('/data/.openclaw/workspace-codex/projects/lexwolf/backend/threshold_config.json')
CI_GATE = Path('/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/ci_quality_gate.py')


def test_quality_protocol_exists():
    assert PROTOCOL.exists(), f'quality_protocol.md fehlt: {PROTOCOL}'


def test_protocol_contains_halluzinations_threshold():
    txt = PROTOCOL.read_text()
    assert 'halluzinations_rate' in txt or 'Halluzination' in txt, 'Metrik halluzinations_rate fehlt'
    assert '5%' in txt or '0.05' in txt, 'Schwellenwert < 5% fehlt'


def test_protocol_contains_quellen_genauigkeit():
    txt = PROTOCOL.read_text()
    assert 'quellen_genauigkeit' in txt or 'Quellengenauigkeit' in txt, 'Metrik quellen_genauigkeit fehlt'
    assert '95%' in txt or '0.95' in txt, 'Schwellenwert > 95% fehlt'


def test_protocol_contains_vollstaendigkeits_score():
    txt = PROTOCOL.read_text()
    assert 'vollstaendigkeits_score' in txt or 'Vollständigkeit' in txt, 'Metrik vollstaendigkeits_score fehlt'
    assert '80%' in txt or '0.80' in txt or '0.8' in txt, 'Schwellenwert > 80% fehlt'


def test_protocol_describes_freigabe_prozess():
    txt = PROTOCOL.read_text()
    assert 'Freigabe' in txt or 'freigabe' in txt, 'Freigabe-Prozess fehlt'
    assert any(w in txt for w in ['verantwortlich', 'Verantwortlich', 'Lead', 'QA', 'Review']), \
        'Verantwortliche Person/Rolle fehlt'


def test_ci_quality_gate_exists():
    assert CI_GATE.exists(), f'CI/CD-Gate-Skript fehlt: {CI_GATE}'


def test_ci_gate_enforces_thresholds():
    src = CI_GATE.read_text()
    assert '0.05' in src or '5' in src, 'Halluzinations-Schwelle im CI-Gate fehlt'
    assert '0.95' in src or '95' in src, 'Quellengenauigkeits-Schwelle im CI-Gate fehlt'
    assert '0.8' in src or '80' in src, 'Vollständigkeits-Schwelle im CI-Gate fehlt'
