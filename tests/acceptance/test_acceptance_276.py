"""
T#276: Ergebnis-Editor: strukturierte Blöcke pro Voraussetzung/Einwand
(desktop/confidence_coloring.py: SourceLink, ResultBlock, build_result_blocks).

Erweitert das bestehende 3-Farben-System (grün=verifiziert, gelb=niedrige
Konfidenz, rot=nicht in DB), das bislang nur auf Satz-Ebene (TextSegment)
existierte, auf Abschnitts-Ebene: je ein Block pro Anspruchsvoraussetzung
und pro Einwand/Einrede aus dem Gutachten-JSON
(backend/services/gutachten_prompt.SCHEMA_BESCHREIBUNG), mit eigenem Status
und klickbarer Quellenverknüpfung.

Prüft:
- Die bestehende Satz-Ebene-Komponente (ConfidenceColorizer.score_to_color,
  TextSegment) ist durch die Erweiterung nicht kaputt gegangen.
- status_to_color bildet exakt die drei Farben korrekt ab.
- build_result_blocks erzeugt genau einen Block pro Voraussetzung und pro
  Einwand, über mehrere Anspruchsgrundlagen hinweg, mit korrekter
  Klickbarkeit der Quelle (Norm bzw. Aktenbeleg [Dn]).
- Ende-zu-Ende gegen die ECHTE Postgres-DB (legal_chunks): eine tatsächlich
  vorhandene Norm wird grün, eine erfundene Norm wird rot eingefärbt.
"""
import copy
import os
import socket
import sys
from pathlib import Path

import pytest

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST

sys.path.insert(0, os.path.join(_ROOT, "desktop"))
sys.path.insert(0, os.path.join(_ROOT, "backend"))

os.environ.setdefault("PG_DSN", "host=localhost port=5432 dbname=lexwolf user=postgres password=postgres")

from confidence_coloring import (  # noqa: E402
    ConfidenceColorizer,
    ResultBlock,
    SourceLink,
    TextSegment,
    build_result_blocks,
)


def _pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


def _gutachten_mit_zwei_anspruchsgrundlagen() -> dict:
    """Zwei Anspruchsgrundlagen mit zusammen 3 Voraussetzungen und 2
    Einwänden, bewusst mit unterschiedlichen Quellentypen (Gesetzestext,
    Aktenbeleg [Dn], Entscheidung ohne Norm) wie im Gutachten-Schema
    vorgesehen."""
    return {
        "anspruchsgrundlagen": [
            {
                "bezeichnung": "Anspruch auf Zahlung des Kaufpreises",
                "norm": "§ 433 Abs. 2 BGB",
                "obersatz": "V könnte gegen K einen Anspruch haben.",
                "voraussetzungen": [
                    {
                        "merkmal": "Wirksamer Kaufvertrag",
                        "norm": "§ 433 BGB",
                        "quelle": "Gesetzestext",
                        "status": "erfüllt",
                        "begruendung": "Angebot und Annahme liegen vor.",
                    },
                    {
                        "merkmal": "Übergabe erfolgt",
                        "norm": "",
                        "quelle": "Aktenbeleg [D2]",
                        "status": "erfüllt",
                        "begruendung": "K holte das Fahrzeug laut [D2] ab.",
                    },
                ],
                "entstanden": {"status": "ja", "begruendung": "Vertrag wirksam."},
                "nicht_erloschen": {"status": "ja", "begruendung": "Keine Erfüllung."},
                "durchsetzbar": {"status": "ja", "begruendung": "Keine Einrede durchgreifend."},
                "einwendungen_einreden": [
                    {
                        "bezeichnung": "Verjährung",
                        "norm": "§ 626 BGB",
                        "greift": "nein",
                        "begruendung": "Regelverjährung noch nicht abgelaufen.",
                    },
                ],
                "rechtsprechung": [],
                "ergebnis": "besteht",
            },
            {
                "bezeichnung": "Anspruch auf Schadensersatz",
                "norm": "§ 823 Abs. 1 BGB",
                "obersatz": "K könnte gegen V einen Anspruch haben.",
                "voraussetzungen": [
                    {
                        "merkmal": "Rechtsgutsverletzung",
                        "norm": "§ 9999 Erfundenes Gesetz",
                        "quelle": "Gesetzestext",
                        "status": "offen",
                        "begruendung": "Unklar, ob tatsächlich eine Verletzung vorliegt.",
                    },
                ],
                "entstanden": {"status": "offen", "begruendung": "Noch zu prüfen."},
                "nicht_erloschen": {"status": "ja", "begruendung": "-"},
                "durchsetzbar": {"status": "offen", "begruendung": "-"},
                "einwendungen_einreden": [
                    {
                        "bezeichnung": "Mitverschulden",
                        "norm": "§ 9998 Erfundene Norm",
                        "greift": "offen",
                        "begruendung": "Noch zu klären.",
                    },
                ],
                "rechtsprechung": [],
                "ergebnis": "offen",
            },
        ],
        "erfolgsprognose": {"ampel": "mittel", "begruendung": "Teilweise offen."},
        "gesamtergebnis": "Ein Anspruch besteht, der zweite ist noch offen.",
        "offene_punkte": [],
    }


