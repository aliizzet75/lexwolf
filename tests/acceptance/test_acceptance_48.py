import pytest
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../backend"))


def test_grounded_system_prompt_exists():
    from services import react_engine
    assert hasattr(react_engine, "GROUNDED_SYSTEM_PROMPT"), \
        "GROUNDED_SYSTEM_PROMPT fehlt in react_engine.py"


def test_system_prompt_forbids_training_data():
    from services.react_engine import GROUNDED_SYSTEM_PROMPT
    prompt_lower = GROUNDED_SYSTEM_PROMPT.lower()
    assert any(kw in prompt_lower for kw in ["trainingsdaten", "trainings-daten", "eigenes wissen"]), \
        "Prompt enthält kein explizites Verbot von Trainingsdaten"


def test_system_prompt_requires_source_reference():
    from services.react_engine import GROUNDED_SYSTEM_PROMPT
    prompt_lower = GROUNDED_SYSTEM_PROMPT.lower()
    assert any(kw in prompt_lower for kw in ["ausschließlich", "nur", "quellen", "bereitgestellten"]), \
        "Prompt verlangt keine ausschließliche Verwendung bereitgestellter Quellen"


def test_system_prompt_handles_missing_info():
    from services.react_engine import GROUNDED_SYSTEM_PROMPT
    prompt_lower = GROUNDED_SYSTEM_PROMPT.lower()
    assert "keine informationen" in prompt_lower or "unzureichend" in prompt_lower, \
        "Prompt enthält keine Anweisung für fehlende Informationen"


def test_system_prompt_forbids_invented_laws():
    from services.react_engine import GROUNDED_SYSTEM_PROMPT
    prompt_lower = GROUNDED_SYSTEM_PROMPT.lower()
    assert any(kw in prompt_lower for kw in ["erfinde", "erfind", "gesetze", "paragraphen"]), \
        "Prompt enthält kein Verbot erfundener Gesetze/Paragraphen"
