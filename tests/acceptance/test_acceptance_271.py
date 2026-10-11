import socket

import pytest
import requests

BOARD_API = "http://localhost:8082"
T257_ID = 257


def _board_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 8082))
    s.close()
    return r == 0


def _get_task_257():
    tasks = requests.get(f"{BOARD_API}/api/tasks", params={"projekt_id": 1}, timeout=5).json()
    for t in tasks:
        if t.get("id") == T257_ID:
            return t
    raise AssertionError(f"Task #{T257_ID} nicht im Board gefunden")


@pytest.mark.skipif(not _board_ok(), reason="Board API nicht erreichbar auf port 8082")
def test_t257_hat_konkrete_feature_beschreibung():
    task = _get_task_257()
    notizen = task.get("notizen", "")
    kontext = notizen.split("AUFGABE:")[0].replace("KONTEXT:", "").strip()

    assert kontext.lower() != "test", (
        "T#257 KONTEXT enthaelt weiterhin nur den Platzhalter 'Test' - "
        "Feature-Beschreibung wurde noch nicht nachgeliefert"
    )
    assert len(kontext) > 30, (
        f"T#257 KONTEXT ist zu kurz fuer eine umsetzbare Feature-Beschreibung: {kontext!r}"
    )


@pytest.mark.skipif(not _board_ok(), reason="Board API nicht erreichbar auf port 8082")
def test_t257_hat_screenshot_oder_verweist_nicht_mehr_darauf():
    task = _get_task_257()
    notizen = task.get("notizen", "")
    if "oben beschriebenen Feature" in notizen:
        assert task.get("attachment_url") or task.get("attachment_id"), (
            "AUFGABE verweist auf ein 'oben beschriebenes' Feature, aber es fehlt "
            "weiterhin ein Screenshot/Attachment"
        )
