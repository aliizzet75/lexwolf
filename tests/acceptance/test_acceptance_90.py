import os
import socket
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', 'backend'))
DB_URL = "postgresql://postgres:postgres@localhost:5432/lexwolf"


def pg_ok():
    s = socket.socket(); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_ab_test_flag_und_50_50_split_in_antwort_objekt():
    from services.style_learner import StyleLearner

    learner = StyleLearner(db_url=DB_URL, account_id="ab_test_precheck")
    varianten = [learner.waehle_ab_test_variante() for _ in range(200)]
    assert all(v.get("variante") in ("alt", "neu") for v in varianten), \
        "Antwort-Objekt muss ein A/B-Test-Flag mit Wert 'alt' oder 'neu' enthalten"
    anteil_neu = sum(1 for v in varianten if v["variante"] == "neu") / len(varianten)
    assert 0.35 <= anteil_neu <= 0.65, f"50/50 Split erwartet, war {anteil_neu}"


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_implizite_praeferenz_wird_getrackt_und_nach_20_samples_ausgewertet():
    from services.style_learner import StyleLearner

    learner = StyleLearner(db_url=DB_URL, account_id="ab_test_precheck")
    ergebnis = None
    for _ in range(20):
        antwort = learner.waehle_ab_test_variante()
        ergebnis = learner.tracke_ab_test_praeferenz(antwort["ab_test_id"], korrigiert=False)
    assert ergebnis is not None and ergebnis.get("ausgewertet") is True, \
        "Nach 20 Samples muss automatisch eine Auswertung erfolgen"
