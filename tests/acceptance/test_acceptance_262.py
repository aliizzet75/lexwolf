import socket
import requests
import pytest

BOARD_API = "http://localhost:8082"
BACKEND_URL = "http://localhost:8000"


def board_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 8082))
    s.close()
    return r == 0


def _get_task_262():
    resp = requests.get(f"{BOARD_API}/api/tasks", params={"projekt_id": 1}, timeout=5)
    resp.raise_for_status()
    tasks = {t["id"]: t for t in resp.json()}
    assert 262 in tasks, "T#262 nicht im Board gefunden"
    return tasks[262]


@pytest.mark.skipif(not board_ok(), reason="Board API nicht erreichbar auf port 8082")
def test_t262_hat_screenshot_attachment():
    task = _get_task_262()
    assert task.get("attachment_id") or task.get("attachment_url"), (
        "T#262 hat kein Screenshot-Attachment verknüpft — "
        "End-to-end Verifikation des Screenshot-Uploads nicht erfüllt"
    )


@pytest.mark.skipif(not board_ok(), reason="Board API nicht erreichbar auf port 8082")
def test_t262_attachment_ist_ueber_backend_abrufbar():
    task = _get_task_262()
    url = task.get("attachment_url")
    assert url, "T#262 hat keine attachment_url"
    response = requests.get(url, timeout=10)
    assert response.status_code == 200, (
        f"Screenshot-Attachment von T#262 ist nicht abrufbar: {response.status_code}"
    )
    assert response.content[:8] == b"\x89PNG\r\n\x1a\n", "Attachment ist kein gueltiges PNG"
