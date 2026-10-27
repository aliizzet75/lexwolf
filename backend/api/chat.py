"""
/chat — Multi-Turn-Konversation mit Gesprächsgedächtnis.
Nimmt eine Liste von Messages entgegen (max. 10) und gibt
eine Antwort des Rechtsassistenten zurück.
"""
import logging
import asyncio
from fastapi import APIRouter
from pydantic import BaseModel
from typing import List, Optional, Tuple

from services.search_service import HybridSearchService
from services.llm_client import ask_llm
from services.quality_verification import QualityVerificationService
from api.ask import _direct_paragraph_search
from api.chat_prompt import (action_for_response, build_system_prompt, detect_intent, format_chunks,
                             format_zitatpruefung)

logger = logging.getLogger(__name__)

router = APIRouter(tags=["chat"])

_search_service = None
_quality_service = None


def _get_search():
    global _search_service
    if _search_service is None:
        _search_service = HybridSearchService()
    return _search_service


def _get_quality_service() -> QualityVerificationService:
    global _quality_service
    if _quality_service is None:
        _quality_service = QualityVerificationService()
    return _quality_service


class ChatMessage(BaseModel):
    role: str  # "user" oder "assistant"
    content: str


class ChatRequest(BaseModel):
    messages: List[ChatMessage]
    mandant_context: Optional[str] = None


class QuellenCheckItem(BaseModel):
    """Ergebnis der Belegprüfung für ein einzelnes Zitat (§, Rechtsprechung oder Aktendokument)."""
    zitat: str
    geprueft: bool  # wurde automatisiert gegen die Datenbank geprüft?
    existiert: bool  # Norm/Entscheidung/Dokument in der Datenbank vorhanden?
    inhalt_stimmt: bool  # entspricht das zitierte Detail (Paragraf/Fundstelle) dem DB-Eintrag?
    hinweis: Optional[str] = None
    chunk_id: Optional[int] = None  # id des zugehörigen legal_chunks-Eintrags (T#296), Positionsangabe der Quelle


class ChatResponse(BaseModel):
    content: str
    intent: str
    suggested_action: str
    confidence_score: float
    quellen_check: List[QuellenCheckItem] = []


def _search_chunks(query: str) -> str:
    """Sucht relevante Chunks und gibt sie als kompakten Text zurück.
    Expliziter §-Verweis (z.B. "§433 BGB") geht zuerst über die direkte
    Paragraphen-Suche — ohne die fand die reine Vektorsuche hier schon mal
    ZPO § 433 statt BGB § 433 (falsches Gesetz, gleiche Nummer)."""
    try:
        search = _get_search()
        para_hits = _direct_paragraph_search(query, search.database_service, limit=3)
        rest_limit = max(0, 5 - len(para_hits))
        fused = (
            search.hybrid_search_with_graph(query, limit=rest_limit, fast_mode=bool(para_hits))
            if rest_limit else []
        )
        para_ids = {c.get("id") for c in para_hits}
        results = para_hits + [r for r in fused if r.get("id") not in para_ids]
        return format_chunks(results)
    except Exception:
        return ""


_REASONING_LEAK_MARKERS = (
    "the user ask", "i need to", "let me", "i should", "let's parse",
    "we need answer", "the instruction says", "final answer:",
)


def _looks_like_reasoning_leak(text: str) -> bool:
    """Erkennt, wenn das Modell sein Denkprotokoll statt einer fertigen
    Antwort ausgibt (englische Meta-Sätze, ungewöhnlich lang für eine
    Chat-Antwort). Beobachtet bei kimi-k2.7-code:cloud wenn die gelieferten
    DB-Chunks nicht zur Frage passten."""
    if len(text) < 800:
        return False
    lower = text.lower()
    return sum(lower.count(m) for m in _REASONING_LEAK_MARKERS) >= 2


def _status_zu_flags(status: str, hinweis: Optional[str] = None) -> Tuple[bool, bool, bool]:
    """Übersetzt den von api.akte.pruefe_belege gelieferten Status-String in
    (geprueft, existiert, inhalt_stimmt). 'bitte prüfen' heißt je nach Hinweis
    entweder 'automatisch nicht prüfbar' (Art.-Zitate) oder 'geprüft, aber nicht
    in der Datenbank gefunden' (Rechtsprechung)."""
    if status == "geprüft":
        return True, True, True
    if status in ("nicht gefunden", "nicht in Datenbank", "Dokument existiert nicht"):
        return True, False, False
    if status == "bitte prüfen" and hinweis == "nicht in Datenbank":
        return True, False, False
    return False, False, False  # "bitte prüfen" (Art.) oder "nicht prüfbar"


