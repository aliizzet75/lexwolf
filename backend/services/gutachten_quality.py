"""
Qualitätssicherung für den Gutachten-Modus (T#278, Milestone "Gutachten /
Anspruchsprüfung", Paket G-8 in docs/KONZEPT_GUTACHTEN.md).

Nutzt die bestehende LLM-as-Judge/Post-Verification-Pipeline (Entry 48,
siehe docs/quality_protocol.md, backend/llm_judge.py,
services/quality_verification.py) als Grundlage, ergänzt sie aber um:

1. Eine STRENGERE Vollständigkeits-Metrik speziell für Einwendungen/Einreden
   (EINWAENDE_VOLLSTAENDIGKEIT_SCHWELLE = 0.95 statt der allgemeinen 0.80 aus
   quality_protocol.md) — ein übersehener Einwand (z.B. Verjährung) kann die
   Mandanten-Entscheidung über Klage oder Vergleich direkt falsch beeinflussen,
   das ist eine andere Fehlerklasse als eine ungenaue Paragraphenangabe.
2. Eine Norm-Existenzprüfung gegen die echte Postgres-Datenbank
   (Tabelle legal_chunks), analog zu tests/quality/source_checker.py, aber
   produktionsseitig in backend/ statt nur in tests/, weil T#274 (Routing)
   die Prüfung vor jeder Anzeige an den Anwalt braucht.
3. Ein zweites, unabhängiges LLM-as-Judge-Review speziell für den
   Gutachtenstil (nicht nur Paragraphen-Overlap wie llm_judge.py, sondern
   Prüfung von Vollständigkeit, Schlüssigkeit und erfundenen Aktenzeichen).
4. Eine Produktions-Freigabesperre: ist_produktionsfreigabe_erteilt() liest
   die schriftliche Freigabe des Anwalts aus docs/gutachten_freigabe_status.json
   (analog zum bestehenden M3-Vorgehen aus Entry 48 — "Schriftliche Freigabe
   durch Bruder (Rechtsanwalt) nach manuellem Test"). Ohne diese Freigabe darf
   der Gutachten-Modus nicht produktiv geschaltet werden.
"""
import json
import logging
import os
import re
from typing import Optional

from services.gutachten_prompt import (
    ANSPRUCH_ERGEBNIS,
    JA_NEIN_OFFEN,
    validate_anspruchspruefung,
)

logger = logging.getLogger(__name__)

# ------------------------------------------------------------- Schwellenwerte

# Allgemeine Vollständigkeit (M3/Entry 48, docs/quality_protocol.md): > 0.80.
# Für Einwendungen/Einreden im Gutachten gilt eine STRENGERE Schwelle, weil ein
# übersehener Einwand (z.B. Verjährung, Mitverschulden) den Mandanten zu einer
# falschen Klage-/Vergleichsentscheidung verleiten kann.
EINWAENDE_VOLLSTAENDIGKEIT_SCHWELLE = 0.95

# Zweiter, unabhängiger LLM-Judge-Durchlauf (Skala 1-5 wie backend/llm_judge.py);
# ein Gutachten mit Mandanten-Relevanz braucht mehr als "ausreichend" (3).
JUDGE_MINDEST_SCORE = 4

_PG_DSN_DEFAULT = "host=localhost port=5432 dbname=lexwolf user=postgres password=postgres"
_FREIGABE_STATUS_PATH = os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "docs", "gutachten_freigabe_status.json"
)


# ---------------------------------------------------- Einwände-Vollständigkeit

#  "Fehlende Abmahnung" und "Zwei-Wochen-Frist" teilen sich z.B. dieselbe
# Grundnorm § 626 BGB, sind aber verschiedene Einwände — ein Treffer NUR über
# die reine Paragraphennummer würde das Fehlen des einen durch die bloße
# Anwesenheit des anderen verdecken. Deshalb zählt die Normnummer allein
# NICHT als Treffer; maßgeblich ist der exakt normalisierte Norm-String
# (inkl. "Abs./Satz") oder eine inhaltliche Übereinstimmung der Bezeichnung.
_STOPWOERTER = {
    "der", "die", "das", "des", "dem", "den", "und", "oder", "von", "vor",
    "nach", "bei", "für", "durch", "ohne", "zur", "zum", "auf", "mit", "ist",
    "sind", "eine", "einer", "einem", "einen", "einwand", "einwendung",
    "einrede", "anspruch", "ansprüche", "wegen", "fehlende", "fehlender",
    "fehlenden", "fehlend",
}


