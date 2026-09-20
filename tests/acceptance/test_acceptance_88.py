import glob
import os
import re
import socket
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', 'backend'))
_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DB_URL = "postgresql://postgres:postgres@localhost:5432/lexwolf"
LEARNER = os.path.join(_ROOT, 'backend', 'services', 'style_learner.py')


def pg_ok():
    s = socket.socket(); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


def test_style_learner_existiert():
    assert os.path.exists(LEARNER), "backend/services/style_learner.py fehlt"


def test_cron_konfiguration_vorhanden():
    kandidaten = glob.glob(os.path.join(_ROOT, '**', '*cron*'), recursive=True)
    kandidaten = [f for f in kandidaten if 'node_modules' not in f and 'venv' not in f]
    treffer = any('style_learner' in open(f, errors='ignore').read() for f in kandidaten if os.path.isfile(f))
    if not treffer and os.path.exists(LEARNER):
        treffer = bool(re.search(r"\d+\s+\d+\s+\*\s+\*\s+\*", open(LEARNER).read()))
    assert treffer, "Kein täglicher Cron-Job für style_learner.py gefunden"


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_stil_profil_wird_aus_feedback_table_aktualisiert():
    import psycopg2
    from services.style_learner import StyleLearner

    ergebnis = StyleLearner(db_url=DB_URL).aktualisiere_stil_profil()
    assert "durchschnittliche_satzlaenge" in ergebnis
    assert "formulierungs_praeferenzen" in ergebnis

    conn = psycopg2.connect(DB_URL)
    cur = conn.cursor()
    cur.execute("SELECT durchschnittliche_satzlaenge FROM style_profiles WHERE profile_id = %s", (ergebnis["profile_id"],))
    row = cur.fetchone()
    conn.close()
    assert row is not None, "Stil-Profil wurde nicht in style_profiles gespeichert"
