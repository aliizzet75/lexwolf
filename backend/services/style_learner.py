"""
Style-Learner Service für LexWolf.

Analysiert die akkumulierten Korrekturen aus der feedback_table und lernt
daraus automatisch ein stil-basiertes Profil pro Anwalt-Account. Das Profil
wird in der style_profiles-Tabelle gespeichert und kann von der
Schriftsatz-Generation als kontextuelles Bias verwendet werden.

Lern-Input:  feedback_table (original, korrigiert, kategorie, zeitstempel)
Lern-Output: StyleProfile Eintrag mit
              - bevorzugter Satzlänge (Durchschnitt korrigierte Sätze)
              - Formulierungs-Präferenzen (häufige Einzelwörter / N-Gramme)
              - häufig korrigierte Muster (Kategorie-Verteilung + häufige Diff-Phrasen)
              - abstrahiertem Vektor für spätere Similarity-Suche

Ablauf (täglich via Cron):
  1. Lade Feedback-Einträge der letzten 30 Tage.
  2. Berechne durchschnittliche Wortzahl pro Satz der korrigierten Texte.
  3. Extrahiere Term-/Bigramm-Präferenzen aus den korrigierten Texten.
  4. Aggregiere Kategorie-Häufigkeiten und häufige Korrigendum-Phrasen.
  5. Speichere oder update style_profiles (JSON-Metadaten + pgvector-Vektor).
"""

import json
import logging
import os
import random
import re
import uuid
from collections import Counter
from datetime import datetime, timedelta
from typing import Any, Dict, List, Optional

from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker

from models import Base, FeedbackEntry, StyleProfile

logger = logging.getLogger(__name__)

SATZENDE_PATTERN = re.compile(r"[.!?]+")
WORT_PATTERN = re.compile(r"[A-Za-zÄÖÜäöüß0-9_\\-]+")
PARAGRAPH_PATTERN = re.compile(r"§\\s?\\d+[a-z]?")
STOP_WOERTER = {
    "der", "die", "das", "den", "dem", "des", "ein", "eine", "einer", "eines",
    "einem", "einen", "und", "oder", "aber", "sondern", "mit", "zu", "zur",
    "zum", "bei", "von", "vom", "für", "fuer", "in", "im", "an", "am", "auf",
    "aus", "als", "wie", "dass", "daß", "es", "sie", "er", "es", "man", "wird",
    "werden", "wurde", "worden", "sein", "sind", "ist", "war", "waren", "haben",
    "hat", "hatte", "kann", "könnte", "soll", "sollte", "muss", "muesste", "darf",
    "dürfte", "wird", "wurden", "wurde", "nicht", "kein", "keine", "ja", "nein",
}

PROFIL_VEKTOR_DIM = 768


ERLAUBTE_SCHRIFTSATZ_TYPEN = {"klage", "widerspruch", "brief"}


