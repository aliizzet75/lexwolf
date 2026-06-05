import socket
import sys
import pytest
from unittest.mock import patch
import requests


def _board_ok():
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", 8082)); s.close(); return r == 0


sys.path.insert(0, "/home/claudeuser/aligator")
import aligator


def test_api_retry_on_connection_error():
    """_api() muss bei ConnectionError 3x wiederholen, dann Exception werfen."""
    call_count = 0

    def fake_request(*args, **kwargs):
        nonlocal call_count
        call_count += 1
        raise requests.exceptions.ConnectionError("Connection refused")

    # time.sleep wird global gepacht (aligator importiert es nach der Implementierung)
    with patch("aligator.requests.request", side_effect=fake_request), \
         patch("time.sleep"):
        with pytest.raises(requests.exceptions.ConnectionError):
            aligator._api("GET", "/api/test")

    assert call_count == 3, f"Erwartet 3 Versuche, tatsächlich: {call_count}"


@pytest.mark.skipif(not _board_ok(), reason="Board-API nicht erreichbar auf port 8082")
def test_api_real_connection():
    """_api() erreicht echte Board-API auf localhost:8082."""
    result = aligator._api("GET", "/api/projekte")
    assert isinstance(result, list)
