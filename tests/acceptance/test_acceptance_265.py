import pytest
import socket
import requests

BOARD_URL = "http://localhost:8082"


def board_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 8082))
    s.close()
    return r == 0


@pytest.mark.skipif(not board_ok(), reason="Board-API nicht erreichbar auf port 8082")
def test_t247_hat_screenshot_oder_fehlerbeschreibung():
    resp = requests.get(f"{BOARD_URL}/api/tasks", params={"projekt_id": 1, "limit": 500})
    resp.raise_for_status()
    tasks = {t["id"]: t for t in resp.json()}
    assert 247 in tasks, "T#247 nicht im Board gefunden"
    t247 = tasks[247]

    hat_anhang = bool(t247.get("attachment_id") or t247.get("attachment_url"))

    notizen = (t247.get("notizen") or "").lower()
    hat_beschreibung = "erwartet" in notizen and (
        "tatsächlich" in notizen or "tatsaechlich" in notizen
    )

    assert hat_anhang or hat_beschreibung, (
        "T#247 enthaelt weiterhin weder Screenshot/Anhang noch eine "
        "erwartet-vs-tatsaechlich Fehlerbeschreibung"
    )