# ------------------------------------------- Rückwärtskompatibilität Satz-Ebene

def test_bestehende_satz_ebene_funktioniert_weiterhin():
    assert ConfidenceColorizer.score_to_color(0.9) == "green"
    assert ConfidenceColorizer.score_to_color(0.7) == "yellow"
    assert ConfidenceColorizer.score_to_color(0.3) == "red"
    seg = TextSegment(text="Der Beklagte", confidence_score=0.85, source="GPT-4")
    assert seg.color == "green"


# ------------------------------------------------------- status_to_color

def test_status_to_color_bildet_alle_drei_farben_ab():
    assert ConfidenceColorizer.status_to_color("geprüft") == "green"
    assert ConfidenceColorizer.status_to_color("nicht_in_datenbank") == "red"
    assert ConfidenceColorizer.status_to_color("nicht_pruefbar") == "yellow"


# ------------------------------------------------------------- SourceLink

def test_source_link_mit_target_ist_klickbar():
    link = SourceLink(label="§ 433 BGB", status="geprüft", target="§ 433 BGB")
    assert link.klickbar is True
    assert link.color == "green"


def test_source_link_ohne_target_ist_nicht_klickbar():
    link = SourceLink(label="Entscheidung ohne Aktenzeichen", status="nicht_pruefbar", target=None)
    assert link.klickbar is False
    assert link.color == "yellow"


# ------------------------------------------------------ build_result_blocks

def test_build_result_blocks_erzeugt_einen_block_pro_voraussetzung_und_einwand():
    ergebnis = _gutachten_mit_zwei_anspruchsgrundlagen()
    blocks = build_result_blocks(ergebnis, normen_pruefung=None)

    voraussetzungs_bloecke = [b for b in blocks if b.kind == "voraussetzung"]
    einwand_bloecke = [b for b in blocks if b.kind == "einwand"]

    # 2 Voraussetzungen in AGL 1 + 1 in AGL 2 = 3
    assert len(voraussetzungs_bloecke) == 3
    # 1 Einwand in AGL 1 + 1 in AGL 2 = 2
    assert len(einwand_bloecke) == 2
    assert all(isinstance(b, ResultBlock) for b in blocks)


def test_aktenbeleg_quelle_ist_klickbar_aber_nicht_pruefbar():
    ergebnis = _gutachten_mit_zwei_anspruchsgrundlagen()
    blocks = build_result_blocks(ergebnis, normen_pruefung=None)
    uebergabe = next(b for b in blocks if b.titel == "Übergabe erfolgt")
    assert uebergabe.quelle.target == "D2"
    assert uebergabe.quelle.klickbar is True
    assert uebergabe.color == "yellow"  # nicht gegen Normen-DB prüfbar


def test_ohne_normen_pruefung_ist_alles_gelb_statt_falsch_verifiziert():
    """Ohne echte DB-Prüfung darf NICHTS als grün (verifiziert) oder rot
    (nicht in DB) erscheinen — das wäre eine falsche Gewissheit."""
    ergebnis = _gutachten_mit_zwei_anspruchsgrundlagen()
    blocks = build_result_blocks(ergebnis, normen_pruefung=None)
    norm_bloecke = [b for b in blocks if b.norm]
    assert norm_bloecke, "Testdaten müssten Normen enthalten"
    assert all(b.color == "yellow" for b in norm_bloecke)


def test_block_mit_norm_gefunden_in_db_simulation_ist_gruen():
    ergebnis = _gutachten_mit_zwei_anspruchsgrundlagen()
    normen_pruefung = {"gesamt": 1, "existieren": 1, "fehlen": 0, "fehlende_normen": []}
    blocks = build_result_blocks(ergebnis, normen_pruefung=normen_pruefung)
    kaufvertrag = next(b for b in blocks if b.titel == "Wirksamer Kaufvertrag")
    assert kaufvertrag.color == "green"
    assert kaufvertrag.quelle.target == "§ 433 BGB"


def test_block_mit_norm_fehlend_in_db_simulation_ist_rot():
    ergebnis = _gutachten_mit_zwei_anspruchsgrundlagen()
    normen_pruefung = {
        "gesamt": 1, "existieren": 0, "fehlen": 1,
        "fehlende_normen": ["§ 9999 Erfundenes Gesetz"],
    }
    blocks = build_result_blocks(ergebnis, normen_pruefung=normen_pruefung)
    rechtsgut = next(b for b in blocks if b.titel == "Rechtsgutsverletzung")
    assert rechtsgut.color == "red"


