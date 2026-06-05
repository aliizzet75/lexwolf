"""
Acceptance Tests für Task #71: Laufwerk-Scanner – Schriftsätze indexieren

DoD:
- Scan-Pfad in Settings konfigurierbar
- .docx, .pdf, .txt werden erkannt
- SQLite-Index wird angelegt
- Initialer Scan läuft im Hintergrund
"""
import importlib.util
import json
import sqlite3
import tempfile
import time
import threading
from pathlib import Path

import pytest

DESKTOP = Path('/data/.openclaw/workspace-codex/projects/lexwolf/desktop')
SCANNER_MODULE = DESKTOP / 'drive_scanner.py'


def _import_scanner():
    spec = importlib.util.spec_from_file_location("drive_scanner", SCANNER_MODULE)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# ── Datei-Existenz ──────────────────────────────────────────────────────────

def test_drive_scanner_datei_existiert():
    assert SCANNER_MODULE.exists(), f'drive_scanner.py fehlt in {DESKTOP}'


def test_modul_importierbar():
    mod = _import_scanner()
    assert mod is not None


# ── Settings ─────────────────────────────────────────────────────────────────

def test_settings_manager_existiert():
    mod = _import_scanner()
    assert hasattr(mod, 'SettingsManager'), 'SettingsManager fehlt'


def test_scan_path_konfigurierbar():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as tmp:
        settings_path = Path(tmp) / 'settings.json'
        mgr = mod.SettingsManager(settings_path)
        s = mod.ScanSettings(scan_path='/tmp/kanzlei-dokumente')
        mgr.save(s)
        loaded = mgr.load()
        assert loaded.scan_path == '/tmp/kanzlei-dokumente', \
            f'Scan-Pfad nicht gespeichert. Geladen: {loaded.scan_path}'


def test_settings_json_hat_scanner_sektion():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as tmp:
        settings_path = Path(tmp) / 'settings.json'
        mgr = mod.SettingsManager(settings_path)
        mgr.save(mod.ScanSettings(scan_path='/kanzlei'))
        data = json.loads(settings_path.read_text())
        assert 'scanner' in data, f'settings.json hat keine "scanner"-Sektion: {data}'
        assert 'scan_path' in data['scanner'], '"scan_path" fehlt in scanner-Sektion'


def test_leere_settings_geben_leeren_scan_path():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as tmp:
        settings_path = Path(tmp) / 'settings.json'
        mgr = mod.SettingsManager(settings_path)
        s = mgr.load()
        assert s.scan_path == ''


# ── Dateiformat-Erkennung ─────────────────────────────────────────────────────

def test_docx_wird_erkannt():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        Path(scan_dir, 'Mustermann_Klage.docx').write_bytes(b'PK\x03\x04')  # docx magic
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=Path(db_dir) / 'test.db')
        count = scanner.scan_sync()
        assert count >= 1, f'.docx wurde nicht erkannt (count={count})'


def test_pdf_wird_erkannt():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        Path(scan_dir, 'Mueller_Antrag.pdf').write_bytes(b'%PDF-1.4')
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=Path(db_dir) / 'test.db')
        count = scanner.scan_sync()
        assert count >= 1, f'.pdf wurde nicht erkannt (count={count})'


def test_txt_wird_erkannt():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        Path(scan_dir, 'Schreiben_Schmidt_2024.txt').write_text('Inhalt')
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=Path(db_dir) / 'test.db')
        count = scanner.scan_sync()
        assert count >= 1, f'.txt wurde nicht erkannt (count={count})'


def test_andere_formate_werden_ignoriert():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        Path(scan_dir, 'bild.png').write_bytes(b'\x89PNG')
        Path(scan_dir, 'tabelle.xlsx').write_bytes(b'PK\x03\x04')
        Path(scan_dir, 'Mustermann_Vertrag.docx').write_bytes(b'PK\x03\x04')
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=Path(db_dir) / 'test.db')
        count = scanner.scan_sync()
        assert count == 1, f'Nur .docx soll erkannt werden, gefunden: {count}'


# ── SQLite-Index ──────────────────────────────────────────────────────────────

def test_sqlite_index_wird_angelegt():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        db_path = Path(db_dir) / 'drive_index.db'
        Path(scan_dir, 'Huber_Scheidung.pdf').write_bytes(b'%PDF-1.4')
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=db_path)
        scanner.scan_sync()
        assert db_path.exists(), 'SQLite-Datenbank wurde nicht angelegt'


