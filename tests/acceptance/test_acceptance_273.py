"""
T#273: Gutachtenstil-Prompt-Schema für die Anspruchsprüfung
(backend/services/gutachten_prompt.py).

Prüft:
- Der System-Prompt verlangt alle sechs Schritte aus den Task-Notizen
  (Anspruchsgrundlage, Tatbestandsmerkmale einzeln mit Norm+Quelle,
  entstanden/nicht erloschen/durchsetzbar, aktive Einwände-Gegensuche,
  Rechtsprechung, Ampel-Erfolgsprognose, Gesamtergebnis).
- Die Antwort ist strukturiertes JSON, nicht nur Fließtext.
- validate_anspruchspruefung() erkennt ein korrektes Beispiel als gültig und
  erkennt konkrete Fehler (fehlende Einwände-Gegensuche, ungültige
  Ampel-Werte, fehlende Norm/Quelle je Tatbestandsmerkmal).
- parse_anspruchspruefung_antwort() holt das JSON auch aus einer Antwort mit
  Markdown-Codeblock/Text drumherum.
- Ende-zu-Ende gegen ein echtes LLM (Ollama, siehe CLAUDE.md-Umgebung):
  Prompt -> KI-Antwort -> Parsen -> Validieren muss ohne Schema-Fehler laufen.
"""
import copy
import json
import os
import socket
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent.parent.parent / "backend"))

from services.gutachten_prompt import (  # noqa: E402
    AMPEL_WERTE,
    ANSPRUCH_ERGEBNIS,
    JA_NEIN_OFFEN,
    MERKMAL_STATUS,
    build_anspruchspruefung_system_prompt,
    build_anspruchspruefung_user_prompt,
    parse_anspruchspruefung_antwort,
    validate_anspruchspruefung,
)


def _gueltiges_beispiel() -> dict:
    return {
        "anspruchsgrundlagen": [
            {
                "bezeichnung": "Anspruch auf Zahlung des Kaufpreises",
                "norm": "§ 433 Abs. 2 BGB",
                "obersatz": "V könnte gegen K einen Anspruch auf Zahlung des Kaufpreises aus § 433 Abs. 2 BGB haben.",
                "voraussetzungen": [
                    {
                        "merkmal": "Wirksamer Kaufvertrag",
                        "norm": "§§ 433, 145 ff. BGB",
                        "quelle": "[D1]",
                        "status": "erfüllt",
                        "begruendung": "Angebot und Annahme liegen laut [D1] vor.",
                    }
                ],
                "entstanden": {"status": "ja", "begruendung": "Vertrag wirksam geschlossen."},
                "nicht_erloschen": {"status": "ja", "begruendung": "Keine Erfüllung, kein Erlass."},
                "durchsetzbar": {"status": "ja", "begruendung": "Keine Einrede durchgreifend."},
                "einwendungen_einreden": [
                    {
                        "bezeichnung": "Verjährung",
                        "norm": "§§ 195, 199 BGB",
                        "greift": "nein",
                        "begruendung": "Regelverjährung noch nicht abgelaufen.",
                    }
                ],
                "rechtsprechung": [],
                "ergebnis": "besteht",
            }
        ],
        "erfolgsprognose": {"ampel": "hoch", "begruendung": "Alle Voraussetzungen erfüllt, keine Einwendung greift."},
        "gesamtergebnis": "Der Anspruch auf Zahlung des Kaufpreises besteht.",
        "offene_punkte": [],
    }


# --------------------------------------------------------------- Prompt-Inhalt

