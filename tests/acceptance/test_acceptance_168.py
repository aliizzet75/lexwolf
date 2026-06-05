import socket
import pytest


def _pg_ok():
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_dod_weniger_als_1000_ohne_legal_field():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE legal_field IS NULL OR legal_field = ''")
    count = cur.fetchone()[0]
    conn.close()
    assert count < 1000, f"Noch {count} Chunks ohne legal_field (DoD: < 1000)"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_dod_kschg_ist_arbeitsrecht():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags = 'kschg' AND (legal_field IS NULL OR legal_field != 'Arbeitsrecht')")
    falsch = cur.fetchone()[0]
    conn.close()
    assert falsch == 0, f"{falsch} kschg-Chunks haben NICHT legal_field='Arbeitsrecht'"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_dod_bgb_paragraph_535_ist_mietrecht():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE tags = 'bgb' AND title LIKE '%§ 535%' AND legal_field != 'Mietrecht'")
    falsch = cur.fetchone()[0]
    conn.close()
    assert falsch == 0, f"{falsch} BGB-§535-Chunks haben NICHT legal_field='Mietrecht'"
