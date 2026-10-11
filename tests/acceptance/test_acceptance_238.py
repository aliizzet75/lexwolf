import socket
import requests
import pytest

BACKEND_URL = "http://localhost:8000"
PNG_BYTES = (
    b"\x89PNG\r\n\x1a\n\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01"
    b"\x08\x02\x00\x00\x00\x90wS\xde\x00\x00\x00\x0cIDATx\x9cc\xf8\xcf\xc0"
    b"\x00\x00\x03\x01\x01\x00\x18\xdd\x8d\xb0\x00\x00\x00\x00IEND\xaeB`\x82"
)


def backend_ok() -> bool:
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 8000))
    s.close()
    return r == 0


@pytest.mark.skipif(not backend_ok(), reason="LexWolf-Backend nicht erreichbar auf Port 8000")
def test_bug_feedback_confirm_akzeptiert_screenshot_upload():
    files = {"screenshot": ("bug.png", PNG_BYTES, "image/png")}
    data = {
        "summary": '{"titel": "Dropdown leer", "beschreibung": "Mandanten-Dropdown zeigt keine Eintraege"}',
        "typ": "bug",
        "bug": '{"repro_steps": "1. Dialog oeffnen 2. Dropdown klicken", "client_version": "1.2.3", "affected_ui_location": "Mandanten-Liste"}',
    }
    response = requests.post(f"{BACKEND_URL}/feature-feedback/confirm", data=data, files=files, timeout=10)
    assert response.status_code == 200, f"Screenshot-Upload beim Bug-Report schlägt fehl: {response.status_code} {response.text}"
    body = response.json()
    assert "attachment_url" in body or "attachment_id" in body, "Antwort verknüpft keinen Screenshot-Anhang mit dem Board-Eintrag"
