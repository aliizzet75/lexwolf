"""
Konfidenz-Farbkodierung für den Rich-Text-Editor.
Segmente werden nach Konfidenz-Score eingefärbt:
  > 0.8  → grün   (sicher)
  0.5–0.8 → gelb   (unsicher)
  < 0.5  → rot    (niedrige Konfidenz)

T#276 ("Ergebnis-Editor: strukturierte Blöcke pro Voraussetzung/Einwand")
erweitert dasselbe 3-Farben-System von der Satz-Ebene (TextSegment) auf die
Abschnitts-Ebene: ResultBlock bildet je einen Block pro Anspruchsvoraussetzung
bzw. pro Einwand aus dem Gutachten-JSON (siehe
backend/services/gutachten_prompt.py, SCHEMA_BESCHREIBUNG) mit eigenem Status
und klickbarer Quellenverknüpfung (SourceLink) ab. Es ist bewusst dieselbe
Komponente, nicht ein neues UI-Pattern: beide Ebenen nutzen dieselben drei
Farben und dieselbe ConfidenceColorizer-Logik, nur der Status-Vokabular der
Abschnitts-Ebene (geprüft/nicht_in_datenbank/nicht_pruefbar statt eines
rohen Scores) orientiert sich am bereits im Gutachten-Schema verwendeten
Rechtsprechungs-Status ("geprüft" | "nicht in Datenbank" | "nicht prüfbar"),
damit beide Teile des Schemas konsistent eingefärbt werden.

Bleibt bewusst frei von schweren Imports (keine DB, kein LLM-SDK) wie das
ursprüngliche Modul — die DB-Normprüfung passiert in
backend/services/gutachten_quality.py (pruefe_normen_in_db); dieses Modul
nimmt nur deren Ergebnis entgegen und färbt ein.
"""
import re
from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class TextSegment:
    text: str
    confidence_score: float
    source: str
    color: Optional[str] = field(default=None, init=False)

    def __post_init__(self):
        self.color = ConfidenceColorizer.score_to_color(self.confidence_score)


class ConfidenceColorizer:
    COLOR_GREEN = "green"
    COLOR_YELLOW = "yellow"
    COLOR_RED = "red"

    # Quellen-Status auf Abschnitts-Ebene, analog zum "rechtsprechung[].status"
    # Vokabular aus gutachten_prompt.SCHEMA_BESCHREIBUNG — bewusst dieselben
    # drei Werte, damit Voraussetzungen/Einwände und Rechtsprechung im selben
    # Gutachten konsistent eingefärbt werden können.
    STATUS_GEPRUEFT = "geprüft"
    STATUS_NICHT_IN_DB = "nicht_in_datenbank"
    STATUS_NICHT_PRUEFBAR = "nicht_pruefbar"

    @staticmethod
    def score_to_color(score: float) -> str:
        if score > 0.8:
            return ConfidenceColorizer.COLOR_GREEN
        elif score >= 0.5:
            return ConfidenceColorizer.COLOR_YELLOW
        else:
            return ConfidenceColorizer.COLOR_RED

    @staticmethod
    def status_to_color(status: str) -> str:
        """Abschnitts-Ebene: grün=verifiziert (geprüft, in DB gefunden),
        gelb=niedrige Konfidenz (nicht prüfbar, z.B. reiner Aktenbeleg),
        rot=nicht in DB (geprüft, aber nicht gefunden)."""
        if status == ConfidenceColorizer.STATUS_GEPRUEFT:
            return ConfidenceColorizer.COLOR_GREEN
        elif status == ConfidenceColorizer.STATUS_NICHT_IN_DB:
            return ConfidenceColorizer.COLOR_RED
        else:
            return ConfidenceColorizer.COLOR_YELLOW

    @staticmethod
    def get_tooltip(segment: "TextSegment") -> str:
        pct = round(segment.confidence_score * 100, 1)
        return f"Konfidenz: {pct}% | Quelle: {segment.source}"

    def colorize(self, segments: List[TextSegment]) -> List[TextSegment]:
        for seg in segments:
            seg.color = self.score_to_color(seg.confidence_score)
        return segments


@dataclass
class SourceLink:
    """Quellenverknüpfung eines ResultBlock. `target` ist das klickbare Ziel
    (Norm-String für die Normsuche, Aktenbeleg-Anker wie 'D2' zum Sprung an
    die Fundstelle in der Akte, oder None, wenn keine Verknüpfung möglich
    ist). `status` ist eines der ConfidenceColorizer.STATUS_*-Werte."""
    label: str
    status: str
    target: Optional[str] = None
    color: Optional[str] = field(default=None, init=False)

    def __post_init__(self):
        self.color = ConfidenceColorizer.status_to_color(self.status)

    @property
    def klickbar(self) -> bool:
        return bool(self.target)


