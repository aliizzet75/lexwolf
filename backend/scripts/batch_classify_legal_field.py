#!/usr/bin/env python3
"""
Batch-classify legal_field for existing chunks retroactively.
Processes ~84.958 chunks without legal_field in batches of 1000.
"""

import re
import sys
import psycopg2

DATABASE_URL = "postgresql://postgres:postgres@localhost:5432/lexwolf"

# Direct tag → legal field mappings
TAG_MAP = {
    # Arbeitsrecht
    'kschg': 'Arbeitsrecht',
    'arbgg': 'Arbeitsrecht',
    'betrvg': 'Arbeitsrecht',
    'tvg': 'Arbeitsrecht',
    'entgfg': 'Arbeitsrecht',
    'muschg': 'Arbeitsrecht',
    'beeg': 'Arbeitsrecht',
    'burlg': 'Arbeitsrecht',
    'tzbfg': 'Arbeitsrecht',
    # Familienrecht (standalone tag)
    'famfg': 'Familienrecht',
    # Strafrecht
    'stgb': 'Strafrecht',
    'stpo': 'Strafrecht',
    # Zivilprozessrecht
    'zpo': 'Zivilprozessrecht',
    'gvg': 'Zivilprozessrecht',
    # Handelsrecht
    'hgb': 'Handelsrecht',
    'aktg': 'Handelsrecht',
    'gmbhg': 'Handelsrecht',
    # Steuerrecht
    'ao_1977': 'Steuerrecht',
    'estg': 'Steuerrecht',
    'ustg': 'Steuerrecht',
    # Verfassungsrecht
    'gg': 'Verfassungsrecht',
}


def extract_bgb_paragraph(title: str) -> int | None:
    """Extract the § number from a BGB chunk title."""
    if not title:
        return None
    m = re.search(r'§\s*(\d+)', title)
    if m:
        return int(m.group(1))
    return None


def classify_bgb(title: str) -> str:
    """Classify a BGB chunk based on its paragraph number."""
    para = extract_bgb_paragraph(title)
    if para is None:
        return 'Allgemeines Zivilrecht'
    if 535 <= para <= 580:
        return 'Mietrecht'
    if 823 <= para <= 853:
        return 'Deliktsrecht'
    if 1297 <= para <= 1921:
        return 'Familienrecht'
    if 1922 <= para <= 2385:
        return 'Erbrecht'
    return 'Allgemeines Zivilrecht'


def classify_chunk(tags: str | None, title: str | None) -> str:
    """Return the legal_field for a chunk given its tags and title."""
    if not tags:
        return 'Allgemeines Zivilrecht'

    tag = tags.strip().lower()

    # Direct match
    if tag in TAG_MAP:
        return TAG_MAP[tag]

    # BGB subsection classification
    if tag == 'bgb':
        return classify_bgb(title or '')

    # SGB wildcard
    if tag.startswith('sgb'):
        return 'Sozialrecht'

    # Default
    return 'Allgemeines Zivilrecht'


def reclassify_bgb(conn):
    """Reclassify existing BGB chunks from generic 'Zivilrecht' to subfields."""
    cur = conn.cursor()
    cur.execute("SELECT id, title FROM legal_chunks WHERE tags = 'bgb'")
    rows = cur.fetchall()
    if not rows:
        return
    updates = [(classify_bgb(title), chunk_id) for chunk_id, title in rows]
    cur.executemany("UPDATE legal_chunks SET legal_field = %s WHERE id = %s", updates)
    conn.commit()
    print(f"BGB: {len(rows)} Chunks nach §-Bereich reklassifiziert.")


def run():
    conn = psycopg2.connect(DATABASE_URL)
    conn.autocommit = False

    try:
        cur = conn.cursor()

        # Reclassify BGB chunks by paragraph range first
        reclassify_bgb(conn)

        # Total count
        cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE legal_field IS NULL OR legal_field = ''")
        total = cur.fetchone()[0]
        print(f"Chunks ohne legal_field: {total}")

        if total == 0:
            print("Nichts zu tun.")
            return

        offset = 0
        processed = 0
        batch_size = 1000

        while True:
            cur.execute(
                "SELECT id, tags, title FROM legal_chunks WHERE legal_field IS NULL OR legal_field = '' "
                "ORDER BY id LIMIT %s",
                (batch_size,)
            )
            rows = cur.fetchall()
            if not rows:
                break

            updates = []
            for chunk_id, tags, title in rows:
                field = classify_chunk(tags, title)
                updates.append((field, chunk_id))

            cur.executemany(
                "UPDATE legal_chunks SET legal_field = %s WHERE id = %s",
                updates
            )
            conn.commit()

            processed += len(rows)
            print(f"  [{processed}/{total}] Batch {offset//batch_size + 1} fertig "
                  f"({len(rows)} Chunks klassifiziert)")

            offset += batch_size
            if len(rows) < batch_size:
                break

        # Final count
        cur.execute("SELECT COUNT(*) FROM legal_chunks WHERE legal_field IS NULL OR legal_field = ''")
        remaining = cur.fetchone()[0]
        print(f"\nFertig. Verbleibend ohne legal_field: {remaining}")

        # Distribution
        cur.execute("SELECT legal_field, COUNT(*) FROM legal_chunks GROUP BY legal_field ORDER BY COUNT(*) DESC")
        print("\nVerteilung:")
        for field, count in cur.fetchall():
            print(f"  {field or '(leer)'}: {count}")

    finally:
        conn.close()


if __name__ == '__main__':
    run()
