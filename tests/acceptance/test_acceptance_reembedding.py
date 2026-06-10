#!/usr/bin/env python3
"""
Akzeptanztest für Task #160: Re-Embedding aller Chunks
"""

import json
import os
import socket
import sys
from pathlib import Path

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf.disabled"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf.disabled"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
sys.path.insert(0, os.path.join(_ROOT, "backend"))


def _pg_ok():
    s = socket.socket()
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


def test_acceptance_reembedding():
    """Testet ob Re-Embedding-Script korrekt implementiert ist."""
    print("Running Acceptance Test for Task #160")
    
    # Prüfe reembed_all.py
    script_path = Path(os.path.join(_ROOT, "backend/scripts/reembed_all.py"))
    assert script_path.exists(), "reembed_all.py existiert nicht"
    print("✅ reembed_all.py existiert")
    
    # Lade Script-Inhalt
    with open(script_path, "r") as f:
        source = f.read()
    
    # Test 1: EMBEDDING_MODEL env var Konfiguration
    assert "EMBEDDING_MODEL" in source, "EMBEDDING_MODEL env var nicht verwendet"
    print("✅ EMBEDDING_MODEL env var Konfiguration implementiert")
    
    # Test 2: Batch-Verarbeitung (256)
    assert "BATCH_SIZE = 256" in source, "Batch-Größe nicht auf 256 gesetzt"
    print("✅ Batch-Größe 256 konfiguriert")
    
    # Test 3: Update-Logik für legal_chunks
    assert "UPDATE legal_chunks" in source, "UPDATE legal_chunks fehlt"
    assert "SET vector = " in source, "SET vector = fehlt"
    assert "WHERE id = " in source, "WHERE id = fehlt"
    print("✅ Update-Logik für legal_chunks implementiert")
    
    # Test 4: Fortschritts-Logging
    assert "logger.info" in source or "print" in source, "Fortschritts-Logging fehlt"
    print("✅ Fortschritts-Logging implementiert")
    
    # Test 5: Fehlerbehandlung (weitermachen bei Fehler)
    assert "except Exception" in source, "Fehlerbehandlung fehlt"
    print("✅ Fehlerbehandlung implementiert (weitermachen bei Fehler)")
    
    # Test 6: Wiederaufnahme (start_id parameter)
    assert "start_id" in source, "Wiederaufnahme-Logik fehlt (start_id)"
    print("✅ Wiederaufnahme-Logik implementiert (start_id)")
    
    # Test 7: Mini-Test nach Abschluss
    assert "mini_test" in source.lower(), "Mini-Test nach Abschluss fehlt"
    print("✅ Mini-Test nach Abschluss implementiert")
    
    # Test 8: Batch-Embedding-Funktion
    assert "compute_embedding" in source or "batch" in source.lower(), "Batch-Verarbeitung fehlt"
    print("✅ Batch-Verarbeitung von Embeddings implementiert")

    # Test 9: embedding_model + embedding_timestamp werden gesetzt
    assert "embedding_model" in source, "embedding_model wird nicht in UPDATE gesetzt"
    assert "embedding_timestamp" in source, "embedding_timestamp wird nicht in UPDATE gesetzt"
    print("✅ embedding_model/embedding_timestamp im UPDATE gesetzt")

    # Test 10: mini_test_reembedding ist echt (nutzt Vektorsuche, nicht Keyword-Hack)
    assert "_chunk_matches_paragraph" in source, (
        "mini_test_reembedding() ist noch die Fake-Implementierung — "
        "_chunk_matches_paragraph fehlt"
    )
    assert "kschg" not in source.split("def mini_test_reembedding")[1].split("def ")[0], (
        "mini_test_reembedding() enthält noch den Fake-Keyword-Check 'kschg'"
    )
    print("✅ mini_test_reembedding() nutzt echte Vektorsuche (_chunk_matches_paragraph)")

    print("\n" + "="*60)
    print("✅ ACCEPTANCE TEST PASSED")
    print("="*60)


import pytest

@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_db_embedding_columns_exist():
    """Prüft ob embedding_model + embedding_timestamp Spalten in legal_chunks existieren."""
    import psycopg2
    PG_DSN = "postgresql://postgres:postgres@localhost:5432/lexwolf"
    conn = psycopg2.connect(PG_DSN)
    cur = conn.cursor()
    cur.execute("""
        SELECT column_name FROM information_schema.columns
        WHERE table_name = 'legal_chunks'
          AND column_name IN ('embedding_model', 'embedding_timestamp')
    """)
    cols = {row[0] for row in cur.fetchall()}
    conn.close()
    assert "embedding_model" in cols, "Spalte embedding_model fehlt in legal_chunks"
    assert "embedding_timestamp" in cols, "Spalte embedding_timestamp fehlt in legal_chunks"
    print("✅ DB-Spalten embedding_model + embedding_timestamp vorhanden")


def test_sqlalchemy_model_has_embedding_columns():
    """Prüft ob models.py die neuen Spalten für LegalChunk definiert."""
    models_path = Path(os.path.join(_ROOT, "backend/models.py"))
    assert models_path.exists(), "models.py nicht gefunden"
    source = models_path.read_text()
    assert "embedding_model" in source, "LegalChunk.embedding_model fehlt in models.py"
    assert "embedding_timestamp" in source, "LegalChunk.embedding_timestamp fehlt in models.py"
    print("✅ models.py definiert embedding_model + embedding_timestamp")


def test_acceptance_reembedding_pytest():
    """pytest wrapper für acceptance test."""
    test_acceptance_reembedding()


if __name__ == "__main__":
    test_acceptance_reembedding()
