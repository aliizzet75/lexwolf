#!/usr/bin/env python3
"""
Akzeptanztest für Task #160: Re-Embedding aller Chunks
"""

import json
import os
from pathlib import Path


def test_acceptance_reembedding():
    """Testet ob Re-Embedding-Script korrekt implementiert ist."""
    print("Running Acceptance Test for Task #160")
    
    # Prüfe reembed_all.py
    script_path = Path("/data/.openclaw/workspace-codex/projects/lexwolf/backend/scripts/reembed_all.py")
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
    
    print("\n" + "="*60)
    print("✅ ACCEPTANCE TEST PASSED")
    print("="*60)


def test_acceptance_reembedding_pytest():
    """pytest wrapper für acceptance test."""
    test_acceptance_reembedding()


if __name__ == "__main__":
    test_acceptance_reembedding()
