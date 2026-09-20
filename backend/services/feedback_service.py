import os
import re
import logging
from datetime import datetime
from typing import Optional

import json

from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker

from models import Base, FeedbackEntry

logger = logging.getLogger(__name__)

KATEGORIE_FORMULIERUNG = "formulierung"
KATEGORIE_SATZSTRUKTUR = "satzstruktur"
KATEGORIE_PARAGRAPH = "paragraph"
KATEGORIE_LAENGE = "laenge"

GUELTIGE_KATEGORIEN = {
    KATEGORIE_FORMULIERUNG,
    KATEGORIE_SATZSTRUKTUR,
    KATEGORIE_PARAGRAPH,
    KATEGORIE_LAENGE,
}

# Zweite Schutzschicht falls die Client-seitige Anonymisierung (T#82/T#89)
# versagt hat: lehnt Feedback ab, das noch wie Klartextnamen aussieht.
_ANREDE_NAME_PATTERN = re.compile(
    r"\b(Herr|Frau|Mandant(?:in)?|Kläger(?:in)?|Beklagte[r]?)\s+[A-ZÄÖÜ][a-zäöüß]+\b"
)
_EMAIL_PATTERN = re.compile(r"[\w.+-]+@[\w-]+\.[\w.-]+")

_PARAGRAPH_PATTERN = re.compile(r"§\s?\d+[a-z]?")
_SATZENDE_PATTERN = re.compile(r"[.!?]+")

LAENGEN_SCHWELLE = 0.2  # relative Wortzahl-Abweichung ab der als "Länge"-Korrektur gilt


class FeedbackValidationError(ValueError):
    """Feedback wurde abgelehnt, z.B. weil es Klartextnamen enthält."""


def enthaelt_klartextnamen(*texte: str) -> bool:
    """Prüft heuristisch, ob übermittelter Text noch Klartextnamen enthält."""
    for text in texte:
        if not text:
            continue
        if _ANREDE_NAME_PATTERN.search(text):
            return True
        if _EMAIL_PATTERN.search(text):
            return True
    return False


