import os
import socket
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', 'backend'))
DB_URL = "postgresql://postgres:postgres@localhost:5432/lexwolf"


def pg_ok():
    s = socket.socket(); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_separate_stil_profile_pro_schriftsatz_typ():
    import psycopg2
    from services.style_learner import StyleLearner

    klage = StyleLearner(db_url=DB_URL, account_id="typ_precheck", schriftsatz_typ="klage")
    brief = StyleLearner(db_url=DB_URL, account_id="typ_precheck", schriftsatz_typ="brief")
    assert klage.profile_id != brief.profile_id, "Klage und Brief muessen getrennte Profile besitzen"

    klage.aktualisiere_stil_profil()
    brief.aktualisiere_stil_profil()

    conn = psycopg2.connect(DB_URL)
    cur = conn.cursor()
    cur.execute("SELECT profile_id FROM style_profiles WHERE profile_id IN (%s, %s)", (klage.profile_id, brief.profile_id))
    gefunden = {row[0] for row in cur.fetchall()}
    conn.close()
    assert gefunden == {klage.profile_id, brief.profile_id}, "Beide Typ-Profile muessen persistiert sein"


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_fallback_auf_generisches_profil_bei_unbekanntem_typ():
    from services.style_learner import StyleLearner

    generisch = StyleLearner(db_url=DB_URL, account_id="typ_precheck")
    unbekannt = StyleLearner(db_url=DB_URL, account_id="typ_precheck", schriftsatz_typ="voellig_unbekannter_typ_xyz")
    assert unbekannt.profile_id == generisch.profile_id, \
        "Unbekannter Schriftsatz-Typ muss auf generisches Profil zurueckfallen"
