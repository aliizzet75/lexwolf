import asyncio
import os
import socket
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', 'backend'))
DB_URL = "postgresql://postgres:postgres@localhost:5432/lexwolf"


def pg_ok():
    s = socket.socket(); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


def _strukturiert():
    return {
        "fristen": [{"datum": "15.06.2026", "beschreibung": "Widerspruch einlegen"}],
        "aufgaben": [{"was": "Unterlagen prüfen", "bis_wann": "10.06.2026"}],
        "themen": ["Kündigung"],
        "naechste_schritte": ["Widerspruch vorbereiten"],
    }


def test_generate_suggestions_liefert_mindestens_zwei_vorschlags_typen():
    from services.conversation_assistant import ConversationAssistant

    ca = ConversationAssistant()
    vorschlaege = ca.generate_suggestions(_strukturiert(), mandant_history=["Letztes Gespräch: Kündigung erhalten"])

    assert isinstance(vorschlaege, list)
    assert len(vorschlaege) >= 2, "generate_suggestions() muss mindestens 2 Vorschlags-Typen liefern"

    typen = {v["typ"] for v in vorschlaege}
    assert "fristennotiz" in typen, "Fristennotiz-Vorschlag fehlt"
    assert "email_entwurf" in typen, "E-Mail-Entwurf-Vorschlag fehlt"

    for v in vorschlaege:
        assert v.get("text"), f"Vorschlag {v.get('typ')} hat keinen Text"
        assert v.get("titel")


def test_generate_suggestions_ohne_fristen_liefert_trotzdem_email_entwurf():
    from services.conversation_assistant import ConversationAssistant

    ca = ConversationAssistant()
    strukturiert = {"fristen": [], "aufgaben": [], "themen": ["Allgemein"], "naechste_schritte": []}
    vorschlaege = ca.generate_suggestions(strukturiert)

    typen = {v["typ"] for v in vorschlaege}
    assert "email_entwurf" in typen
    assert "fristennotiz" not in typen, "Ohne erkannte Fristen soll keine Fristennotiz vorgeschlagen werden"


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_entscheidung_ja_nein_geaendert_fliesst_in_style_learner():
    """DoD: Die Anwalt-Entscheidung (Ja/Nein/Geändert) muss in style_learner einfließen."""
    from services.style_learner import StyleLearner

    account_id = "test_acceptance_131_style"
    learner = StyleLearner(db_url=DB_URL, account_id=account_id)

    learner.tracke_vorschlag_entscheidung("fristennotiz", "ja")
    learner.tracke_vorschlag_entscheidung("fristennotiz", "nein")
    ergebnis = learner.tracke_vorschlag_entscheidung("fristennotiz", "geaendert")

    assert ergebnis["zaehler"]["ja"] >= 1
    assert ergebnis["zaehler"]["nein"] >= 1
    assert ergebnis["zaehler"]["geaendert"] >= 1

    gespeichert = learner.hole_vorschlag_feedback()
    assert gespeichert["fristennotiz"]["ja"] >= 1
    assert gespeichert["fristennotiz"]["nein"] >= 1
    assert gespeichert["fristennotiz"]["geaendert"] >= 1


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_tracke_vorschlag_entscheidung_lehnt_unbekannte_entscheidung_ab():
    from services.style_learner import StyleLearner

    learner = StyleLearner(db_url=DB_URL, account_id="test_acceptance_131_invalid")
    with pytest.raises(ValueError):
        learner.tracke_vorschlag_entscheidung("fristennotiz", "vielleicht")


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_entscheidung_geaendert_fliesst_zusaetzlich_als_korrektur_in_feedback_service():
    """DoD: Bei 'geändert' muss der echte Korrektur-Diff in feedback_service/feedback_table landen
    (gleiche Lernschleife wie reguläre Anwalts-Korrekturen), NICHT separat gespeichert."""
    from services.feedback_service import FeedbackService

    fs = FeedbackService(db_url=DB_URL)
    vorschlag_text = "Sehr geehrter Mandant, bitte melden Sie sich."
    geaenderter_text = "Bitte melden Sie sich bis Freitag dringend zurück und reichen Sie alle Unterlagen ein."

    eintrag = fs.process_feedback({"original": vorschlag_text, "korrigiert": geaenderter_text})

    assert eintrag["id"] is not None
    assert eintrag["kategorie"] in ("formulierung", "satzstruktur", "paragraph", "laenge")


@pytest.mark.skipif(not pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_api_endpoints_end_to_end_suggestions_und_decision():
    """End-to-End über die FastAPI-Route-Handler: generate_suggestions -> Entscheidung
    -> style_learner (immer) + feedback_service (nur bei 'geaendert')."""
    from api.conversation import (
        generate_suggestions,
        submit_suggestion_decision,
        SuggestionsRequest,
        SuggestionDecisionRequest,
    )
    from fastapi import HTTPException

    async def _run():
        req = SuggestionsRequest(strukturiert=_strukturiert(), mandant_history=["Erstgespräch zu Kündigung"])
        resp = await generate_suggestions(req)
        assert len(resp.vorschlaege) >= 2

        # "geaendert" ohne geaenderter_text -> 400
        with pytest.raises(HTTPException) as exc:
            await submit_suggestion_decision(SuggestionDecisionRequest(
                vorschlag_typ="fristennotiz", vorschlag_text="x", entscheidung="geaendert",
            ))
        assert exc.value.status_code == 400

        # "ja" -> nur style_learner, kein feedback_entry
        r_ja = await submit_suggestion_decision(SuggestionDecisionRequest(
            vorschlag_typ="fristennotiz", vorschlag_text=resp.vorschlaege[0].text,
            entscheidung="ja", account_id="test_acceptance_131_api",
        ))
        assert r_ja["feedback_entry"] is None
        assert r_ja["style_profile"]["zaehler"]["ja"] >= 1

        # "geaendert" -> style_learner UND feedback_service
        r_ge = await submit_suggestion_decision(SuggestionDecisionRequest(
            vorschlag_typ="email_entwurf", vorschlag_text=resp.vorschlaege[1].text,
            entscheidung="geaendert",
            geaenderter_text="Bitte reichen Sie die Unterlagen bis Freitag vollständig und dringend ein.",
            account_id="test_acceptance_131_api",
        ))
        assert r_ge["feedback_entry"] is not None
        assert r_ge["style_profile"]["zaehler"]["geaendert"] >= 1

    asyncio.run(_run())