def test_sqlite_tabelle_schriftsaetze_existiert():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        db_path = Path(db_dir) / 'drive_index.db'
        Path(scan_dir, 'test.txt').write_text('x')
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=db_path)
        scanner.scan_sync()
        with sqlite3.connect(str(db_path)) as conn:
            tables = [r[0] for r in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")]
        assert 'schriftsaetze' in tables, f'Tabelle "schriftsaetze" fehlt. Vorhanden: {tables}'


def test_index_speichert_dateiname_pfad_datum():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        db_path = Path(db_dir) / 'drive_index.db'
        doc = Path(scan_dir, 'Richter_Beschwerde.txt')
        doc.write_text('Beschwerdeschrift')
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=db_path)
        scanner.scan_sync()
        entries = scanner.index.all_entries()
        assert len(entries) == 1, f'Erwartet 1 Eintrag, gefunden: {len(entries)}'
        e = entries[0]
        assert e.dateiname == 'Richter_Beschwerde.txt', f'dateiname falsch: {e.dateiname}'
        assert str(doc) in e.pfad, f'pfad falsch: {e.pfad}'
        assert e.datum, 'datum fehlt'


def test_mandant_aus_dateiname_extrahiert():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        db_path = Path(db_dir) / 'drive_index.db'
        Path(scan_dir, 'Mustermann_Klage.pdf').write_bytes(b'%PDF-1.4')
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=db_path)
        scanner.scan_sync()
        entries = scanner.index.all_entries()
        assert entries, 'Keine Einträge gefunden'
        assert entries[0].mandant == 'Mustermann', \
            f'Mandant nicht korrekt extrahiert: {entries[0].mandant}'


def test_mehrere_dateien_werden_alle_indexiert():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        db_path = Path(db_dir) / 'drive_index.db'
        Path(scan_dir, 'Mueller_Antrag.pdf').write_bytes(b'%PDF-1.4')
        Path(scan_dir, 'Schmidt_Vertrag.docx').write_bytes(b'PK\x03\x04')
        Path(scan_dir, 'Huber_Schreiben.txt').write_text('text')
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=db_path)
        count = scanner.scan_sync()
        assert count == 3, f'Erwartet 3, indexiert: {count}'


def test_delta_scan_aktualisiert_index():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        db_path = Path(db_dir) / 'drive_index.db'
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=db_path)
        # Erster Scan
        Path(scan_dir, 'Erster_Schriftsatz.txt').write_text('text')
        scanner.scan_sync()
        assert scanner.index.count() == 1
        # Zweite Datei hinzufügen
        Path(scan_dir, 'Zweiter_Schriftsatz.pdf').write_bytes(b'%PDF-1.4')
        scanner.scan_delta()
        assert scanner.index.count() == 2, \
            f'Delta-Scan hat neue Datei nicht indexiert. Count: {scanner.index.count()}'


# ── Hintergrund-Scan ─────────────────────────────────────────────────────────

def test_hintergrund_scan_startet_thread():
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        db_path = Path(db_dir) / 'drive_index.db'
        Path(scan_dir, 'Gruber_Antrag.txt').write_text('text')
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=db_path)
        threads_before = threading.active_count()
        scanner.start_background_scan()
        # Thread muss gestartet worden sein oder bereits fertig
        scanner.wait_for_scan(timeout=10.0)
        assert scanner.index.count() >= 1, 'Hintergrund-Scan hat nicht indexiert'


def test_hintergrund_scan_ist_nicht_blockierend():
    """start_background_scan() darf den Haupt-Thread nicht blockieren."""
    mod = _import_scanner()
    with tempfile.TemporaryDirectory() as scan_dir, tempfile.TemporaryDirectory() as db_dir:
        db_path = Path(db_dir) / 'drive_index.db'
        for i in range(5):
            Path(scan_dir, f'Mandant{i}_Schrift.txt').write_text('x' * 1000)
        settings = mod.ScanSettings(scan_path=scan_dir)
        scanner = mod.DriveScanner(settings, db_path=db_path)
        t0 = time.monotonic()
        scanner.start_background_scan()
        elapsed = time.monotonic() - t0
        assert elapsed < 1.0, f'start_background_scan() blockiert {elapsed:.2f}s (soll sofort zurückkehren)'
        scanner.wait_for_scan(timeout=15.0)
