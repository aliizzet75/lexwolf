import socket
import requests
import pytest

BOARD_API = "http://localhost:8082"


def board_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 8082))
    s.close()
    return r == 0


@pytest.mark.skipif(not board_ok(), reason="Board API nicht erreichbar auf localhost:8082")
def test_t247_hat_konkrete_fehlerbeschreibung():
    tasks = requests.get(f"{BOARD_API}/api/tasks", params={"projekt_id": 1}, timeout=5).json()
    t247 = next(t for t in tasks if t["id"] == 247)
    notizen = t247["notizen"].lower()

    hat_erwartet_tatsaechlich = "erwart" in notizen and "tatsächlich" in notizen
    hat_screenshot_ersatz = t247["attachment_id"] is not None

    assert hat_erwartet_tatsaechlich or hat_screenshot_ersatz, (
        "T#247 enthaelt weiterhin keine konkrete Fehlerbeschreibung "
        "(erwartetes vs. tatsaechliches Verhalten) und keinen Screenshot"
    )
    assert "1. dialog oeffnen 2. dropdown klicken" not in notizen.replace("\n", " "), (
        "Reproduktionsschritte wurden nicht um das eigentliche Fehlverhalten ergaenzt"
    )