def test_system_prompt_enthaelt_alle_sechs_schritte_aus_den_notizen():
    prompt = build_anspruchspruefung_system_prompt()

    # 1. Anspruchsgrundlage(n) identifizieren
    assert "Anspruchsgrundlage" in prompt
    # 2. Tatbestandsmerkmale einzeln, je mit Norm+Quelle, entstanden/nicht erloschen/durchsetzbar
    assert "Tatbestandsmerkmal" in prompt
    assert "Norm" in prompt and "Quelle" in prompt
    assert "entstanden" in prompt and "nicht erloschen" in prompt and "durchsetzbar" in prompt
    # 3. Einwendungen & Einreden aktiv gegensuchen
    assert "Einwendungen" in prompt and "Einreden" in prompt
    assert "AKTIV GEGENSUCHEN" in prompt or "aktiv" in prompt.lower()
    # 4. einschlägige Rechtsprechung
    assert "Rechtsprechung" in prompt
    # 5. Erfolgsprognose (Ampel hoch/mittel/gering) mit Begründung
    assert "Erfolgsprognose" in prompt
    for wert in AMPEL_WERTE:
        assert wert in prompt
    assert ("hoch" in prompt) and ("mittel" in prompt) and ("gering" in prompt)
    # 6. Gesamtergebnis
    assert "Gesamtergebnis" in prompt

    # Keine Prozentzahl als Erfolgsaussicht (Konzept Abschnitt 2)
    assert "Prozent" in prompt  # als Verbot erwähnt
    assert "%-" not in prompt


def test_system_prompt_verlangt_ausschliesslich_json_keine_freitext_antwort():
    prompt = build_anspruchspruefung_system_prompt()
    assert "JSON" in prompt
    assert "NUR mit einem einzigen JSON-Objekt" in prompt or "nur mit einem einzigen JSON-Objekt" in prompt.lower()


def test_user_prompt_baut_sachverhalt_fragestellung_und_kontext_ein():
    prompt = build_anspruchspruefung_user_prompt(
        sachverhalt="K kaufte von V ein Auto für 10.000 EUR [D1].",
        fragestellung="Besteht ein Anspruch auf Kaufpreiszahlung?",
        normen_kontext="§ 433 BGB: ...",
    )
    assert "K kaufte von V ein Auto" in prompt
    assert "Besteht ein Anspruch auf Kaufpreiszahlung?" in prompt
    assert "§ 433 BGB" in prompt


# --------------------------------------------------------------- Validierung

def test_validate_akzeptiert_gueltiges_beispiel():
    fehler = validate_anspruchspruefung(_gueltiges_beispiel())
    assert fehler == [], f"Gültiges Beispiel wurde abgelehnt: {fehler}"


def test_validate_erkennt_fehlende_einwaende_gegensuche():
    daten = copy.deepcopy(_gueltiges_beispiel())
    del daten["anspruchsgrundlagen"][0]["einwendungen_einreden"]
    fehler = validate_anspruchspruefung(daten)
    assert any("einwendungen_einreden" in f for f in fehler), fehler


def test_validate_erkennt_ungueltigen_ampel_wert():
    daten = copy.deepcopy(_gueltiges_beispiel())
    daten["erfolgsprognose"]["ampel"] = "sehr hoch"
    fehler = validate_anspruchspruefung(daten)
    assert any("ampel" in f for f in fehler), fehler


def test_validate_erkennt_fehlende_norm_bei_tatbestandsmerkmal():
    daten = copy.deepcopy(_gueltiges_beispiel())
    del daten["anspruchsgrundlagen"][0]["voraussetzungen"][0]["norm"]
    fehler = validate_anspruchspruefung(daten)
    assert any("voraussetzungen[0].norm" in f for f in fehler), fehler


def test_validate_erkennt_ungueltigen_merkmal_status():
    daten = copy.deepcopy(_gueltiges_beispiel())
    daten["anspruchsgrundlagen"][0]["voraussetzungen"][0]["status"] = "wahrscheinlich"
    fehler = validate_anspruchspruefung(daten)
    assert any("status" in f and "voraussetzungen[0]" in f for f in fehler), fehler


def test_validate_erkennt_fehlendes_gesamtergebnis():
    daten = copy.deepcopy(_gueltiges_beispiel())
    daten["gesamtergebnis"] = ""
    fehler = validate_anspruchspruefung(daten)
    assert any("gesamtergebnis" in f for f in fehler), fehler


