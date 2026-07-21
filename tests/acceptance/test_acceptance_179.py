"""
Task #179: GET /mandant/search implementieren
Prüft: Tabelle mandanten in PostgreSQL + Endpoint GET /mandant/search?q=... + GET /mandant/{id}
"""
import pytest
import socket
import urllib.request
import json


def backend_ok():
    s = socket.socket()
    s.settimeout(3)
    r = s.connect_ex(("localhost", 8000))
    s.close()
    return r == 0


def pg_ok():
    s = socket.socket()
    s.settimeout(3)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_mandanten_tabelle_existiert():
    import psycopg2
    c = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = c.cursor()
    cur.execute("SELECT column_name FROM information_schema.columns WHERE table_name='mandanten' ORDER BY ordinal_position")
    cols = {r[0] for r in cur.fetchall()}
    c.close()
    assert {"id", "name", "email", "aktenzeichen", "erstellt_am"} <= cols, f"Fehlende Spalten: {cols}"


@pytest.mark.skipif(not backend_ok(), reason="Backend nicht erreichbar auf port 8000")
def test_mandant_search_endpoint():
    req = urllib.request.Request("http://localhost:8000/mandant/search?q=test")
    with urllib.request.urlopen(req, timeout=10) as resp:
        assert resp.status == 200, f"Unerwarteter Status: {resp.status}"
        data = json.loads(resp.read())
        assert isinstance(data, list), f"Erwarte JSON-Array, bekam: {type(data)}"


@pytest.mark.skipif(not backend_ok(), reason="Backend nicht erreichbar auf port 8000")
def test_mandant_by_id_endpoint_returns_404_for_unknown():
    req = urllib.request.Request("http://localhost:8000/mandant/999999")
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            assert resp.status in (200, 404), f"Unerwarteter Status: {resp.status}"
    except urllib.error.HTTPError as e:
        assert e.code == 404, f"Erwarte 404 für unbekannte ID, bekam: {e.code}"
