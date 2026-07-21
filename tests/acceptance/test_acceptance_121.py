import sys
import os
import pytest
import socket

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', 'backend', 'scripts'))

from batch_classify_legal_field import classify_chunk, classify_bgb


def pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


# --- Unit: regelbasierte Klassifizierung ---

def test_classify_arbeitsrecht_kschg():
    assert classify_chunk("kschg", None) == "Arbeitsrecht"


def test_classify_mietrecht_bgb_paragraph():
    assert classify_bgb("§ 556 Vereinbarungen über Betriebskosten") == "Mietrecht"
    assert classify_bgb("§ 535 Inhalt und Hauptpflichten des Mietvertrags") == "Mietrecht"


def test_classify_familienrecht_bgb_paragraph():
    assert classify_bgb("§ 1353 Eheliche Lebensgemeinschaft") == "Familienrecht"
    assert classify_bgb("§ 1500 Erbverzicht") == "Familienrecht"


def test_classify_arbeitsrecht_betrvg():
    assert classify_chunk("betrvg", None) == "Arbeitsrecht"


def test_classify_fallback_unknown():
    result = classify_chunk("unbekanntes_gesetz_xyz", "irgendein Titel")
    assert isinstance(result, str) and len(result) > 0


# --- Integration: DB-Spalte legal_field vorhanden und befüllt ---

@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_db_legal_field_column_exists():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE legal_field IS NOT NULL AND legal_field != ''")
    count = cur.fetchone()[0]
    conn.close()
    assert count > 0, "Keine Chunks mit legal_field in DB"


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_db_arbeitsrecht_chunks_exist():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE legal_field = 'Arbeitsrecht'")
    count = cur.fetchone()[0]
    conn.close()
    assert count > 0, "Keine Arbeitsrecht-Chunks in DB"
