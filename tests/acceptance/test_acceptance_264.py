import pytest
import socket
import requests

BOARD_API = "http://localhost:8082"


def board_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 8082))
    s.close()
    return r == 0


def _get_task_247():
    resp = requests.get(f"{BOARD_API}/api/tasks", params={"projekt_id": 1}, timeout=5)
    resp.raise_for_status()
    tasks = {t["id"]: t for t in resp.json()}
    assert 247 in tasks, "T#247 nicht im Board gefunden"
    return tasks[247]


@pytest.mark.skipif(not board_ok(), reason="Board API nicht erreichbar auf port 8082")
def test_t247_hat_screenshot_oder_konkrete_beschreibung():
    task = _get_task_247()
    notizen = task.get("notizen", "")
    hat_screenshot = bool(task.get("attachment_id") or task.get("attachment_url"))
    hat_soll_ist = "Soll" in notizen and "Ist" in notizen
    assert hat_screenshot or hat_soll_ist, (
        "T#247 hat weder Screenshot/Attachment noch eine Soll/Ist-Beschreibung "
        "in den Notizen — Ersteller muss noch kontaktiert werden"
    )


@pytest.mark.skipif(not board_ok(), reason="Board API nicht erreichbar auf port 8082")
def test_t247_notizen_ist_nicht_mehr_platzhalter():
    task = _get_task_247()
    notizen = task.get("notizen", "")
    assert "Test ohne Anhang" not in notizen, (
        "T#247 enthält noch den Platzhaltertext 'Test ohne Anhang' — "
        "keine echte Fehlerbeschreibung nachgereicht"
    )
