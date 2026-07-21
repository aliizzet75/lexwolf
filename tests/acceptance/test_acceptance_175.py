"""
T#175 — Desktop Chat-UI: /chat akzeptiert Multi-Turn-History und liefert suggested_action.
Testet das Backend-Protokoll; WPF-UI muss manuell geprüft werden.
"""
import socket
import pytest
import requests

_BACKEND = "http://localhost:8000"


def _backend_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 8000))
    s.close()
    return r == 0


@pytest.fixture(scope="module")
def backend():
    if not _backend_ok():
        pytest.skip("Backend nicht erreichbar auf port 8000")


def test_chat_single_turn(backend):
    resp = requests.post(f"{_BACKEND}/chat", json={
        "messages": [{"role": "user", "content": "Was ist eine fristlose Kündigung?"}]
    }, timeout=30)
    assert resp.status_code == 200
    data = resp.json()
    assert "content" in data and len(data["content"]) > 10
    assert "suggested_action" in data
    assert data["suggested_action"] in ("erstelle_dokument", "berechne_unterhalt", "frage")


def test_chat_multi_turn_history(backend):
    messages = [
        {"role": "user", "content": "Ich habe einen Streit mit meinem Arbeitgeber."},
        {"role": "assistant", "content": "Bitte schildern Sie den Sachverhalt genauer."},
        {"role": "user", "content": "Er hat mir mündlich gekündigt. Was sind meine Rechte?"},
    ]
    resp = requests.post(f"{_BACKEND}/chat", json={"messages": messages}, timeout=30)
    assert resp.status_code == 200
    data = resp.json()
    assert len(data["content"]) > 10


def test_chat_suggested_action_erstelle_dokument(backend):
    resp = requests.post(f"{_BACKEND}/chat", json={
        "messages": [{"role": "user", "content": "Erstelle mir ein Kündigungsschreiben."}]
    }, timeout=30)
    assert resp.status_code == 200
    assert resp.json()["suggested_action"] == "erstelle_dokument"


def test_chat_suggested_action_berechne_unterhalt(backend):
    resp = requests.post(f"{_BACKEND}/chat", json={
        "messages": [{"role": "user", "content": "Berechne den Unterhalt für mein Kind."}]
    }, timeout=30)
    assert resp.status_code == 200
    assert resp.json()["suggested_action"] == "berechne_unterhalt"