def _quellen_check_aus_belegen(belege: list) -> List[QuellenCheckItem]:
    """Baut die pro-Antwort-Quellen-Check-Liste aus den bereits für die
    Zitatprüfung berechneten Belegen (api.akte.pruefe_belege) — keine
    zusätzlichen DB-Abfragen nötig."""
    items = []
    for b in belege:
        geprueft, existiert, inhalt_stimmt = _status_zu_flags(b.get("status", ""), b.get("hinweis"))
        items.append(QuellenCheckItem(
            zitat=b["zitat"],
            geprueft=geprueft,
            existiert=existiert,
            inhalt_stimmt=inhalt_stimmt,
            hinweis=b.get("hinweis"),
            chunk_id=b.get("chunk_id"),
        ))
    return items


@router.post("/chat", response_model=ChatResponse)
async def chat(request: ChatRequest) -> ChatResponse:
    # 1. Letzte 10 Messages begrenzen
    messages = request.messages[-10:]

    # 2. Letzte User-Nachricht für Suche nutzen
    last_user_msg = ""
    for m in reversed(messages):
        if m.role == "user":
            last_user_msg = m.content
            break

    # 3. Relevante Chunks suchen
    # _search_chunks ist blockierend (DB/Embedding-Calls) — in Thread auslagern,
    # sonst friert der einzige uvicorn-Event-Loop für ALLE Requests ein (siehe
    # Vorfall 2026-09-01: Backend über Minuten komplett unerreichbar, auch für
    # /health, weil ein einzelner /chat-Call synchron blockierte).
    chunks_text = await asyncio.to_thread(_search_chunks, last_user_msg) if last_user_msg else ""

    # 4. System-Prompt aufbauen
    system_prompt = build_system_prompt(chunks_text, request.mandant_context)

    # 5. Vollständige Message-History für Ollama aufbauen
    ollama_messages = [{"role": "system", "content": system_prompt}]
    for m in messages:
        ollama_messages.append({"role": m.role, "content": m.content})

    # 6. KI aufrufen — bei Denkprotokoll-Leckage einmal mit Verstärkung retryen
    # ask_llm ist blockierend (Anthropic-SDK-Call) — ebenfalls in Thread
    # auslagern, siehe Kommentar oben bei _search_chunks.
    content = await asyncio.to_thread(ask_llm, ollama_messages, max_tokens=6000, rolle="chat")
    if _looks_like_reasoning_leak(content):
        logger.warning("Reasoning-Leak erkannt (%d Zeichen) — Retry mit verstärkter Anweisung", len(content))
        reinforced = ollama_messages + [{
            "role": "system",
            "content": "Wichtig: Antworte NUR mit der fertigen Antwort auf Deutsch, "
                       "ohne jegliche Zwischengedanken oder Erklärung deines Vorgehens.",
        }]
        content = await asyncio.to_thread(ask_llm, reinforced, max_tokens=6000, rolle="chat")
        if _looks_like_reasoning_leak(content):
            logger.warning("Reasoning-Leak nach Retry weiterhin vorhanden — gebe Fallback-Antwort")
            content = (
                "Entschuldigung, ich konnte dazu gerade keine klare Antwort formulieren. "
                "Können Sie die Frage etwas anders formulieren oder präzisieren?"
            )

    # 7. Intent erkennen und Aktion ableiten
    intent = detect_intent(last_user_msg)
    suggested_action = action_for_response(content, intent)

    # 8. Zitierte Normen/Aktenzeichen gegen die Datenbank prüfen (wie beim Schriftsatz).
    # belege wird auch für das quellen_check-Feld der Response weiterverwendet.
    belege = []
    try:
        from api.akte import pruefe_belege
        belege = await asyncio.to_thread(pruefe_belege, content, 0)
        content += format_zitatpruefung(belege)
    except Exception as e:
        logger.warning(f"Zitatprüfung im Chat fehlgeschlagen: {e}")

    # 9. Qualitätsmetriken (M3 LLM-as-Judge-Pipeline) pro Antwort — auf dem
    # reinen LLM-Content berechnet, bevor Zitatprüfung/Disclaimer angehängt werden,
    # sonst würden die eigenen "✓ §… gefunden"-Zeilen die Zitat-Extraktion verzerren.
    confidence_score = 0.0
    try:
        quality_result = await asyncio.to_thread(
            _get_quality_service().verify_generated_content, content
        )
        confidence_score = quality_result.get("quality_score", 0.0)
    except Exception as e:
        logger.warning(f"Qualitätsprüfung im Chat fehlgeschlagen: {e}")

    quellen_check = _quellen_check_aus_belegen(belege)

    from services.llm_client import using_temporary_test_backend, TEST_MODE_DISCLAIMER
    if using_temporary_test_backend():
        content += TEST_MODE_DISCLAIMER

    return ChatResponse(
        content=content,
        intent=intent,
        suggested_action=suggested_action,
        confidence_score=confidence_score,
        quellen_check=quellen_check,
    )