@dataclass
class ResultBlock:
    """Ein Block des Ergebnis-Editors (T#276) für GENAU eine
    Anspruchsvoraussetzung oder GENAU einen Einwand/Einrede — die
    Abschnitts-Ebene des 3-Farben-Systems, das bislang nur auf Satz-Ebene
    (TextSegment) existierte.

    `status` ist der fachliche Status aus dem Gutachten-Schema (für
    Voraussetzungen: "erfüllt"/"nicht erfüllt"/"offen"; für Einwände, ob die
    Einrede greift: "ja"/"nein"/"offen"). Die Blockfarbe selbst bildet NICHT
    diesen fachlichen Status ab, sondern — wie beim bestehenden
    Satz-Ebene-System — wie gut die zugrunde liegende Quelle verifiziert ist
    (quelle.color)."""
    kind: str  # "voraussetzung" | "einwand"
    titel: str
    norm: str
    status: str
    begruendung: str
    quelle: SourceLink
    color: Optional[str] = field(default=None, init=False)

    def __post_init__(self):
        self.color = self.quelle.color

    def get_tooltip(self) -> str:
        return f"Quelle: {self.quelle.label} | Status: {self.quelle.status}"


_AKTENBELEG_RE = re.compile(r"\[D(\d+)")


def _quelle_status_fuer_norm(norm: str, normen_pruefung: Optional[dict]) -> str:
    """Bildet eine Norm auf den Quellen-Status ab, basierend auf dem
    Ergebnis von backend.services.gutachten_quality.pruefe_normen_in_db()
    (Dict mit u.a. "fehlende_normen"). Ohne Normprüfung (None) oder wenn die
    Prüfung selbst fehlgeschlagen ist ("fehler"-Schlüssel gesetzt), ist die
    Norm nicht prüfbar — NICHT automatisch "nicht in Datenbank", das wäre
    eine falsche Gewissheit."""
    if not norm:
        return ConfidenceColorizer.STATUS_NICHT_PRUEFBAR
    if normen_pruefung is None or normen_pruefung.get("fehler"):
        return ConfidenceColorizer.STATUS_NICHT_PRUEFBAR
    if norm in (normen_pruefung.get("fehlende_normen") or []):
        return ConfidenceColorizer.STATUS_NICHT_IN_DB
    return ConfidenceColorizer.STATUS_GEPRUEFT


def _quelle_fuer_voraussetzung(v: dict, normen_pruefung: Optional[dict]) -> SourceLink:
    norm = str(v.get("norm") or "").strip()
    quelle_text = str(v.get("quelle") or "").strip()
    label = quelle_text or norm or "unbekannt"

    beleg = _AKTENBELEG_RE.search(quelle_text)
    if beleg:
        # Aktenbeleg [Dn]: Fundstelle im Mandats-Dokument, nicht gegen die
        # Normen-Datenbank prüfbar — klickbar zum Sprung an die Stelle in
        # der Akte, aber Farbe "nicht prüfbar" statt einer falschen Gewissheit.
        return SourceLink(label=label, status=ConfidenceColorizer.STATUS_NICHT_PRUEFBAR, target=f"D{beleg.group(1)}")

    if "entscheidung" in quelle_text.lower() and not norm:
        # Rechtsprechungsverweis ohne Norm: eigener Status-Zweig im Schema
        # (anspruchsgrundlagen[].rechtsprechung), hier nur als Verweis ohne
        # Zielbezug prüfbar.
        return SourceLink(label=label, status=ConfidenceColorizer.STATUS_NICHT_PRUEFBAR, target=None)

    status = _quelle_status_fuer_norm(norm, normen_pruefung)
    return SourceLink(label=label, status=status, target=norm or None)


def build_result_blocks(ergebnis: dict, normen_pruefung: Optional[dict] = None) -> List[ResultBlock]:
    """Baut aus dem Gutachten-Ergebnis-JSON (Schema aus
    backend/services/gutachten_prompt.SCHEMA_BESCHREIBUNG) je einen
    ResultBlock pro Anspruchsvoraussetzung und pro Einwand/Einrede, über
    ALLE Anspruchsgrundlagen hinweg — das ist die Abschnitts-Granularität,
    die der Ergebnis-Editor (T#276) statt reinem Fließtext anzeigen soll.

    `normen_pruefung` ist optional das Ergebnis von
    gutachten_quality.pruefe_normen_in_db() gegen die echte DB; ohne dieses
    Argument werden alle Normen als "nicht prüfbar" (gelb) markiert statt
    fälschlich als verifiziert oder fehlend."""
    blocks: List[ResultBlock] = []
    for agl in ergebnis.get("anspruchsgrundlagen") or []:
        if not isinstance(agl, dict):
            continue

        for v in agl.get("voraussetzungen") or []:
            if not isinstance(v, dict):
                continue
            blocks.append(ResultBlock(
                kind="voraussetzung",
                titel=str(v.get("merkmal") or ""),
                norm=str(v.get("norm") or ""),
                status=str(v.get("status") or "offen"),
                begruendung=str(v.get("begruendung") or ""),
                quelle=_quelle_fuer_voraussetzung(v, normen_pruefung),
            ))

        for e in agl.get("einwendungen_einreden") or []:
            if not isinstance(e, dict):
                continue
            norm = str(e.get("norm") or "").strip()
            status = _quelle_status_fuer_norm(norm, normen_pruefung)
            blocks.append(ResultBlock(
                kind="einwand",
                titel=str(e.get("bezeichnung") or ""),
                norm=norm,
                status=str(e.get("greift") or "offen"),
                begruendung=str(e.get("begruendung") or ""),
                quelle=SourceLink(label=norm or str(e.get("bezeichnung") or ""), status=status, target=norm or None),
            ))

    return blocks