def test_db_pruefung_fehlgeschlagen_fuehrt_zu_gelb_nicht_rot():
    """Wenn pruefe_normen_in_db() selbst fehlschlägt (Schlüssel "fehler"),
    darf das NICHT als "nicht in Datenbank" (rot) missverstanden werden —
    sonst würde ein DB-Ausfall dem Anwalt eine falsche, zu negative
    Information suggerieren."""
    ergebnis = _gutachten_mit_zwei_anspruchsgrundlagen()
    normen_pruefung = {"gesamt": 1, "existieren": 0, "fehlen": 1, "fehlende_normen": ["§ 433 BGB"], "fehler": "Verbindung fehlgeschlagen"}
    blocks = build_result_blocks(ergebnis, normen_pruefung=normen_pruefung)
    kaufvertrag = next(b for b in blocks if b.titel == "Wirksamer Kaufvertrag")
    assert kaufvertrag.color == "yellow"


def test_leeres_ergebnis_liefert_leere_liste():
    assert build_result_blocks({"anspruchsgrundlagen": []}) == []
    assert build_result_blocks({}) == []


def test_tooltip_enthaelt_quelle_und_status():
    ergebnis = _gutachten_mit_zwei_anspruchsgrundlagen()
    blocks = build_result_blocks(ergebnis, normen_pruefung=None)
    kaufvertrag = next(b for b in blocks if b.titel == "Wirksamer Kaufvertrag")
    tooltip = kaufvertrag.get_tooltip()
    assert "Gesetzestext" in tooltip
    assert "nicht_pruefbar" in tooltip


def test_editierbarkeit_bleibt_moeglich_wie_bei_textsegment():
    """Analog zu TextSegment (T#79): der Anwalt muss Felder eines Blocks
    nach der KI-Generierung noch ändern können (Begründung nachbessern)."""
    ergebnis = _gutachten_mit_zwei_anspruchsgrundlagen()
    block = build_result_blocks(ergebnis, normen_pruefung=None)[0]
    block.begruendung = "Vom Anwalt überarbeitete Begründung."
    assert block.begruendung == "Vom Anwalt überarbeitete Begründung."


# -------------------------------------------- Ende-zu-Ende mit echter DB

@pytest.mark.skipif(not _pg_ok(), reason="Postgres nicht erreichbar auf Port 5432")
def test_ende_zu_ende_echte_db_vorhandene_norm_gruen_erfundene_norm_rot():
    """Baut den vollständigen Pfad nach: Gutachten-JSON ->
    gutachten_quality.pruefe_normen_in_db() gegen die ECHTE Postgres-DB
    (legal_chunks) -> build_result_blocks(). § 626 BGB ist nachweislich in
    der DB vorhanden (siehe Fristlose-Kündigung-Chunks), § 9999/§ 9998 sind
    frei erfunden und dürfen dort nicht auftauchen."""
    from services.gutachten_quality import pruefe_normen_in_db

    ergebnis = _gutachten_mit_zwei_anspruchsgrundlagen()
    normen_pruefung = pruefe_normen_in_db(ergebnis)

    assert "fehler" not in normen_pruefung, f"DB-Verbindung fehlgeschlagen: {normen_pruefung}"
    assert "§ 626 BGB" not in normen_pruefung["fehlende_normen"], (
        "§ 626 BGB ist in legal_chunks vorhanden, darf nicht als fehlend markiert werden"
    )
    assert "§ 9999 Erfundenes Gesetz" in normen_pruefung["fehlende_normen"]
    assert "§ 9998 Erfundene Norm" in normen_pruefung["fehlende_normen"]

    blocks = build_result_blocks(ergebnis, normen_pruefung=normen_pruefung)

    verjaehrung = next(b for b in blocks if b.titel == "Verjährung")
    assert verjaehrung.kind == "einwand"
    assert verjaehrung.color == "green", f"§ 626 BGB ist in der DB, Block sollte grün sein: {verjaehrung}"
    assert verjaehrung.quelle.klickbar is True

    rechtsgut = next(b for b in blocks if b.titel == "Rechtsgutsverletzung")
    assert rechtsgut.color == "red", f"§ 9999 ist erfunden, Block sollte rot sein: {rechtsgut}"

    mitverschulden = next(b for b in blocks if b.titel == "Mitverschulden")
    assert mitverschulden.color == "red", f"§ 9998 ist erfunden, Block sollte rot sein: {mitverschulden}"

    kaufvertrag = next(b for b in blocks if b.titel == "Wirksamer Kaufvertrag")
    assert kaufvertrag.color == "green", f"§ 433 BGB ist in der DB, Block sollte grün sein: {kaufvertrag}"
