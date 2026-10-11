#!/usr/bin/env python3
"""
Update legal_field for all chunks based on their tags.
This fixes the issue where 99.9% of chunks have empty legal_field.
"""

import os
import sys
import psycopg2

DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://postgres:postgres@host.docker.internal:5432/lexwolf")

# Map tags to legal fields
TAG_TO_FIELD = {
    'bgb': 'Zivilrecht',
    'kschg': 'Arbeitsrecht',
    'stgb': 'Strafrecht',
    'zpo': 'Zivilprozessrecht',
    'stpo': 'Strafprozessrecht',
    'gg': 'Verfassungsrecht',
    'hgb': 'Handelsrecht',
    'gmbhg': 'Gesellschaftsrecht',
    'aktg': 'Aktienrecht',
    'urhg': 'Urheberrecht',
    'patg': 'Patentrecht',
    'markg': 'Markenrecht',
    'arbgg': 'Arbeitsrecht',
    'sgb_1': 'Sozialrecht',
    'sgb_2': 'Sozialrecht',
    'sgb_3': 'Sozialrecht',
    'sgb_4': 'Sozialrecht',
    'sgb_5': 'Sozialrecht',
    'sgb_6': 'Sozialrecht',
    'sgb_7': 'Sozialrecht',
    'sgb_8': 'Sozialrecht',
    'sgb_9': 'Sozialrecht',
    'sgb_10': 'Sozialrecht',
    'sgb_11': 'Sozialrecht',
    'sgb_12': 'Sozialrecht',
    'sgb_13': 'Sozialrecht',
    'sgb_14': 'Sozialrecht',
    'sgb_15': 'Sozialrecht',
    'sgb_18': 'Sozialrecht',
    'sgb_20': 'Sozialrecht',
    'sgb_22': 'Sozialrecht',
    'sgb_23': 'Sozialrecht',
    'sgb_24': 'Sozialrecht',
    'sgb_25': 'Sozialrecht',
    'sgb_26': 'Sozialrecht',
    'sgb_27': 'Sozialrecht',
    'sgb_28': 'Sozialrecht',
    'sgb_29': 'Sozialrecht',
    'sgb_30': 'Sozialrecht',
    'sgb_31': 'Sozialrecht',
    'sgb_32': 'Sozialrecht',
    'sgb_33': 'Sozialrecht',
    'sgb_34': 'Sozialrecht',
    'sgb_35': 'Sozialrecht',
    'sgb_36': 'Sozialrecht',
    'sgb_37': 'Sozialrecht',
    'sgb_38': 'Sozialrecht',
    'sgb_39': 'Sozialrecht',
    'sgb_40': 'Sozialrecht',
    'sgb_41': 'Sozialrecht',
    'sgb_42': 'Sozialrecht',
    'sgb_43': 'Sozialrecht',
    'sgb_44': 'Sozialrecht',
    'sgb_45': 'Sozialrecht',
    'sgb_46': 'Sozialrecht',
    'sgb_47': 'Sozialrecht',
    'sgb_48': 'Sozialrecht',
    'sgb_49': 'Sozialrecht',
    'sgb_50': 'Sozialrecht',
    'vg': 'Verwaltungsrecht',
    'vfg': 'Verwaltungsrecht',
    'burlg': 'Arbeitsrecht',
    'tzfbg': 'Arbeitsrecht',
    'ao': 'Verwaltungsrecht',
    'stPO': 'Strafprozessrecht',
    'kStG': 'Steuerrecht',
    'eStG': 'Steuerrecht',
    'aG': 'Arbeitsgerichtsgesetz',
    'gG': 'Gemeinnützigkeitsrecht',
    'gGK': 'Gemeinnützigkeitsrecht',
    'SGB I': 'Sozialrecht',
    'SGB II': 'Sozialrecht',
    'SGB III': 'Sozialrecht',
    'SGB IV': 'Sozialrecht',
    'SGB V': 'Sozialrecht',
    'SGB VIII': 'Sozialrecht',
    'SGB IX': 'Sozialrecht',
    'SGB X': 'Sozialrecht',
    'SGB XI': 'Sozialrecht',
    'SGB XII': 'Sozialrecht',
    'SGB XIII': 'Sozialrecht',
    'SGB XIV': 'Sozialrecht',
}


def get_db_connection():
    """Get database connection."""
    return psycopg2.connect(DATABASE_URL)


def update_legal_field_for_tag(tag: str, legal_field: str) -> int:
    """Update legal_field for chunks with specific tag (tags stored as text)."""
    conn = get_db_connection()
    try:
        cur = conn.cursor()
        # Tags are stored as text (e.g., 'bgb' or 'bgb,sgb_5')
        # Use LIKE or strpos to find the tag
        cur.execute("""
            UPDATE legal_chunks 
            SET legal_field = %s
            WHERE tags IS NOT NULL 
            AND (tags = %s OR tags LIKE %s OR tags LIKE %s OR tags LIKE %s)
            AND (legal_field IS NULL OR legal_field = '')
        """, (legal_field, tag, f'{tag},%', f'%,{tag},%', f'%,{tag}'))
        updated = cur.rowcount
        conn.commit()
        cur.close()
        return updated
    except Exception as e:
        print(f"Error updating chunks with tag '{tag}': {e}")
        conn.rollback()
        return 0
    finally:
        conn.close()


def get_tag_stats():
    """Get statistics about tags in the database."""
    conn = get_db_connection()
    try:
        cur = conn.cursor()
        cur.execute("""
            SELECT tags, COUNT(*) as cnt 
            FROM legal_chunks 
            WHERE tags IS NOT NULL 
            GROUP BY tags 
            ORDER BY cnt DESC 
            LIMIT 30
        """)
        results = cur.fetchall()
        cur.close()
        return results
    except Exception as e:
        print(f"Error getting tag stats: {e}")
        return []
    finally:
        conn.close()


def get_legal_field_stats():
    """Get statistics about legal_field values."""
    conn = get_db_connection()
    try:
        cur = conn.cursor()
        cur.execute("""
            SELECT legal_field, COUNT(*) as cnt 
            FROM legal_chunks 
            GROUP BY legal_field 
            ORDER BY cnt DESC
        """)
        results = cur.fetchall()
        cur.close()
        return results
    except Exception as e:
        print(f"Error getting legal_field stats: {e}")
        return []
    finally:
        conn.close()


def main():
    print("="*60)
    print("Update legal_field for all chunks based on tags")
    print("="*60)
    
    # Show current stats
    print("\nCurrent legal_field distribution:")
    for field, count in get_legal_field_stats():
        print(f"  {field or '(empty)'}: {count}")
    
    print("\nCurrent tag distribution:")
    for tag, count in get_tag_stats():
        print(f"  {tag}: {count}")
    
    # Update chunks
    total_updated = 0
    for tag, field in TAG_TO_FIELD.items():
        if field == 'Arbeitsrecht' and tag not in ['kschg', 'arbgg', 'burlg', 'tzfbg']:
            continue  # Skip generic work tags
        updated = update_legal_field_for_tag(tag, field)
        if updated > 0:
            print(f"  {tag} -> {field}: {updated} chunks")
            total_updated += updated
    
    print(f"\nTotal updated: {total_updated}")
    
    # Show final stats
    print("\nFinal legal_field distribution:")
    for field, count in get_legal_field_stats():
        print(f"  {field or '(empty)'}: {count}")


if __name__ == "__main__":
    main()