class StyleLearner:
    """Lernt aus feedback_table und aktualisiert das StyleProfile in style_profiles."""

    def __init__(
        self,
        db_url: Optional[str] = None,
        account_id: str = "default",
        schriftsatz_typ: Optional[str] = None,
    ):
        self.db_url = db_url or os.getenv(
            "DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf"
        )
        self.account_id = account_id
        self.schriftsatz_typ = self._normalisiere_schriftsatz_typ(schriftsatz_typ)
        # Generisches Profil hat keine Typ-Suffix; typ-spezifische Profile erhalten z.B. sp_default_klage.
        if self.schriftsatz_typ:
            self.profile_id = f"sp_{account_id}_{self.schriftsatz_typ}"
        else:
            self.profile_id = f"sp_{account_id}"
        self.engine = create_engine(self.db_url)
        self.SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=self.engine)
        Base.metadata.create_all(bind=self.engine, tables=[FeedbackEntry.__table__, StyleProfile.__table__])
        self._ensure_style_profile_columns()

    @staticmethod
    def _normalisiere_schriftsatz_typ(typ: Optional[str]) -> Optional[str]:
        """Akzeptiert API-Parameter; gibt bekannten Typ zurück oder None für generisches Profil."""
        if not typ:
            return None
        typ_norm = typ.strip().lower()
        return typ_norm if typ_norm in ERLAUBTE_SCHRIFTSATZ_TYPEN else None

    def _hole_generisches_profil_id(self) -> str:
        return f"sp_{self.account_id}"

    def _hole_generisches_profil(self) -> Optional[StyleProfile]:
        """Liest das generische (nicht typ-spezifische) Profil aus der DB."""
        db = self.SessionLocal()
        try:
            return db.query(StyleProfile).filter(StyleProfile.profile_id == self._hole_generisches_profil_id()).first()
        finally:
            db.close()

    @property
    def ist_generisches_profil(self) -> bool:
        return self.schriftsatz_typ is None

    def verwende_typ_spezifisches_profil(self, fallback: bool = True) -> Optional[StyleProfile]:
        """
        Gibt das für den aktuellen Schriftsatz-Typ passende Profil zurück.
        Bei unbekanntem Typ oder fehlendem typ-spezifischem Profil wird optional
        auf das generische Profil zurückgegriffen.
        """
        db = self.SessionLocal()
        try:
            if self.schriftsatz_typ:
                profil = db.query(StyleProfile).filter(StyleProfile.profile_id == self.profile_id).first()
                if profil:
                    return profil
            if fallback:
                return db.query(StyleProfile).filter(StyleProfile.profile_id == self._hole_generisches_profil_id()).first()
            return None
        finally:
            db.close()

    def _ensure_style_profile_columns(self) -> None:
        """Stellt sicher, dass style_profiles alle gelernten Metadaten-Spalten besitzt."""
        required_columns = [
            ("durchschnittliche_satzlaenge", "DOUBLE PRECISION"),
            ("formulierungs_praeferenzen", "TEXT"),
            ("korrigierte_muster", "TEXT"),
            ("anzahl_eintraege", "INTEGER"),
            ("ab_test_json", "TEXT"),
        ]
        try:
            with self.engine.connect() as conn:
                for column_name, column_type in required_columns:
                    conn.execute(
                        text(
                            f"ALTER TABLE style_profiles ADD COLUMN IF NOT EXISTS {column_name} {column_type}"
                        )
                    )
                conn.commit()
                logger.debug("style_profiles Spalten erfolgreich geprüft/ergänzt.")
        except Exception as e:
            logger.warning("Konnte style_profiles Spalten nicht ergänzen (DB evtl. nicht erreichbar): %s", e)

    # ------------------------------------------------------------------
    # Öffentliche API
    # ------------------------------------------------------------------

    def aktualisiere_stil_profil(self) -> Dict[str, Any]:
        """Haupt-Methode: Analysiert Feedback und persistiert ein Stil-Profil."""
        seit = datetime.utcnow() - timedelta(days=30)
        eintraege = self._lade_feedback(seit)

        if not eintraege:
            logger.info("Kein Feedback vorhanden – erstelle leeres Standard-Profil.")
            ergebnis = self._baue_ergebnis(
                durchschnittliche_satzlaenge=None,
                formulierungs_praeferenzen={},
                korrigierte_muster={},
                anzahl_eintraege=0,
            )
        else:
            korrigierte_texte = [e.korrigiert for e in eintraege if e.korrigiert]
            kategorien = [e.kategorie for e in eintraege if e.kategorie]

            durchschnittliche_satzlaenge = self._berechne_durchschnittliche_satzlaenge(korrigierte_texte)
            formulierungs_praeferenzen = self._extrahiere_formulierungs_praeferenzen(korrigierte_texte)
            korrigierte_muster = self._aggregiere_korrigierte_muster(eintraege, kategorien)

            ergebnis = self._baue_ergebnis(
                durchschnittliche_satzlaenge=durchschnittliche_satzlaenge,
                formulierungs_praeferenzen=formulierungs_praeferenzen,
                korrigierte_muster=korrigierte_muster,
                anzahl_eintraege=len(eintraege),
            )

        self._speichere_profil(ergebnis)
        return ergebnis

    # ------------------------------------------------------------------
    # A/B-Test API (T#90)
    # ------------------------------------------------------------------

    def waehle_ab_test_variante(self, formulierung_alt: str = "", formulierung_neu: str = "") -> Dict[str, Any]:
        """Wählt zufällig 'alt' oder 'neu' und gibt ein Antwort-Objekt mit A/B-Test-Flag zurück."""
        variante = random.choice(["alt", "neu"])
        ab_test_id = str(uuid.uuid4())
        self._speichere_ab_test(ab_test_id, variante, formulierung_alt, formulierung_neu)
        return {
            "ab_test_id": ab_test_id,
            "variante": variante,
            "text": formulierung_neu if variante == "neu" else formulierung_alt,
        }

    def tracke_ab_test_praeferenz(self, ab_test_id: str, korrigiert: bool = False) -> Optional[Dict[str, Any]]:
        """
        Tracke implizite Anwalt-Präferenz.
        'korrigiert=False' bedeutet: keine Korrektur nötig = implizite Zustimmung.
        """
        db = self.SessionLocal()
        try:
            profile = db.query(StyleProfile).filter(StyleProfile.profile_id == self.profile_id).first()
            if profile is None or not profile.ab_test_json:
                return None

            state = json.loads(profile.ab_test_json)
            running = state.get("running_tests", {})
            if ab_test_id not in running:
                return None

            test = running[ab_test_id]
            if korrigiert:
                test["praeferenz"] = "neu_abgelehnt"
            else:
                test["praeferenz"] = "implizit_alt" if test["variante"] == "alt" else "implizit_neu"
            test["korrigiert"] = korrigiert
            test["beantwortet_am"] = datetime.utcnow().isoformat()

            state.setdefault("history", []).append(test)
            del running[ab_test_id]

            auswertung = self._auswerte_ab_test(state)
            state["auswertung"] = auswertung
            profile.ab_test_json = json.dumps(state, ensure_ascii=False)
            db.commit()
            return auswertung
        except Exception as e:
            db.rollback()
            logger.error("Fehler beim Tracken der A/B-Test-Präferenz: %s", e)
            return None
        finally:
            db.close()

    def _speichere_ab_test(self, ab_test_id: str, variante: str, alt: str, neu: str) -> None:
        db = self.SessionLocal()
        try:
            profile = db.query(StyleProfile).filter(StyleProfile.profile_id == self.profile_id).first()
            if profile is None:
                # Leeres Profil als A/B-Test-State-Speicher anlegen.
                from sqlalchemy.dialects.postgresql import insert as pg_insert
                stmt = pg_insert(StyleProfile).values(
                    profile_id=self.profile_id,
                    vector=[0.0] * PROFIL_VEKTOR_DIM,
                    created_at=datetime.utcnow(),
                    updated_at=datetime.utcnow(),
                    ab_test_json=json.dumps({"running_tests": {}}, ensure_ascii=False),
                ).on_conflict_do_nothing(index_elements=["profile_id"])
                db.execute(stmt)
                db.commit()
                profile = db.query(StyleProfile).filter(StyleProfile.profile_id == self.profile_id).first()

            state = json.loads(profile.ab_test_json or '{"running_tests": {}}')
            state.setdefault("running_tests", {})[ab_test_id] = {
                "ab_test_id": ab_test_id,
                "variante": variante,
                "alt": alt,
                "neu": neu,
                "erstellt_am": datetime.utcnow().isoformat(),
            }
            profile.ab_test_json = json.dumps(state, ensure_ascii=False)
            db.commit()
        except Exception as e:
            db.rollback()
            logger.error("Fehler beim Speichern des A/B-Tests: %s", e)
            raise
        finally:
            db.close()

    def _auswerte_ab_test(self, state: Dict[str, Any]) -> Dict[str, Any]:
        """Berechnet nach 20 Samples automatisch, ob 'neu' oder 'alt' gewinnt."""
        history = state.get("history", [])
        relevant = [h for h in history if "praeferenz" in h]

        if len(relevant) < 20:
            return {"ausgewertet": False, "samples": len(relevant)}

        alt_stimmen = sum(1 for h in relevant if h["praeferenz"] in ("implizit_alt",))
        neu_stimmen = sum(1 for h in relevant if h["praeferenz"] in ("implizit_neu",))
        abgelehnt = sum(1 for h in relevant if h["praeferenz"] == "neu_abgelehnt")
        total = len(relevant)

        # Neu gewinnt, wenn mindestens so viele implizite Zustimmungen wie alt.
        # Implizite Zustimmung = keine Korrektur nötig.
        gewinner = "neu" if neu_stimmen >= alt_stimmen else "alt"

        return {
            "ausgewertet": True,
            "samples": total,
            "alt_stimmen": alt_stimmen,
            "neu_stimmen": neu_stimmen,
            "neu_abgelehnt": abgelehnt,
            "gewinner": gewinner,
            "ratio_neu": round(neu_stimmen / total, 4) if total else 0,
            "ratio_alt": round(alt_stimmen / total, 4) if total else 0,
        }

    # ------------------------------------------------------------------
    # Interne Methoden
    # ------------------------------------------------------------------

    def _lade_feedback(self, seit: datetime) -> List[FeedbackEntry]:
        db = self.SessionLocal()
        try:
            eintraege = (
                db.query(FeedbackEntry)
                .filter(FeedbackEntry.zeitstempel >= seit)
                .order_by(FeedbackEntry.zeitstempel.desc())
                .all()
            )
            return eintraege
        finally:
            db.close()

    @staticmethod
    def _berechne_durchschnittliche_satzlaenge(texte: List[str]) -> Optional[float]:
        """Durchschnittliche Wortzahl pro Satz über alle korrigierten Texte."""
        satz_wort_zahlen: List[int] = []
        for text in texte:
            if not text:
                continue
            saetze = [s.strip() for s in SATZENDE_PATTERN.split(text) if s.strip()]
            for satz in saetze:
                wortzahl = len(WORT_PATTERN.findall(satz))
                if wortzahl:
                    satz_wort_zahlen.append(wortzahl)
        if not satz_wort_zahlen:
            return None
        return round(sum(satz_wort_zahlen) / len(satz_wort_zahlen), 2)

    @staticmethod
    def _extrahiere_formulierungs_praeferenzen(texte: List[str], top_n: int = 20) -> Dict[str, Any]:
        """Zählt Einzelwörter und Bigramme in den korrigierten Texten."""
        einzel_counter: Counter = Counter()
        bigramm_counter: Counter = Counter()

        for text in texte:
            tokens = [t.lower() for t in WORT_PATTERN.findall(text) if t.lower() not in STOP_WOERTER]
            einzel_counter.update(tokens)
            for i in range(len(tokens) - 1):
                bigramm_counter.update([f"{tokens[i]} {tokens[i + 1]}"])

        return {
            "top_einzelwoerter": dict(einzel_counter.most_common(top_n)),
            "top_bigramme": dict(bigramm_counter.most_common(top_n)),
            "gesamt_unterschiedliche_woerter": len(einzel_counter),
        }

    @staticmethod
    def _aggregiere_korrigierte_muster(
        eintraege: List[FeedbackEntry], kategorien: List[str]
    ) -> Dict[str, Any]:
        """Bestimmt häufigste Korrektur-Kategorien und häufige Diff-Phrasen."""
        kategorie_counter = Counter(kategorien)

        # Für jede Kategorie sammeln wir die häufigsten 'korrigiert'-Substrings.
        kategorie_phrasen: Dict[str, Counter] = {
            "formulierung": Counter(),
            "satzstruktur": Counter(),
            "paragraph": Counter(),
            "laenge": Counter(),
        }

        for eintrag in eintraege:
            kat = eintrag.kategorie
            if not kat or kat not in kategorie_phrasen:
                continue
            tokens = [t.lower() for t in WORT_PATTERN.findall(eintrag.korrigiert or "") if t.lower() not in STOP_WOERTER]
            for i in range(len(tokens) - 1):
                kategorie_phrasen[kat].update([f"{tokens[i]} {tokens[i + 1]}"])
            for tok in tokens[:8]:  # Erste Wörter des korrigierten Satzes als Muster
                kategorie_phrasen[kat].update([tok])

        top_phrasen = {
            kat: dict(counter.most_common(10))
            for kat, counter in kategorie_phrasen.items()
            if counter
        }

        return {
            "kategorie_verteilung": dict(kategorie_counter),
            "top_korrigierte_phrasen_pro_kategorie": top_phrasen,
            "gesamt_korrekturen": len(eintraege),
        }

    def _baue_ergebnis(
        self,
        durchschnittliche_satzlaenge: Optional[float],
        formulierungs_praeferenzen: Dict[str, Any],
        korrigierte_muster: Dict[str, Any],
        anzahl_eintraege: int,
    ) -> Dict[str, Any]:
        return {
            "profile_id": self.profile_id,
            "account_id": self.account_id,
            "durchschnittliche_satzlaenge": durchschnittliche_satzlaenge,
            "formulierungs_praeferenzen": formulierungs_praeferenzen,
            "korrigierte_muster": korrigierte_muster,
            "anzahl_eintraege": anzahl_eintraege,
            "aktualisiert_am": datetime.utcnow().isoformat(),
        }

    def _speichere_profil(self, ergebnis: Dict[str, Any]) -> None:
        db = self.SessionLocal()
        try:
            profile = db.query(StyleProfile).filter(StyleProfile.profile_id == self.profile_id).first()
            vektor = self._erzeuge_profil_vektor(ergebnis)

            meta = {
                "durchschnittliche_satzlaenge": ergebnis["durchschnittliche_satzlaenge"],
                "formulierungs_praeferenzen": ergebnis["formulierungs_praeferenzen"],
                "korrigierte_muster": ergebnis["korrigierte_muster"],
                "anzahl_eintraege": ergebnis["anzahl_eintraege"],
                "aktualisiert_am": ergebnis["aktualisiert_am"],
            }
            # Metadaten werden direkt als dedizierte Spalten in style_profiles
            # gespeichert (durchschnittliche_satzlaenge, formulierungs_praeferenzen,
            # korrigierte_muster, anzahl_eintraege). Zusätzlich wird ein pgvector-
            # Vektor aus den gelernten Profil-Werten erzeugt.

            if profile:
                profile.vector = vektor
                profile.durchschnittliche_satzlaenge = ergebnis["durchschnittliche_satzlaenge"]
                profile.formulierungs_praeferenzen = json.dumps(ergebnis["formulierungs_praeferenzen"], ensure_ascii=False)
                profile.korrigierte_muster = json.dumps(ergebnis["korrigierte_muster"], ensure_ascii=False)
                profile.anzahl_eintraege = ergebnis["anzahl_eintraege"]
                profile.updated_at = datetime.utcnow()
                if profile.ab_test_json is None:
                    profile.ab_test_json = json.dumps({"running_tests": {}}, ensure_ascii=False)
            else:
                profile = StyleProfile(
                    profile_id=self.profile_id,
                    vector=vektor,
                    durchschnittliche_satzlaenge=ergebnis["durchschnittliche_satzlaenge"],
                    formulierungs_praeferenzen=json.dumps(ergebnis["formulierungs_praeferenzen"], ensure_ascii=False),
                    korrigierte_muster=json.dumps(ergebnis["korrigierte_muster"], ensure_ascii=False),
                    anzahl_eintraege=ergebnis["anzahl_eintraege"],
                    ab_test_json=json.dumps({"running_tests": {}}, ensure_ascii=False),
                    created_at=datetime.utcnow(),
                    updated_at=datetime.utcnow(),
                )
                db.add(profile)

            db.commit()
            logger.info("StyleProfile %s aktualisiert (%s Einträge).", self.profile_id, ergebnis["anzahl_eintraege"])
        except Exception as e:
            db.rollback()
            logger.error("Fehler beim Speichern des StyleProfils: %s", e)
            raise
        finally:
            db.close()

    def _erzeuge_profil_vektor(self, ergebnis: Dict[str, Any]) -> List[float]:
        """Erzeugt einen deterministischen 768-dim-Vektor aus dem gelernten Profil."""
        import hashlib

        # Einfacher, interpretierbarer Vektor: Satzlänge + Kategorie-Dichte + Präferenz-Bag.
        vektor = [0.0] * PROFIL_VEKTOR_DIM

        laenge = ergebnis.get("durchschnittliche_satzlaenge") or 0.0
        vektor[0] = float(laenge) / 100.0  # Normiert

        verteilung = ergebnis.get("korrigierte_muster", {}).get("kategorie_verteilung", {})
        for i, kat in enumerate(["formulierung", "satzstruktur", "paragraph", "laenge"], start=1):
            vektor[i] = float(verteilung.get(kat, 0)) / max(1, ergebnis.get("anzahl_eintraege", 1))

        # Top-Einzelwörter als Hash-Signatur in den restlichen Dimensionen streuen.
        top_woerter = ergebnis.get("formulierungs_praeferenzen", {}).get("top_einzelwoerter", {})
        for idx, (wort, count) in enumerate(top_woerter.items()):
            if idx >= 100:
                break
            digest = hashlib.md5(wort.encode("utf-8")).hexdigest()
            position = (int(digest[:8], 16) % (PROFIL_VEKTOR_DIM - 10)) + 10
            vektor[position] += float(count) / 100.0

        # Normalisieren auf Länge ~1
        norm = sum(v * v for v in vektor) ** 0.5 or 1.0
        return [round(v / norm, 6) for v in vektor]

    def berechne_lernfortschritt(self) -> Dict[str, Any]:
        """Liefert einen heuristischen Lern-Fortschritt zwischen 0 und 1."""
        try:
            db = self.SessionLocal()
            try:
                profil = db.query(StyleProfile).filter(StyleProfile.profile_id == self.profile_id).first()
                # Wenn kein typ-spezifisches Profil, generisches Profil verwenden.
                if profil is None and self.schriftsatz_typ:
                    generisch_id = self._hole_generisches_profil_id()
                    profil = db.query(StyleProfile).filter(StyleProfile.profile_id == generisch_id).first()

                anzahl = profil.anzahl_eintraege if profil and profil.anzahl_eintraege else 0

                # Ziel-Sample-Zahl: 80 liefert "100 %", aber progressiv gedämpft.
                ziel = 80.0
                lern_fortschritt = min(1.0, anzahl / ziel)

                # Bekannte Aspekte hängen von verfügbaren Profil-Metadaten ab.
                bekannte_aspekte = []
                if profil:
                    if profil.durchschnittliche_satzlaenge is not None:
                        bekannte_aspekte.append("Satzlänge")
                    if profil.formulierungs_praeferenzen:
                        try:
                            praef = json.loads(profil.formulierungs_praeferenzen)
                            if praef.get("top_einzelwoerter") or praef.get("top_bigramme"):
                                bekannte_aspekte.append("Formalitätsgrad")
                            if profil.korrigierte_muster:
                                muster = json.loads(profil.korrigierte_muster)
                                if muster.get("kategorie_verteilung"):
                                    bekannte_aspekte.append("Korrektur-Muster")
                        except (json.JSONDecodeError, TypeError):
                            pass
                if not bekannte_aspekte:
                    bekannte_aspekte.append("Noch im Lernen")

                return {
                    "lern_fortschritt": round(lern_fortschritt, 2),
                    "bekannte_aspekte": bekannte_aspekte,
                    "samples_gesammelt": int(anzahl),
                }
            finally:
                db.close()
        except Exception as exc:
            logger.warning("Konnte Lern-Fortschritt nicht aus DB berechnen: %s", exc)
            return {
                "lern_fortschritt": 0.0,
                "bekannte_aspekte": ["Noch im Lernen"],
                "samples_gesammelt": 0,
            }


# ---------------------------------------------------------------------------
# CLI-Einstieg und Cron-Kompatibilität
# ---------------------------------------------------------------------------

def main():
    logging.basicConfig(level=logging.INFO, format="%(asctime)s - %(name)s - %(levelname)s - %(message)s")
    account_id = os.getenv("STYLE_LEARNER_ACCOUNT", "default")
    learner = StyleLearner(account_id=account_id)
    ergebnis = learner.aktualisiere_stil_profil()
    logger.info("Style-Learner erfolgreich ausgeführt: profile_id=%s satzlaenge=%s", ergebnis["profile_id"], ergebnis["durchschnittliche_satzlaenge"])


if __name__ == "__main__":
    main()

# Täglicher Cron-Job (Cron-Zeile für Installation):
# 0 2 * * * cd /data/.openclaw/workspace-codex/projects/lexwolf/backend && python3 services/style_learner.py >> /data/.openclaw/workspace-codex/projects/lexwolf/logs/style_learner.log 2>&1
