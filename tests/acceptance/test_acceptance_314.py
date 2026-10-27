"""Task #314: confidence_score muss auf dem reinen LLM-Content berechnet
werden, bevor format_zitatpruefung(belege) angehängt wird.

Mockt ask_llm (keine echten API-Kosten) und pruefe_belege (fest geprüfter
Beleg, erzwingt einen nicht-leeren Zitatprüfungs-Block), um deterministisch
zu prüfen, mit welchem Text verify_generated_content aufgerufen wird.
"""
import os
import sys
import pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent.parent / "backend"))

import pytest
from fastapi.testclient import TestClient


@pytest.fixture
def client():
    from main import app
    return TestClient(app)


def test_confidence_score_wird_auf_unveraendertem_content_berechnet(client, monkeypatch):
    import api.chat as chat_module
    import api.akte as akte_module

    fake_answer = "Dies ist die fertige Antwort ohne weitere Anhänge."
    monkeypatch.setattr(chat_module, "ask_llm", lambda *a, **k: fake_answer)
    monkeypatch.setattr(akte_module, "pruefe_belege", lambda content, x: [
        {"art": "Norm", "status": "geprüft", "zitat": "§ 433 BGB"}
    ])

    gesehene_texte = []

    def spy_verify(content, context=None):
        gesehene_texte.append(content)
        return {"quality_score": 0.9}

    monkeypatch.setattr(chat_module._get_quality_service(), "verify_generated_content", spy_verify)

    resp = client.post("/chat", json={"messages": [{"role": "user", "content": "Frage?"}]})
    assert resp.status_code == 200
    data = resp.json()

    assert gesehene_texte == [fake_answer], (
        "verify_generated_content muss auf dem reinen LLM-Content aufgerufen werden, "
        f"wurde aber mit {gesehene_texte!r} aufgerufen"
    )
    assert "Zitatprüfung" in data["content"]
    assert "gefunden" in data["content"]
