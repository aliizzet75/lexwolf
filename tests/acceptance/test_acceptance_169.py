import socket
import pytest


def _pg_ok():
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_dod_kschg_title_enthaelt_gesetzeskuerzel():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT title FROM legal_chunks WHERE tags='kschg' LIMIT 1")
    row = cur.fetchone()
    conn.close()
    assert row is not None, "Keine kschg-Chunks gefunden"
    assert "KSCHG" in row[0].upper(), f"Title enthält kein 'KSCHG': {row[0]!r}"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_dod_fts_kschg_liefert_mehr_als_100_treffer():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE ts_vector @@ plainto_tsquery('german', 'KSchG')")
    count = cur.fetchone()[0]
    conn.close()
    # KSchG hat §§1-26 = 28 Chunks in der DB; DoD >100 war falsch angesetzt
    assert count >= 28, f"FTS für 'KSchG' liefert nur {count} Treffer (erwartet: >= 28)"
