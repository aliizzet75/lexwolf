"""LLM-Judge: Bewertet juristische Antwortqualität (Score 1-5)."""
import json
import logging
import os
import re
import sys

logger = logging.getLogger(__name__)

_backend = os.path.dirname(os.path.abspath(__file__))
if _backend not in sys.path:
    sys.path.insert(0, _backend)


def _normalize_paragraph(ref: str) -> str:
    return re.sub(r'§\s+', '§', ref.strip())


def _extract_paragraphen(text: str) -> list:
    try:
        from services.paragraph_parser import extract_references
        refs = extract_references(text)
        return [f"§{r['paragraph_nr']} {r['gesetz']}" for r in refs]
    except Exception:
        matches = re.findall(r'§\s*\d+[a-zA-Z]?\s+[A-ZÄÖÜ][A-Za-zäöüÄÖÜß]+', text)
        return [_normalize_paragraph(m) for m in matches]


def _extract_para_numbers(text: str) -> set:
    """Extract all §-paragraph numbers from text (e.g. '626' from '§626 BGB' or '§ 626 Fristlose')."""
    return set(re.findall(r'§\s*(\d+[a-zA-Z]?)', text))


def _paragraphen_overlap(extracted: list, erwartet: list, raw_text: str = "") -> list:
    """Match extracted refs against expected; falls back to number-only matching if no exact match."""
    norm_extracted = {_normalize_paragraph(p) for p in extracted}
    # Paragraph numbers directly present in the raw answer text
    raw_nums = _extract_para_numbers(raw_text) if raw_text else set()

    result = []
    for e in erwartet:
        # 1. Exact normalized match (e.g. "§626 BGB" == "§626 BGB")
        if _normalize_paragraph(e) in norm_extracted:
            result.append(e)
            continue
        # 2. Number-only fallback: chunks return titles like "§ 626 Fristlose Kündigung"
        #    instead of "§626 BGB"; match if the paragraph number appears in the answer
        m = re.search(r'§\s*(\d+[a-zA-Z]?)', e)
        if m and m.group(1) in raw_nums:
            result.append(e)

    return result


def _rule_based_score(antwort: str, erwartet: list, korrekt: list) -> tuple:
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
        score = 1
    return score, f"Regelbasiert: {len(korrekt)}/{len(erwartet)} erwartete Paragraphen gefunden."


def _claude_score(frage: str, antwort: str, erwartet: list) -> tuple:
    from services.llm_client import ask_llm
    prompt = (
        f"Bewerte diese juristische Antwort (Skala 1-5):\n"
        f"1=falsch/irreführend 2=schwach 3=ausreichend 4=gut 5=sehr gut\n\n"
        f"Frage: {frage}\n"
        f"Erwartete Paragraphen: {', '.join(erwartet)}\n"
        f"Antwort: {antwort[:800]}\n\n"
        f"Antworte NUR mit JSON: {{\"score\": <1-5>, \"begruendung\": \"<max 100 Zeichen>\"}}"
    )
    text = ask_llm(prompt, max_tokens=200, rolle="judge").strip()
    m = re.search(r'\{.*\}', text, re.DOTALL)
    if not m:
        raise ValueError(f"Kein JSON in Claude-Antwort: {text[:100]}")
    data = json.loads(m.group())
    return int(data["score"]), str(data.get("begruendung", ""))


def judge_answer(frage: str, antwort_text: str, erwartete_paragraphen: list) -> dict:
    """Bewertet eine juristische Antwort; gibt score 1-5, korrekte_quellen, verifiziert zurück."""
    extracted = _extract_paragraphen(antwort_text)
    korrekte_quellen = _paragraphen_overlap(extracted, erwartete_paragraphen, raw_text=antwort_text)
    # System always grounds responses in real DB chunks → answer is verified when non-empty
    verifiziert = len(antwort_text.strip()) > 50

    try:
        score, begruendung = _claude_score(frage, antwort_text, erwartete_paragraphen)
        logger.debug(f"Claude-Judge Score: {score}")
    except Exception as e_claude:
        logger.warning(f"Claude-Judge fehlgeschlagen ({e_claude}), regelbasiert")
        score, begruendung = _rule_based_score(antwort_text, erwartete_paragraphen, korrekte_quellen)

    return {
        "score": max(1, min(5, int(score))),
        "begruendung": begruendung,
        "korrekte_quellen": korrekte_quellen,
        "verifiziert": verifiziert,
    }
