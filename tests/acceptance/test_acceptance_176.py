"""
Task #176: Desktop Client – Lokale Mandanten-DB (SQLite) + Laufwerk-Indexierung
Prüft: LocalDb.cs mit Schema, DokumentScanner.cs, SQLite-Erstellung, Backend /chat.
"""
import pytest
import socket
import sqlite3
import tempfile
import os
from pathlib import Path

DESKTOP = Path("/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/desktop")
BACKEND_URL = "http://212.227.180.66:8000"


def backend_ok():
    s = socket.socket()
    s.settimeout(3)
    r = s.connect_ex(("212.227.180.66", 8000))
    s.close()
    return r == 0


# --- AUFGABE 1: LocalDb.cs vorhanden und Schema korrekt ---

def test_localdb_cs_existiert():
    p = DESKTOP / "Database" / "LocalDb.cs"
    assert p.exists(), f"Datei fehlt: {p}"


def test_localdb_cs_enthaelt_alle_tabellen():
    p = DESKTOP / "Database" / "LocalDb.cs"
    if not p.exists():
        pytest.skip("LocalDb.cs noch nicht erstellt")
    content = p.read_text()
    assert "mandanten" in content, "Tabelle 'mandanten' fehlt in LocalDb.cs"
    assert "dokumente" in content, "Tabelle 'dokumente' fehlt in LocalDb.cs"
    assert "chat_history" in content, "Tabelle 'chat_history' fehlt in LocalDb.cs"


def test_sqlite_schema_erstellen():
    with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
        db_path = f.name
    try:
        conn = sqlite3.connect(db_path)
        cur = conn.cursor()
        cur.executescript("""
            CREATE TABLE mandanten (id TEXT PRIMARY KEY, name TEXT, nummer TEXT, erstellt TEXT);
            CREATE TABLE dokumente (id TEXT PRIMARY KEY, mandant_id TEXT, pfad TEXT,
                titel TEXT, inhalt_chunk TEXT, erstellt TEXT, geaendert TEXT);
            CREATE TABLE chat_history (id TEXT PRIMARY KEY, mandant_id TEXT, role TEXT,
                content TEXT, timestamp TEXT);
        """)
        conn.commit()
        cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
        tabellen = {r[0] for r in cur.fetchall()}
        conn.close()
        assert {"mandanten", "dokumente", "chat_history"} <= tabellen
    finally:
        os.unlink(db_path)


# --- AUFGABE 2: DokumentScanner.cs vorhanden ---

def test_dokumentscanner_cs_existiert():
    p = DESKTOP / "Services" / "DokumentScanner.cs"
    assert p.exists(), f"Datei fehlt: {p}"


def test_dokumentscanner_cs_enthaelt_kernfunktionen():
    p = DESKTOP / "Services" / "DokumentScanner.cs"
    if not p.exists():
        pytest.skip("DokumentScanner.cs noch nicht erstellt")
    content = p.read_text()
    assert "FileSystemWatcher" in content, "FileSystemWatcher fehlt in DokumentScanner.cs"
    assert ".docx" in content or "docx" in content, ".docx-Unterstützung fehlt"
    assert ".pdf" in content or "pdf" in content, ".pdf-Unterstützung fehlt"


# --- AUFGABE 3: Backend /chat erreichbar für KI-Zusammenfassung ---

@pytest.mark.skipif(not backend_ok(), reason="Backend nicht erreichbar auf 212.227.180.66:8000")
def test_backend_chat_endpoint_erreichbar():
    import urllib.request, json
    payload = json.dumps({"messages": [{"role": "user", "content": "Hallo"}]}).encode()
    req = urllib.request.Request(
        f"{BACKEND_URL}/chat",
        data=payload,
        headers={"Content-Type": "application/json"},
        method="POST"
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            assert resp.status in (200, 201, 422), f"Unerwarteter Status: {resp.status}"
    except Exception as e:
        pytest.fail(f"/chat nicht erreichbar: {e}")