def _normalize_norm(text: str) -> str:
    return re.sub(r"\s+", " ", (text or "").strip().lower())


def _keywords(text: str) -> set:
    """Inhaltstragende Wörter einer Einwands-Bezeichnung (>= 4 Buchstaben,
    ohne Füllwörter) für den Vergleich zweier Bezeichnungen."""
    woerter = re.findall(r"[a-zäöüß]{4,}", (text or "").lower())
    return {w for w in woerter if w not in _STOPWOERTER}


def _keywords_ueberlappen(a: set, b: set) -> bool:
    """True, wenn ein Schlüsselwort aus a Teilstring eines aus b ist (oder
    umgekehrt) — erfasst Wortstamm-Varianten wie "Verjährung" vs.
    "Verjährungseinrede"."""
    return any(x in y or y in x for x in a for y in b)


def _einwand_gefunden(erwartet: dict, gefundene: list) -> bool:
    """Prüft, ob ein erwarteter Einwand unter den von der KI gefundenen
    einwendungen_einreden auftaucht — über eine inhaltliche Übereinstimmung
    der Bezeichnung ODER eine exakte Übereinstimmung der vollständigen
    Norm-Angabe (weil die KI einen Einwand ggf. anders benennt als im
    Testfall vorgegeben, aber nicht einfach weil dieselbe Grundnorm auch für
    einen anderen Einwand zitiert wird, siehe oben)."""
    e_kw = _keywords(erwartet.get("bezeichnung", ""))
    e_norm = _normalize_norm(erwartet.get("norm", ""))

    for g in gefundene:
        if not isinstance(g, dict):
            continue
        g_kw = _keywords(g.get("bezeichnung", ""))
        if e_kw and g_kw and _keywords_ueberlappen(e_kw, g_kw):
            return True
        g_norm = _normalize_norm(g.get("norm", ""))
        if e_norm and g_norm and e_norm == g_norm:
            return True
    return False


def einwaende_vollstaendigkeit(ergebnis: dict, erwartete_einwaende: list) -> dict:
    """Recall der erwarteten Einwendungen/Einreden über ALLE Anspruchsgrundlagen
    des Gutachtens hinweg (die KI muss einen Einwand nicht derselben
    Anspruchsgrundlage zuordnen wie der Testfall, nur finden).

    Gibt {"score": float, "gefunden": [...], "fehlend": [...]} zurück.
    score=1.0 bei leerer erwartete_einwaende-Liste (nichts zu finden)."""
    if not erwartete_einwaende:
        return {"score": 1.0, "gefunden": [], "fehlend": []}

    agls = ergebnis.get("anspruchsgrundlagen") or []
    alle_gefundenen = []
    for agl in agls:
        if isinstance(agl, dict):
            alle_gefundenen.extend(agl.get("einwendungen_einreden") or [])

    gefunden, fehlend = [], []
    for erwartet in erwartete_einwaende:
        (gefunden if _einwand_gefunden(erwartet, alle_gefundenen) else fehlend).append(erwartet)

    return {
        "score": len(gefunden) / len(erwartete_einwaende),
        "gefunden": gefunden,
        "fehlend": fehlend,
    }


# --------------------------------------------------- Normprüfung gegen die DB

def _alle_normen(ergebnis: dict) -> list:
    """Sammelt alle Norm-Angaben aus dem Gutachten-JSON (Anspruchsgrundlagen,
    Voraussetzungen, Einwendungen) für die Existenzprüfung gegen die DB."""
    normen = []
    for agl in ergebnis.get("anspruchsgrundlagen") or []:
        if not isinstance(agl, dict):
            continue
        if agl.get("norm"):
            normen.append(agl["norm"])
        for v in agl.get("voraussetzungen") or []:
            if isinstance(v, dict) and v.get("norm"):
                normen.append(v["norm"])
        for e in agl.get("einwendungen_einreden") or []:
            if isinstance(e, dict) and e.get("norm"):
                normen.append(e["norm"])
    return normen


