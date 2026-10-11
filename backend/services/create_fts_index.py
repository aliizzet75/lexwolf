#!/usr/bin/env python3
"""Erstellt GIN-Index für ts_vector in legal_chunks"""

import os
os.environ['DATABASE_URL'] = 'postgresql://postgres:postgres@host.docker.internal:5432/lexwolf'

from sqlalchemy import create_engine, text

engine = create_engine(os.environ['DATABASE_URL'])
with engine.connect() as conn:
    # Prüfe ob Index existiert
    result = conn.execute(text("""
        SELECT indexname 
        FROM pg_indexes 
        WHERE tablename = 'legal_chunks' AND indexname = 'idx_ts_vector';
    """)).fetchone()
    print(f"Index idx_ts_vector existiert: {result is not None}")
    
    # Erstelle Index wenn nicht vorhanden
    if not result:
        print("Erstelle GIN-Index idx_ts_vector...")
        conn.execute(text("CREATE INDEX IF NOT EXISTS idx_ts_vector ON legal_chunks USING GIN(ts_vector);"))
        conn.commit()
        print("Index erstellt.")
    else:
        print("Index existiert bereits, überspringe Erstellung.")
    
    # Verifiziere Index
    result = conn.execute(text("""
        SELECT indexname, indexdef
        FROM pg_indexes
        WHERE tablename = 'legal_chunks' AND indexname = 'idx_ts_vector';
    """)).fetchone()
    print(f"\nVerifizierung: {result}")
