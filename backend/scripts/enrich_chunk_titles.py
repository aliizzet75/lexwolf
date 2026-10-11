#!/usr/bin/env python3
"""
Chunk-Title-Anreicherung: Gesetzeskürzel in Title einfügen.

Wandelt z.B. "§ 1 Sozial ungerechtfertigte Kündigungen" (tags=kschg)
zu "KSCHG - § 1 Sozial ungerechtfertigte Kündigungen" um,
damit FTS bei Suche nach "KSchG" auch Treffer liefert.
"""

import psycopg2
import sys


DATABASE_URL = "postgresql://postgres:postgres@localhost:5432/lexwolf"


def enrich_titles(conn):
    cur = conn.cursor()

    cur.execute("""
        UPDATE legal_chunks
        SET title = UPPER(tags) || ' - ' || title
        WHERE tags IS NOT NULL AND tags != ''
          AND title NOT ILIKE '%' || tags || '%'
    """)
    updated_titles = cur.rowcount
    print(f"Titles angereichert: {updated_titles}")

    cur.execute("""
        UPDATE legal_chunks
        SET ts_vector = to_tsvector('german', COALESCE(title,'') || ' ' || COALESCE(text,''))
    """)
    updated_vectors = cur.rowcount
    print(f"ts_vectors neu berechnet: {updated_vectors}")

    conn.commit()
    cur.close()
    return updated_titles


def verify(conn):
    cur = conn.cursor()

    cur.execute("SELECT title FROM legal_chunks WHERE tags='kschg' LIMIT 1")
    row = cur.fetchone()
    if row and "KSCHG" in row[0].upper():
        print(f"OK - KSCHG in title: {row[0]}")
    else:
        print(f"FAIL - Title ohne KSCHG: {row}")

    cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE ts_vector @@ plainto_tsquery('german', 'KSchG')")
    count = cur.fetchone()[0]
    print(f"FTS-Treffer für 'KSchG': {count}")

    cur.close()


if __name__ == "__main__":
    try:
        conn = psycopg2.connect(DATABASE_URL)
    except Exception as e:
        print(f"DB-Verbindungsfehler: {e}", file=sys.stderr)
        sys.exit(1)

    enrich_titles(conn)
    verify(conn)
    conn.close()
    print("DONE")
