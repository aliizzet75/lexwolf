import json
import os
import re
import socket
import sys
import pytest

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
if os.path.isdir("/data/.openclaw/workspace-codex/projects/lexwolf"):
    _ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"
sys.path.insert(0, os.path.join(_ROOT, "backend"))

PAYLOAD_PATH = os.path.join(_ROOT, "desktop/test/sample_feedback_payload.json")
DOC_PATH = os.path.join(_ROOT, "docs/feedback_datenschutz.md")
NAME_PATTERN = re.compile(r"\b(Herr|Frau|Mandant(?:in)?|Kläger(?:in)?|Beklagte[r]?)\s+[A-ZÄÖÜ][a-zäöüß]+\b")
PARAGRAPH_PATTERN = re.compile(r"§\s?\d+[a-z]?")


def _pg_ok():
    s = socket.socket(); s.settimeout(2)
    r = s.connect_ex(("localhost", 5432)); s.close()
    return r == 0


def test_sample_payload_nur_statistische_felder():
    payload = json.load(open(PAYLOAD_PATH, encoding="utf-8"))
    assert set(payload.keys()) <= {"satzlaengen_verteilung", "wortklassen_haeufigkeiten", "korrektur_kategorien"}
    dump = json.dumps(payload, ensure_ascii=False)
    assert not NAME_PATTERN.search(dump), "Payload enthaelt Eigennamen-Muster"
    assert not PARAGRAPH_PATTERN.search(dump), "Payload enthaelt Paragraph-Referenzen"


def test_dsgvo_dokumentation_vorhanden():
    assert os.path.exists(DOC_PATH), "DSGVO-Dokumentation fehlt"
    text = open(DOC_PATH, encoding="utf-8").read()
    assert "satzlaengen_verteilung" in text and "wortklassen_haeufigkeiten" in text


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_metrik_feedback_speichert_keine_rohtexte():
    from services.feedback_service import FeedbackService
    from models import FeedbackEntry
    svc = FeedbackService()
    result = svc.process_metrics_feedback({
        "satzlaengen_verteilung": {"6-10": 2},
        "wortklassen_haeufigkeiten": {"noun_or_name": 3},
        "korrektur_kategorien": {"formulierung": 2},
    })
    db = svc.SessionLocal()
    eintrag = db.query(FeedbackEntry).filter_by(id=result["id"]).first()
    db.close()
    assert eintrag.original is None and eintrag.korrigiert is None