def pruefe_normen_in_db(ergebnis: dict, pg_dsn: Optional[str] = None) -> dict:
    """Prüft, ob die im Gutachten genannten §§ tatsächlich in legal_chunks
    existieren (echte Postgres-Abfrage, kein Mock). Gibt
    {"gesamt", "existieren", "fehlen", "fehlende_normen"} zurück.

    Eine nicht in der DB gefundene Norm ist für ein Gutachten schwerwiegender
    als im allgemeinen Chat-Modus — der Anwalt könnte sich auf eine Norm
    verlassen, die die KI erfunden oder falsch benannt hat."""
    dsn = pg_dsn or os.environ.get("PG_DSN", _PG_DSN_DEFAULT)
    normen = _alle_normen(ergebnis)
    unique = list(dict.fromkeys(normen))
    if not unique:
        return {"gesamt": 0, "existieren": 0, "fehlen": 0, "fehlende_normen": []}

    import psycopg2

    existieren = []
    try:
        conn = psycopg2.connect(dsn)
        try:
            with conn.cursor() as cur:
                for norm in unique:
                    m = re.search(r"§\s*(\d+[a-zA-Z]?)", norm)
                    if not m:
                        continue
                    nr = m.group(1)
                    cur.execute(
                        "SELECT 1 FROM legal_chunks WHERE title LIKE %s OR title LIKE %s LIMIT 1",
                        (f"§ {nr} %", f"§ {nr}"),
                    )
                    if cur.fetchone():
                        existieren.append(norm)
        finally:
            conn.close()
    except Exception as e:
        logger.warning(f"Normprüfung gegen DB fehlgeschlagen ({e}); keine Normen als geprüft markiert")
        return {"gesamt": len(unique), "existieren": 0, "fehlen": len(unique), "fehlende_normen": unique, "fehler": str(e)}

    fehlende = [n for n in unique if n not in existieren]
    return {
        "gesamt": len(unique),
        "existieren": len(existieren),
        "fehlen": len(fehlende),
        "fehlende_normen": fehlende,
    }


# --------------------------------------------- Zweiter, unabhängiger LLM-Judge

def _rule_based_judge(ergebnis: dict, einwaende_score: float, normen_check: dict) -> tuple:
    """Fallback ohne LLM-Aufruf (z.B. kein API-Key verfügbar) — kombiniert
    Schema-Validität, Einwände-Vollständigkeit und Normprüfung zu einem
    1-5-Score, analog zu llm_judge._rule_based_score()."""
    fehler = validate_anspruchspruefung(ergebnis)
    normen_ok = normen_check.get("fehlen", 0) == 0

    if fehler or not normen_ok:
        score = 1
    elif einwaende_score >= 0.95:
        score = 5
    elif einwaende_score >= 0.80:
        score = 4
    elif einwaende_score >= 0.5:
        score = 3
    else:
        score = 2
    begruendung = (
        f"Regelbasiert: {len(fehler)} Schema-Fehler, "
        f"Einwände-Score {einwaende_score:.0%}, "
        f"{normen_check.get('fehlen', 0)} fehlende Normen in DB."
    )
    return score, begruendung


def judge_gutachten(sachverhalt: str, ergebnis: dict, erwartete_einwaende: list, normen_check: dict) -> dict:
    """Zweites, unabhängiges LLM-as-Judge-Review speziell für den
    Gutachtenstil. Prüft Vollständigkeit der Einwände, Schlüssigkeit und
    ob Aktenzeichen/Rechtsprechung plausibel wirken (keine Garantie, aber ein
    weiterer Blickwinkel als der generierende Aufruf selbst).

    Fällt auf eine Regel zurück, wenn der LLM-Aufruf fehlschlägt (kein
    API-Key, Netzwerkfehler) — damit die Pipeline auch offline testbar ist."""
    einwaende = einwaende_vollstaendigkeit(ergebnis, erwartete_einwaende)

    try:
        from services.llm_client import ask_llm

        prompt = (
            "Du bist ein zweiter, unabhängiger juristischer Prüfer (LLM-as-Judge). "
            "Ein anderes Modell hat folgendes Gutachten-JSON zu einem Sachverhalt erstellt. "
            "Prüfe NICHT den Sachverhalt neu, sondern ob das JSON in sich schlüssig ist, "
            "ob die erwarteten Einwendungen/Einreden berücksichtigt wurden, und ob Normen "
            "oder Aktenzeichen unplausibel/erfunden wirken.\n\n"
            f"Sachverhalt (Auszug):\n{(sachverhalt or '')[:1500]}\n\n"
            f"Erwartete Einwendungen/Einreden (vom Testfall, nicht dem Anwalt sichtbar):\n"
            f"{json.dumps(erwartete_einwaende, ensure_ascii=False)}\n\n"
            f"Gutachten-JSON:\n{json.dumps(ergebnis, ensure_ascii=False)[:4000]}\n\n"
            "Antworte NUR mit JSON: "
            '{"score": <1-5>, "begruendung": "<max 200 Zeichen>", "fehlende_einwaende": ["..."]}'
        )
        text = ask_llm(prompt, max_tokens=400).strip()
        m = re.search(r"\{.*\}", text, re.DOTALL)
        if not m:
            raise ValueError(f"Kein JSON in Judge-Antwort: {text[:100]}")
        data = json.loads(m.group())
        score = int(data["score"])
        begruendung = str(data.get("begruendung", ""))
    except Exception as e:
        logger.warning(f"LLM-Judge für Gutachten fehlgeschlagen ({e}), Fallback auf Regel-Score")
        score, begruendung = _rule_based_judge(ergebnis, einwaende["score"], normen_check)

    return {
        "score": max(1, min(5, score)),
        "begruendung": begruendung,
        "einwaende_vollstaendigkeit": einwaende["score"],
        "fehlende_einwaende": einwaende["fehlend"],
    }


