#!/usr/bin/env python3
"""
Akzeptanztest für Task #157: FTS-Fix Compound Stemming Bug
"""

import os
import sys
from pathlib import Path

import pytest


def test_acceptance_fts():
    """Testet ob FTS-Fix korrekt implementiert ist."""
    print("Running Acceptance Test for Task #157")

    # Import search_service direkt
    search_service_path = Path("/data/.openclaw/workspace-codex/projects/lexwolf/backend/services/search_service.py")
    assert search_service_path.exists(), "search_service.py existiert nicht"
    print("✅ search_service.py existiert")

    # Lese den Code und prüfe websearch_to_tsquery
    with open(search_service_path, "r") as f:
        source = f.read()
    assert "websearch_to_tsquery" in source, "search_service.py nutzt nicht websearch_to_tsquery"
    print("✅ search_service.py nutzt websearch_to_tsquery")

    assert "or_query" in source, "OR-Fallback ist nicht implementiert"
    print("✅ OR-Fallback ist implementiert")

    # Prüfe GIN-Index
    os.environ['DATABASE_URL'] = 'postgresql://postgres:postgres@host.docker.internal:5432/lexwolf'

    from sqlalchemy import create_engine, text

    engine = create_engine(os.environ['DATABASE_URL'])
    with engine.connect() as conn:
        result = conn.execute(text("""
            SELECT indexname 
            FROM pg_indexes 
            WHERE tablename = 'legal_chunks' AND indexname = 'idx_ts_vector';
        """)).fetchone()
        assert result is not None, "GIN-Index idx_ts_vector ist nicht vorhanden"
        print("✅ GIN-Index idx_ts_vector ist vorhanden")

    # Prüfe FTS-Test
    with engine.connect() as conn:
        result = conn.execute(text("""
            SELECT COUNT(*) 
            FROM legal_chunks 
            WHERE ts_vector @@ websearch_to_tsquery('german', 'Kündigungsschutz');
        """)).fetchone()
        assert result[0] > 0, "FTS-Test 'Kündigungsschutz' liefert 0 Ergebnisse"
        print(f"✅ FTS-Test 'Kündigungsschutz' liefert {result[0]} Ergebnisse (>0)")

    # Prüfe Sparse search - direkte DB-Abfrage
    with engine.connect() as conn:
        result = conn.execute(text("""
            SELECT COUNT(*) 
            FROM legal_chunks 
            WHERE ts_vector @@ websearch_to_tsquery('german', 'Kündigung Mitarbeiter');
        """)).fetchone()
        print(f"FTS-Test 'Kündigung Mitarbeiter': {result[0]} Ergebnisse")
    
    # Prüfe dass mindestens 5 Ergebnisse für "Kündigung Mitarbeiter" existieren
    with engine.connect() as conn:
        result = conn.execute(text("""
            SELECT COUNT(*) 
            FROM legal_chunks 
            WHERE ts_vector @@ websearch_to_tsquery('german', 'Kündigung Mitarbeiter');
        """)).fetchone()
        assert result[0] >= 5, f"Sparse search 'Kündigung Mitarbeiter' liefert nur {result[0]} Ergebnisse (erwartet >=5)"
        print(f"✅ Sparse search 'Kündigung Mitarbeiter' liefert {result[0]} Ergebnisse (>=5)")

    print("\n" + "="*60)
    print("✅ ACCEPTANCE TEST PASSED")
    print("="*60)


def test_acceptance_fts_pytest():
    """pytest wrapper für acceptance test."""
    test_acceptance_fts()


if __name__ == "__main__":
    test_acceptance_fts()