class FeedbackService:
    """Verarbeitet eingehende, anonymisierte Anwalt-Korrekturen (POST /api/feedback)."""

    def __init__(self, db_url: Optional[str] = None):
        self.db_url = db_url or os.getenv(
            "DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf"
        )
        self.engine = create_engine(self.db_url)
        self.SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=self.engine)
        Base.metadata.create_all(bind=self.engine, tables=[FeedbackEntry.__table__])
        self._ensure_feedback_metric_columns()

    def _ensure_feedback_metric_columns(self):
        """Fügt bei einer bestehenden Tabelle fehlende Metrik-Spalten idempotent hinzu."""
        columns = [
            "satzlaengen_verteilung",
            "wortklassen_haeufigkeiten",
            "korrektur_kategorien",
        ]
        with self.engine.begin() as conn:
            for col in columns:
                conn.execute(text(
                    f"ALTER TABLE {FeedbackEntry.__tablename__} "
                    f"ADD COLUMN IF NOT EXISTS {col} TEXT"
                ))

    @staticmethod
    def kategorisiere_diff(original: str, korrigiert: str) -> str:
        """Ordnet die Korrektur einer Kategorie zu: Paragraph > Länge > Satzstruktur > Formulierung."""
        original = (original or "").strip()
        korrigiert = (korrigiert or "").strip()

        orig_paragraphen = set(_PARAGRAPH_PATTERN.findall(original))
        korr_paragraphen = set(_PARAGRAPH_PATTERN.findall(korrigiert))
        if orig_paragraphen != korr_paragraphen:
            return KATEGORIE_PARAGRAPH

        orig_woerter = original.split()
        korr_woerter = korrigiert.split()
        laengen_diff = abs(len(korr_woerter) - len(orig_woerter))
        laenge_referenz = max(len(orig_woerter), 1)
        if laengen_diff / laenge_referenz > LAENGEN_SCHWELLE:
            return KATEGORIE_LAENGE

        orig_saetze = [s for s in _SATZENDE_PATTERN.split(original) if s.strip()]
        korr_saetze = [s for s in _SATZENDE_PATTERN.split(korrigiert) if s.strip()]
        if len(orig_saetze) != len(korr_saetze):
            return KATEGORIE_SATZSTRUKTUR

        return KATEGORIE_FORMULIERUNG

    def process_feedback(self, payload: dict) -> dict:
        """Validiert, kategorisiert und speichert ein Feedback-Diff. Wirft FeedbackValidationError bei Verstößen."""
        original = payload.get("original", "")
        korrigiert = payload.get("korrigiert", "")

        if not original or not korrigiert:
            raise FeedbackValidationError("original und korrigiert sind erforderlich")

        if enthaelt_klartextnamen(original, korrigiert):
            raise FeedbackValidationError(
                "Feedback enthält mutmaßlich Klartextnamen und wurde abgelehnt"
            )

        kategorie = self.kategorisiere_diff(original, korrigiert)
        return self._speichere(original, korrigiert, kategorie, payload.get("zeitstempel"))

    def _speichere(self, original: str, korrigiert: str, kategorie: str, zeitstempel: Optional[str]) -> dict:
        """Speichert ein Feedback-Diff. Die Rohtexte werden NICHT persistiert,
        sondern nur die abgeleitete Kategorie."""
        zeitstempel_dt = self._parse_zeitstempel(zeitstempel)

        db = self.SessionLocal()
        try:
            eintrag = FeedbackEntry(
                original=None,
                korrigiert=None,
                kategorie=kategorie,
                zeitstempel=zeitstempel_dt or datetime.utcnow(),
            )
            db.add(eintrag)
            db.commit()
            db.refresh(eintrag)
            return {
                "id": eintrag.id,
                "kategorie": eintrag.kategorie,
                "zeitstempel": eintrag.zeitstempel.isoformat(),
            }
        finally:
            db.close()

    @staticmethod
    def _parse_zeitstempel(zeitstempel: Optional[str]) -> Optional[datetime]:
        if not zeitstempel:
            return None
        try:
            return datetime.fromisoformat(str(zeitstempel).replace("Z", "+00:00"))
        except ValueError:
            return None

    def process_metrics_feedback(self, payload: dict) -> dict:
        """Speichert einen reinen Metrik-Payload ohne jegliche Rohtexte."""
        kategorien = payload.get("korrektur_kategorien", {})
        if not kategorien:
            raise FeedbackValidationError("korrektur_kategorien sind erforderlich")

        # Nutze die häufigste Kategorie als Repräsentant für diese Metrik-Batch.
        kategorie = max(kategorien.items(), key=lambda item: item[1])[0]
        if kategorie not in GUELTIGE_KATEGORIEN:
            raise FeedbackValidationError(f"Ungültige Korrektur-Kategorie: {kategorie}")

        zeitstempel = payload.get("zeitstempel")
        db = self.SessionLocal()
        try:
            eintrag = FeedbackEntry(
                original=None,
                korrigiert=None,
                kategorie=kategorie,
                satzlaengen_verteilung=json.dumps(payload.get("satzlaengen_verteilung", {}), ensure_ascii=False),
                wortklassen_haeufigkeiten=json.dumps(payload.get("wortklassen_haeufigkeiten", {}), ensure_ascii=False),
                korrektur_kategorien=json.dumps(kategorien, ensure_ascii=False),
                zeitstempel=self._parse_zeitstempel(zeitstempel) or datetime.utcnow(),
            )
            db.add(eintrag)
            db.commit()
            db.refresh(eintrag)
            return {
                "id": eintrag.id,
                "kategorie": eintrag.kategorie,
                "zeitstempel": eintrag.zeitstempel.isoformat(),
            }
        finally:
            db.close()