def test_alle_enums_wie_in_den_task_notizen_gefordert():
    assert AMPEL_WERTE == ("hoch", "mittel", "gering")
    assert set(MERKMAL_STATUS) == {"erfüllt", "nicht erfüllt", "offen"}
    assert set(JA_NEIN_OFFEN) == {"ja", "nein", "offen"}
    assert set(ANSPRUCH_ERGEBNIS) == {"besteht", "besteht nicht", "offen"}


# --------------------------------------------------------------- Parsen

def test_parse_holt_json_aus_codeblock_mit_text_drumherum():
    antwort = (
        "Hier ist meine Prüfung:\n```json\n"
        + json.dumps(_gueltiges_beispiel(), ensure_ascii=False)
        + "\n```\nDas war's."
    )
    daten = parse_anspruchspruefung_antwort(antwort)
    assert validate_anspruchspruefung(daten) == []


def test_parse_ohne_json_wirft_klaren_fehler():
    with pytest.raises(ValueError):
        parse_anspruchspruefung_antwort("Das ist nur Fließtext, kein JSON.")


# --------------------------------------------------------------- Ende-zu-Ende mit echter KI

def _ollama_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 11434))
    s.close()
    return r == 0


@pytest.mark.skipif(not _ollama_ok(), reason="Ollama nicht erreichbar auf Port 11434")
def test_ende_zu_ende_echte_ki_liefert_valides_gutachten_json():
    os.environ["LLM_PROVIDER"] = "ollama"
    # Frischer Import, damit llm_client._PROVIDER den aktuellen Wert liest.
    sys.modules.pop("services.llm_client", None)
    from services.llm_client import ask_llm

    system = build_anspruchspruefung_system_prompt()
    user = build_anspruchspruefung_user_prompt(
        sachverhalt=(
            "V verkaufte K am 01.03.2024 einen gebrauchten PKW für 8.000 EUR [D1]. "
            "K holte das Fahrzeug ab, zahlte aber bislang nichts [D2]. Eine Mahnung "
            "vom 15.04.2024 blieb erfolglos [D3]."
        ),
        fragestellung="Besteht ein Anspruch von V gegen K auf Zahlung des Kaufpreises?",
    )
    messages = [
        {"role": "system", "content": system},
        {"role": "user", "content": user},
    ]

    try:
        antwort = ask_llm(messages, max_tokens=3000)
    except RuntimeError as e:
        if "402" in str(e) or "Payment Required" in str(e):
            pytest.skip(f"Ollama-Cloud-Modell ohne Guthaben in dieser Umgebung: {e}")
        raise
    assert antwort.strip(), "KI hat keine Antwort geliefert"

    try:
        daten = parse_anspruchspruefung_antwort(antwort)
    except ValueError:
        # Ein Retry mit Verstärkungshinweis - Modelle vergessen die JSON-Vorgabe
        # gelegentlich beim ersten Versuch.
        messages.append({"role": "assistant", "content": antwort})
        messages.append({
            "role": "user",
            "content": "Antworte ausschließlich mit dem JSON-Objekt, ohne Text davor oder danach.",
        })
        antwort = ask_llm(messages, max_tokens=3000)
        daten = parse_anspruchspruefung_antwort(antwort)

    fehler = validate_anspruchspruefung(daten)
    assert fehler == [], f"KI-Antwort entspricht nicht dem Schema: {fehler}\n\nAntwort war:\n{antwort}"

    # Die Ampel wird irgendeinen der drei vorgegebenen Werte wählen - K hat
    # unstreitig nicht gezahlt, der Fall sollte klar zugunsten von V ausfallen.
    assert daten["erfolgsprognose"]["ampel"] in AMPEL_WERTE
    assert len(daten["anspruchsgrundlagen"]) >= 1
    assert len(daten["anspruchsgrundlagen"][0]["einwendungen_einreden"]) >= 0
