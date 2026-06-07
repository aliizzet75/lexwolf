"""
/ask — Zentraler Endpunkt für den LexWolf-Prototyp.
Nimmt freien Text entgegen, erkennt den Intent, sucht in der Datenbank
und gibt Denkprozess + Ergebnis zurück.
"""
from fastapi import APIRouter
from pydantic import BaseModel
from typing import List, Optional
import time
import re

from services.database_service import DatabaseService
from services.search_service import HybridSearchService

router = APIRouter(prefix="/ask", tags=["ask"])

db = DatabaseService()
_search_service = None

def _get_search():
    global _search_service
    if _search_service is None:
        _search_service = HybridSearchService()
    return _search_service


class AskRequest(BaseModel):
    text: str


class ReasoningStep(BaseModel):
    emoji: str
    text: str


class Source(BaseModel):
    title: str
    court: Optional[str] = None
    date: Optional[str] = None
    case_number: Optional[str] = None
    excerpt: str


class AskResponse(BaseModel):
    intent: str
    reasoning: List[ReasoningStep]
    output: str
    sources: List[Source]
    duration_ms: int


# ── Intent-Erkennung ──────────────────────────────────────────────────────────

_INTENT_PATTERNS = [
    ("erstelle",    r"\b(erstell|schreib|formulier|verfass|entwerf)\b"),
    ("erklaere",    r"\b(erkl[äa]r|was (ist|bedeutet|versteht man)|definier|erl[äa]uter)\b"),
    ("suche",       r"\b(such|find|gibt es|zeig|welche urteile|rechtsprechung)\b"),
    ("frage",       r"\?"),
]

def _detect_intent(text: str) -> str:
    lower = text.lower()
    for intent, pattern in _INTENT_PATTERNS:
        if re.search(pattern, lower):
            return intent
    return "suche"


# ── Schlüsselwort-Extraktion für DB-Query ────────────────────────────────────

_STOP = {"mir", "eine", "einen", "der", "die", "das", "ein", "und", "oder",
         "ist", "sind", "ich", "für", "zu", "von", "mit", "auf", "an", "im",
         "in", "des", "dem", "den", "wie", "was", "ob", "ob", "bitte",
         "erstelle", "erstell", "schreibe", "schreib", "formuliere",
         "erkläre", "erklaere", "suche", "finde", "zeig"}

def _extract_query(text: str) -> str:
    words = re.findall(r"[a-zA-ZäöüÄÖÜß]{4,}", text)
    keywords = [w for w in words if w.lower() not in _STOP]
    return " ".join(keywords[:8]) if keywords else text


# ── Ergebnis-Formatierung ─────────────────────────────────────────────────────

def _format_output(intent: str, original_text: str, chunks: list) -> str:
    if not chunks:
        return (
            "Zu dieser Anfrage konnten keine passenden Einträge in der "
            "Rechtsdatenbank gefunden werden.\n\n"
            "Mögliche Gründe:\n"
            "• Das Thema ist noch nicht in der Datenbank erfasst\n"
            "• Die Datenbank wird nächtlich aktualisiert\n\n"
            "Bitte reformulieren Sie Ihre Anfrage oder wenden Sie sich an einen Kollegen."
        )

    if intent == "erstelle":
        header = (
            f"Auf Basis der Datenbank wurden folgende relevante Rechtsquellen "
            f"für Ihre Anfrage gefunden:\n\n"
            f"**Anfrage:** {original_text}\n\n"
            f"---\n\n"
            f"*Hinweis: Die vollständige Dokumenterstellung ist in Vorbereitung. "
            f"Im Folgenden die relevanten Rechtsgrundlagen als Ausgangsbasis:*\n\n"
        )
    elif intent == "erklaere":
        header = f"**Zur Frage:** {original_text}\n\nRelevante Rechtsquellen:\n\n"
    else:
        header = f"**Suchergebnisse für:** {original_text}\n\n"

    lines = [header]
    for i, chunk in enumerate(chunks[:5], 1):
        title = chunk.get("title", "Ohne Titel")
        court = chunk.get("court", "")
        date = chunk.get("date", "")
        text = chunk.get("text", "")[:400].strip()
        meta = " · ".join(filter(None, [court, date]))

        lines.append(f"**{i}. {title}**")
        if meta:
            lines.append(f"*{meta}*")
        lines.append(f"\n{text}{'...' if len(chunk.get('text','')) > 400 else ''}\n")
        lines.append("---\n")

    return "\n".join(lines)


