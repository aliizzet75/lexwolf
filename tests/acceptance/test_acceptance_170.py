import socket
import pytest


def _pg_ok():
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


SKIP = pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")


@SKIP
def test_dod_1601_mindestens_3_chunks():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags='bgb' AND title ILIKE '%1601%'")
    count = cur.fetchone()[0]
    conn.close()
    assert count >= 3, f"§1601-Chunks: {count} (erwartet >= 3)"


@SKIP
def test_dod_535_mindestens_8_chunks():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags='bgb' AND title ILIKE '%535%'")
    count = cur.fetchone()[0]
    conn.close()
    assert count >= 8, f"§535-Chunks: {count} (erwartet >= 8)"


@SKIP
def test_dod_kein_bgb_chunk_ueber_2000_zeichen():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags='bgb' AND LENGTH(text) > 2000")
    count = cur.fetchone()[0]
    conn.close()
    assert count == 0, f"{count} BGB-Chunks sind > 2000 Zeichen"


@SKIP
def test_dod_vector_nicht_null_fuer_alle_bgb_chunks():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags='bgb' AND vector IS NULL")
    count = cur.fetchone()[0]
    conn.close()
    assert count == 0, f"{count} BGB-Chunks haben vector=NULL"
