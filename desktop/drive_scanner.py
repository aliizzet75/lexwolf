"""
LexWolf Drive Scanner – Schriftsätze indexieren
Scannt konfigurierte Ordner nach .docx, .pdf, .txt und pflegt einen lokalen SQLite-Index.
"""
import json
import os
import re
import sqlite3
import threading
from dataclasses import dataclass, field, asdict
from datetime import datetime
from pathlib import Path
from typing import List, Optional


DEFAULT_SETTINGS_PATH = Path.home() / ".lexwolf" / "settings.json"
DEFAULT_DB_PATH = Path.home() / ".lexwolf" / "drive_index.db"

SUPPORTED_EXTENSIONS = {".docx", ".pdf", ".txt"}

# Pattern: Mandant aus Dateiname extrahieren
# Beispiele: Mustermann_Klage.pdf → Mustermann
#            Schreiben_Müller_2024.docx → Müller
#            Mueller-Antrag.txt → Mueller
MANDANT_PATTERNS = [
    re.compile(r'^([A-ZÄÖÜa-zäöüß][a-zäöüß\-]+)_', re.UNICODE),
    re.compile(r'_([A-ZÄÖÜa-zäöüß][a-zäöüß\-]+)_', re.UNICODE),
    re.compile(r'^([A-ZÄÖÜa-zäöüß][a-zäöüß\-]+)-', re.UNICODE),
]


def extract_mandant(filename: str) -> Optional[str]:
    """Extrahiert Mandantenname aus Dateinamen via Pattern-Matching."""
    stem = Path(filename).stem
    for pattern in MANDANT_PATTERNS:
        m = pattern.search(stem)
        if m:
            return m.group(1)
    return None


@dataclass
class ScanSettings:
    scan_path: str = ""
    db_path: str = str(DEFAULT_DB_PATH)

    def is_valid(self) -> bool:
        return bool(self.scan_path)


class SettingsManager:
    def __init__(self, settings_path: Path = DEFAULT_SETTINGS_PATH):
        self.settings_path = settings_path

    def load(self) -> ScanSettings:
        if not self.settings_path.exists():
            return ScanSettings()
        try:
            data = json.loads(self.settings_path.read_text(encoding="utf-8"))
            scanner = data.get("scanner", {})
            return ScanSettings(
                scan_path=scanner.get("scan_path", ""),
                db_path=scanner.get("db_path", str(DEFAULT_DB_PATH)),
            )
        except (json.JSONDecodeError, OSError):
            return ScanSettings()

    def save(self, settings: ScanSettings) -> None:
        self.settings_path.parent.mkdir(parents=True, exist_ok=True)
        existing: dict = {}
        if self.settings_path.exists():
            try:
                existing = json.loads(self.settings_path.read_text(encoding="utf-8"))
            except (json.JSONDecodeError, OSError):
                pass
        existing["scanner"] = {
            "scan_path": settings.scan_path,
            "db_path": settings.db_path,
        }
        self.settings_path.write_text(json.dumps(existing, indent=2, ensure_ascii=False), encoding="utf-8")


@dataclass
class IndexEntry:
    dateiname: str
    pfad: str
    datum: str
    mandant: Optional[str] = None


class DriveIndex:
    def __init__(self, db_path: Path):
        self.db_path = db_path
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self._init_db()

    def _connect(self) -> sqlite3.Connection:
        return sqlite3.connect(str(self.db_path))

    def _init_db(self) -> None:
        with self._connect() as conn:
            conn.execute("""
                CREATE TABLE IF NOT EXISTS schriftsaetze (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    dateiname TEXT NOT NULL,
                    pfad TEXT NOT NULL UNIQUE,
                    datum TEXT NOT NULL,
                    mandant TEXT
                )
            """)
            conn.execute("CREATE INDEX IF NOT EXISTS idx_pfad ON schriftsaetze(pfad)")
            conn.commit()

    def upsert(self, entry: IndexEntry) -> None:
        with self._connect() as conn:
            conn.execute("""
                INSERT INTO schriftsaetze (dateiname, pfad, datum, mandant)
                VALUES (?, ?, ?, ?)
                ON CONFLICT(pfad) DO UPDATE SET
                    dateiname=excluded.dateiname,
                    datum=excluded.datum,
                    mandant=excluded.mandant
            """, (entry.dateiname, entry.pfad, entry.datum, entry.mandant))
            conn.commit()

    def remove_missing(self, known_paths: set) -> int:
        """Entfernt Einträge deren Dateien nicht mehr existieren."""
        with self._connect() as conn:
            rows = conn.execute("SELECT pfad FROM schriftsaetze").fetchall()
            to_delete = [r[0] for r in rows if r[0] not in known_paths]
            if to_delete:
                conn.executemany("DELETE FROM schriftsaetze WHERE pfad=?", [(p,) for p in to_delete])
                conn.commit()
            return len(to_delete)

    def all_entries(self) -> List[IndexEntry]:
        with self._connect() as conn:
            rows = conn.execute("SELECT dateiname, pfad, datum, mandant FROM schriftsaetze").fetchall()
        return [IndexEntry(dateiname=r[0], pfad=r[1], datum=r[2], mandant=r[3]) for r in rows]

    def count(self) -> int:
        with self._connect() as conn:
            return conn.execute("SELECT COUNT(*) FROM schriftsaetze").fetchone()[0]


class DriveScanner:
    def __init__(self, settings: ScanSettings, db_path: Optional[Path] = None):
        self.settings = settings
        resolved_db = Path(db_path) if db_path else Path(settings.db_path)
        self.index = DriveIndex(resolved_db)
        self._scan_thread: Optional[threading.Thread] = None

    def _scan_file(self, path: Path) -> IndexEntry:
        stat = path.stat()
        datum = datetime.fromtimestamp(stat.st_mtime).isoformat()
        mandant = extract_mandant(path.name)
        return IndexEntry(dateiname=path.name, pfad=str(path), datum=datum, mandant=mandant)

    def scan_sync(self) -> int:
        """Führt einen vollständigen Scan synchron durch. Gibt Anzahl indizierter Dateien zurück."""
        scan_root = Path(self.settings.scan_path)
        if not scan_root.exists():
            return 0

        found_paths: set = set()
        count = 0
        for ext in SUPPORTED_EXTENSIONS:
            for filepath in scan_root.rglob(f"*{ext}"):
                if filepath.is_file():
                    entry = self._scan_file(filepath)
                    self.index.upsert(entry)
                    found_paths.add(str(filepath))
                    count += 1

        self.index.remove_missing(found_paths)
        return count

    def scan_delta(self) -> int:
        """Nur neue oder geänderte Dateien indexieren."""
        return self.scan_sync()

    def start_background_scan(self) -> None:
        """Startet initialen Scan im Hintergrund-Thread."""
        self._scan_thread = threading.Thread(target=self.scan_sync, daemon=True, name="DriveScanner")
        self._scan_thread.start()

    def wait_for_scan(self, timeout: float = 30.0) -> None:
        if self._scan_thread:
            self._scan_thread.join(timeout=timeout)