# ── Endpoint ──────────────────────────────────────────────────────────────────

@router.post("", response_model=AskResponse)
async def ask(request: AskRequest):
    t0 = time.monotonic()
    text = request.text.strip()
    reasoning: List[ReasoningStep] = []

    # 1. Intent erkennen
    intent = _detect_intent(text)
    intent_labels = {
        "erstelle":  "Dokumenterstellung",
        "erklaere":  "Rechtliche Erklärung",
        "suche":     "Rechtsprechungssuche",
        "frage":     "Rechtliche Auskunft",
    }
    reasoning.append(ReasoningStep(
        emoji="🧠",
        text=f"Intent erkannt: **{intent_labels.get(intent, intent)}**"
    ))

    # 2. Suchbegriffe extrahieren
    query = _extract_query(text)
    reasoning.append(ReasoningStep(
        emoji="🔍",
        text=f"Suche in Rechtsdatenbank nach: *{query}*"
    ))

    # 3. Echte Hybrid-Suche (HyDE + pgvector + FTS + Rechtsgebiet-Boosting)
    chunks = []
    try:
        search = _get_search()
        raw = search.hybrid_search_with_graph(query, limit=8, fast_mode=True)
        # Distanz → Score: bester Treffer = 100%, Rest relativ dazu normalisiert
        distances = [float(r.get("dense_score", r.get("score", 1.0))) for r in raw]
        min_dist = min(distances) if distances else 1.0
        max_dist = max(distances) if distances else 1.0
        dist_range = max_dist - min_dist if max_dist != min_dist else 1.0
        for r, dist in zip(raw, distances):
            score = 1.0 - (dist - min_dist) / dist_range  # bester=1.0, schlechtester=0.0
            # Skaliere auf 60-100% da alle Treffer bereits relevant sind
            score = 0.60 + score * 0.40
            chunks.append({
                "id": r.get("id"),
                "text": r.get("text", ""),
                "title": r.get("title", ""),
                "score": round(score, 2),
                "source": r.get("tags", ""),
                "url": "",
            })
        reasoning.append(ReasoningStep(
            emoji="📚",
            text=f"{len(chunks)} relevante Einträge gefunden"
        ))
    except Exception as e:
        reasoning.append(ReasoningStep(
            emoji="⚠️",
            text=f"Suchfehler: {str(e)[:120]}"
        ))

    # 4. Konfidenz prüfen
    if chunks:
        scores = [c.get("score", 0) for c in chunks if isinstance(c, dict)]
        avg = sum(scores) / len(scores) if scores else 0
        reasoning.append(ReasoningStep(
            emoji="📊",
            text=f"Durchschnittliche Relevanz: {avg:.0%}"
        ))

    # 5. Output aufbereiten
    reasoning.append(ReasoningStep(
        emoji="✍️",
        text="Ergebnis wird aufbereitet..."
    ))
    output = _format_output(intent, text, chunks)

    # Sources
    sources = []
    for c in chunks[:5]:
        if not isinstance(c, dict):
            continue
        sources.append(Source(
            title=c.get("title", "Ohne Titel"),
            court=c.get("court"),
            date=c.get("date"),
            case_number=c.get("case_number"),
            excerpt=(c.get("text") or "")[:200],
        ))

    duration_ms = int((time.monotonic() - t0) * 1000)
    reasoning.append(ReasoningStep(
        emoji="✅",
        text=f"Fertig in {duration_ms} ms"
    ))

    return AskResponse(
        intent=intent,
        reasoning=reasoning,
        output=output,
        sources=sources,
        duration_ms=duration_ms,
    )
