import socket
import pytest
import psycopg2
from datetime import date


def pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_legal_chunks_hat_versioning_spalten():
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute(
        "SELECT column_name FROM information_schema.columns "
        "WHERE table_name='legal_chunks'"
    )
    cols = {r[0] for r in cur.fetchall()}
    conn.close()
    assert "valid_from" in cols, "Spalte valid_from fehlt in legal_chunks"
    assert "valid_until" in cols, "Spalte valid_until fehlt in legal_chunks"


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_mehrere_versionen_gleicher_paragraph():
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute(
        "SELECT COUNT(*) FROM legal_chunks "
        "WHERE title ILIKE '%§ 1%KSchG%' OR title ILIKE '%§1 KSchG%'"
    )
    count = cur.fetchone()[0]
    conn.close()
    assert count >= 2, (
        f"Erwartet ≥2 Versionen von §1 KSchG, gefunden: {count}. "
        "Chunk-Versionierung noch nicht implementiert."
    )


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_stichtagsabfrage_liefert_korrekte_version():
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    stichtag = date(2022, 1, 1)
    cur.execute(
        "SELECT id, valid_from, valid_until FROM legal_chunks "
        "WHERE valid_from <= %s AND (valid_until IS NULL OR valid_until > %s) "
        "LIMIT 1",
        (stichtag, stichtag),
    )
    row = cur.fetchone()
    conn.close()
    assert row is not None, (
        f"Stichtagsabfrage für {stichtag} liefert kein Ergebnis — "
        "valid_from/valid_until nicht befüllt oder Spalten fehlen"
    )


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_aktuelle_version_hat_offenes_valid_until():
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute(
        "SELECT COUNT(*) FROM legal_chunks WHERE valid_until IS NULL AND valid_from IS NOT NULL"
    )
    count = cur.fetchone()[0]
    conn.close()
    assert count > 0, "Keine aktuelle Version (valid_until=NULL) gefunden — Versionierung nicht implementiert"
