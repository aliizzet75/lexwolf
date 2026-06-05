import os
import sys
import pytest
from pathlib import Path

WORKFLOW_PATH = Path("/data/.openclaw/workspace-codex/projects/lexwolf/.github/workflows/quality_tests.yml")
METRICS_PATH = Path("/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/metrics.py")
QUALITY_DIR = Path("/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality")

sys.path.insert(0, str(QUALITY_DIR))


def test_workflow_exists():
    assert WORKFLOW_PATH.exists(), f"quality_tests.yml fehlt: {WORKFLOW_PATH}"


def test_workflow_triggers_on_main():
    content = WORKFLOW_PATH.read_text()
    assert "branches: [main]" in content or "branches:\n    - main" in content, \
        "Workflow muss auf main-Branch triggern"


def test_workflow_has_threshold_checks():
    content = WORKFLOW_PATH.read_text()
    assert "halluzinations_rate" in content, "halluzinations_rate-Check fehlt im Workflow"
    assert "quellen_genauigkeit" in content, "quellen_genauigkeit-Check fehlt im Workflow"
    assert "vollstaendigkeits_score" in content, "vollstaendigkeits_score-Check fehlt im Workflow"


def test_workflow_fail_conditions():
    content = WORKFLOW_PATH.read_text()
    assert "0.1" in content, "Schwellenwert 0.1 fuer halluzinations_rate fehlt"
    assert "0.9" in content, "Schwellenwert 0.9 fuer quellen_genauigkeit fehlt"
    assert "0.7" in content, "Schwellenwert 0.7 fuer vollstaendigkeits_score fehlt"
    assert "sys.exit(1)" in content, "sys.exit(1) fuer Fail-Condition fehlt"


@pytest.mark.skipif(not METRICS_PATH.exists(), reason="metrics.py nicht vorhanden")
def test_metrics_thresholds_logic():
    from metrics import halluzinations_rate, quellen_genauigkeit, vollstaendigkeits_score
    passing = [{"verifiziert": True, "korrekte_quellen": ["§1 BGB"], "erwartete_paragraphen": ["§1 BGB"], "verweigert": False}]
    assert halluzinations_rate(passing) <= 0.1
    assert quellen_genauigkeit(passing) >= 0.9
    assert vollstaendigkeits_score(passing) >= 0.7