# -------------------------------------------------------- Gesamt-Qualitätstor

def pruefe_gutachten_qualitaet(
    sachverhalt: str,
    ergebnis: dict,
    erwartete_einwaende: Optional[list] = None,
    pg_dsn: Optional[str] = None,
) -> dict:
    """Gesamt-Qualitätsprüfung für EIN Gutachten-Ergebnis: Schema, strengere
    Einwände-Vollständigkeit, Normprüfung gegen die echte DB und ein zweites
    LLM-Judge-Review. Gibt zurück, ob das Gutachten dem Anwalt ohne weitere
    Warnung gezeigt werden darf (bestanden=True) und, falls nicht, warum."""
    erwartete_einwaende = erwartete_einwaende or []
    gruende_blockiert = []

    schema_fehler = validate_anspruchspruefung(ergebnis)
    if schema_fehler:
        gruende_blockiert.append(f"Schema ungültig: {'; '.join(schema_fehler)}")

    einwaende = einwaende_vollstaendigkeit(ergebnis, erwartete_einwaende)
    if einwaende["score"] < EINWAENDE_VOLLSTAENDIGKEIT_SCHWELLE:
        fehlend_namen = [e.get("bezeichnung", "?") for e in einwaende["fehlend"]]
        gruende_blockiert.append(
            f"Einwände-Vollständigkeit {einwaende['score']:.0%} < "
            f"{EINWAENDE_VOLLSTAENDIGKEIT_SCHWELLE:.0%} (fehlend: {', '.join(fehlend_namen)})"
        )

    normen_check = pruefe_normen_in_db(ergebnis, pg_dsn=pg_dsn)
    if normen_check.get("fehlen", 0) > 0:
        gruende_blockiert.append(
            f"{normen_check['fehlen']} zitierte Norm(en) nicht in DB gefunden: "
            f"{', '.join(normen_check['fehlende_normen'])}"
        )

    judge = judge_gutachten(sachverhalt, ergebnis, erwartete_einwaende, normen_check)
    if judge["score"] < JUDGE_MINDEST_SCORE:
        gruende_blockiert.append(
            f"LLM-Judge-Score {judge['score']}/5 < {JUDGE_MINDEST_SCORE}/5 ({judge['begruendung']})"
        )

    return {
        "bestanden": not gruende_blockiert,
        "gruende_blockiert": gruende_blockiert,
        "schema_fehler": schema_fehler,
        "einwaende_vollstaendigkeit": einwaende["score"],
        "einwaende_fehlend": einwaende["fehlend"],
        "normen_pruefung": normen_check,
        "judge_score": judge["score"],
        "judge_begruendung": judge["begruendung"],
    }


# --------------------------------------------- Produktions-Freigabesperre

def ist_produktionsfreigabe_erteilt(status_pfad: Optional[str] = None) -> bool:
    """Liest die schriftliche Freigabe des Anwalts (analog M3/Entry 48:
    "Schriftliche Freigabe durch Bruder (Rechtsanwalt) nach manuellem Test").

    Ohne diese Freigabe (Default: False) darf T#274 den Gutachten-Modus nicht
    produktiv anbieten — selbst wenn alle automatisierten Metriken bestehen,
    ersetzt das nicht die fachliche Prüfung durch den Anwalt (§ 43 BRAO)."""
    pfad = status_pfad or _FREIGABE_STATUS_PATH
    try:
        with open(pfad, encoding="utf-8") as f:
            status = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return False
    return bool(status.get("freigegeben")) and bool(status.get("freigegeben_durch"))
