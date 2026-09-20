import socket
import pytest
import requests

BASE = "http://localhost:8000"


def _port_ok(port):
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", port)); s.close(); return r == 0


backend_ok = _port_ok(8000)


@pytest.mark.skipif(not backend_ok, reason="LexWolf-Backend nicht erreichbar auf port 8000")
def test_style_progress_endpoint_exists():
    r = requests.get(f"{BASE}/api/style/progress", timeout=10)
    assert r.status_code == 200, f"Unexpected status {r.status_code}: {r.text[:200]}"


@pytest.mark.skipif(not backend_ok, reason="LexWolf-Backend nicht erreichbar auf port 8000")
def test_style_progress_response_structure():
    r = requests.get(f"{BASE}/api/style/progress", timeout=10)
    assert r.status_code == 200
    data = r.json()
    assert "lern_fortschritt" in data, f"Fehlendes Feld 'lern_fortschritt': {data}"
    assert "bekannte_aspekte" in data, f"Fehlendes Feld 'bekannte_aspekte': {data}"
    assert "samples_gesammelt" in data, f"Fehlendes Feld 'samples_gesammelt': {data}"
    assert isinstance(data["lern_fortschritt"], (int, float)), "lern_fortschritt muss eine Zahl sein"
    assert 0.0 <= data["lern_fortschritt"] <= 1.0, "lern_fortschritt muss zwischen 0.0 und 1.0 liegen"
    assert isinstance(data["bekannte_aspekte"], list), "bekannte_aspekte muss eine Liste sein"
    assert isinstance(data["samples_gesammelt"], int), "samples_gesammelt muss eine ganze Zahl sein"
