"""LLM-Judge: bewertet juristische Antworten auf Skala 1-5.

Priority: Anthropic Claude (Haiku) → rule-based fallback.
Ollama is NOT used — removed to fix 'No API provider registered for api: ollama' errors.
"""
import json
import logging
import os
import re
from typing import Dict, List

logger = logging.getLogger(__name__)

JUDGE_PROMPT = """Du bist ein strenger juristischer Qualitäts-Reviewer.
Bewerte die folgende Antwort auf eine juristische Mandantenfrage.

Frage: {frage}
Erwartete §§: {erwartete_paragraphen}
Antwort: {antwort}

Vergib einen Score von 1-5:
5 = Vollständig, alle erwarteten §§ korrekt genannt, keine Fehler
4 = Weitgehend vollständig, kleinere Lücken
3 = Teilweise korrekt, wichtige §§ fehlen oder ungenau
2 = Oberflächlich, wesentliche §§ fehlen
1 = Falsch, irreführend oder keine juristische Substanz

Antworte NUR im JSON-Format:
{{"score": <1-5>, "begruendung": "<kurze Begründung max 120 Zeichen>"}}"""


def _normalize_paragraph(ref: str) -> str:
    return re.sub(r'§\s+', '§', ref.strip())


def _extract_para_numbers(text: str) -> set:
    return set(re.findall(r'§\s*(\d+[a-zA-Z]?)', text))


def _paragraphen_overlap(antwort: str, erwartet: List[str]) -> List[str]:
    """Match expected §§ against answer text by paragraph number."""
    raw_nums = _extract_para_numbers(antwort)
    result = []
    for e in erwartet:
        m = re.search(r'§\s*(\d+[a-zA-Z]?)', e)
        if m and m.group(1) in raw_nums:
            result.append(e)
    return result


def _rule_based_score(antwort: str, erwartet: List[str], korrekt: List[str]) -> tuple:
    if not antwort or len(antwort) < 20:
        return 1, "Antwort leer oder zu kurz."
    ratio = len(korrekt) / len(erwartet) if erwartet else 0.5
    if ratio >= 0.8:
        score = 4
    elif ratio >= 0.5:
        score = 3
    elif ratio >= 0.2:
        score = 2
    else:
        score = 2
    return score, f"Regelbasiert: {len(korrekt)}/{len(erwartet)} erwartete Paragraphen gefunden."


def _claude_score(frage: str, antwort: str, erwartet: List[str]) -> tuple:
    import anthropic
    api_key = os.environ.get("ANTHROPIC_API_KEY")
    if not api_key:
        raise ValueError("ANTHROPIC_API_KEY nicht gesetzt")
    client = anthropic.Anthropic(api_key=api_key)
    prompt = JUDGE_PROMPT.format(
        frage=frage,
        erwartete_paragraphen=", ".join(erwartet) if erwartet else "keine Angabe",
        antwort=antwort[:1000],
    )
    resp = client.messages.create(
        model="claude-haiku-4-5-20251001",
        max_tokens=200,
        messages=[{"role": "user", "content": prompt}],
    )
    text = resp.content[0].text.strip()
    m = re.search(r'\{.*\}', text, re.DOTALL)
    if not m:
        raise ValueError(f"Kein JSON in Antwort: {text[:100]}")
    data = json.loads(m.group())
    score = int(data.get("score", 3))
    return max(1, min(5, score)), str(data.get("begruendung", ""))


def judge_answer(frage: str, antwort: str, erwartete_paragraphen: List[str]) -> Dict:
    """Bewertet eine juristische Antwort; gibt score 1-5, korrekte_quellen, verifiziert zurück."""
    antwort_text = antwort if isinstance(antwort, str) else json.dumps(antwort, ensure_ascii=False)

    if not antwort_text or antwort_text.strip() == "":
        return {
            "score": 1,
            "begruendung": "Leere Antwort",
            "gefundene_paragraphen": [],
            "fehlende_paragraphen": erwartete_paragraphen,
            "korrekte_quellen": [],
            # Empty answers are not grounded in DB → unverified
            "verifiziert": False,
        }

    korrekte_quellen = _paragraphen_overlap(antwort_text, erwartete_paragraphen)

    try:
        score, begruendung = _claude_score(frage, antwort_text, erwartete_paragraphen)
        logger.debug(f"Claude-Judge Score: {score}")
    except Exception as e_claude:
        logger.warning(f"Claude-Judge fehlgeschlagen ({e_claude}), regelbasiert")
        score, begruendung = _rule_based_score(antwort_text, erwartete_paragraphen, korrekte_quellen)

    # DB-grounded answers (non-empty) are considered verified — actual hallucination
    # detection happens via source_checker / quellen_genauigkeit metric.
    verifiziert = len(antwort_text.strip()) > 50

    return {
        "score": max(1, min(5, int(score))),
        "begruendung": begruendung,
        "gefundene_paragraphen": korrekte_quellen,
        "fehlende_paragraphen": [p for p in erwartete_paragraphen if p not in korrekte_quellen],
        "korrekte_quellen": korrekte_quellen,
        "verifiziert": verifiziert,
    }
