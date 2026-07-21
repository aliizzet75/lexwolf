import socket
import pytest
import requests

BASE = "http://localhost:8000"


def _port_ok(port):
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", port)); s.close(); return r == 0


backend_ok = _port_ok(8000)
ollama_ok = _port_ok(11434)


@pytest.mark.skipif(not backend_ok, reason="LexWolf-Backend nicht erreichbar auf port 8000")
def test_chat_endpoint_exists():
    r = requests.post(f"{BASE}/chat", json={"messages": [{"role": "user", "content": "Was ist Kündigungsschutz?"}]}, timeout=60)
    assert r.status_code == 200, f"Unexpected status {r.status_code}: {r.text[:200]}"


@pytest.mark.skipif(not backend_ok, reason="LexWolf-Backend nicht erreichbar auf port 8000")
def test_chat_response_structure():
    payload = {"messages": [{"role": "user", "content": "Wie lange ist die Kündigungsfrist?"}]}
    r = requests.post(f"{BASE}/chat", json=payload, timeout=60)
    assert r.status_code == 200
    data = r.json()
    assert "content" in data, f"Fehlendes Feld 'content': {data}"
    assert "intent" in data, f"Fehlendes Feld 'intent': {data}"
    assert "suggested_action" in data, f"Fehlendes Feld 'suggested_action': {data}"
    assert data["intent"] in ("frage", "erstelle", "berechne"), f"Ungültiger intent: {data['intent']}"


@pytest.mark.skipif(not backend_ok, reason="LexWolf-Backend nicht erreichbar auf port 8000")
def test_chat_multi_turn_history():
    messages = [
        {"role": "user", "content": "Mein Mandant wurde fristlos entlassen."},
        {"role": "assistant", "content": "Was sind die genauen Umstände?"},
        {"role": "user", "content": "Er hat angeblich Betriebsgeheimnisse weitergegeben."},
    ]
    r = requests.post(f"{BASE}/chat", json={"messages": messages}, timeout=60)
    assert r.status_code == 200
    data = r.json()
    assert len(data.get("content", "")) > 10, "Antwort ist zu kurz"


@pytest.mark.skipif(not backend_ok, reason="LexWolf-Backend nicht erreichbar auf port 8000")
def test_chat_mandant_context():
    payload = {
        "messages": [{"role": "user", "content": "Welche Rechte hat mein Mandant?"}],
        "mandant_context": "Mandant: Arbeitnehmer, 45 Jahre, seit 10 Jahren angestellt, fristlos entlassen.",
    }
    r = requests.post(f"{BASE}/chat", json=payload, timeout=60)
    assert r.status_code == 200
    data = r.json()
    assert "content" in data and len(data["content"]) > 10
