"""
/chat — Multi-Turn-Konversation mit Gesprächsgedächtnis.
Nimmt eine Liste von Messages entgegen (max. 10) und gibt
eine Antwort des Rechtsassistenten zurück.
"""
import logging
import asyncio
from fastapi import APIRouter
from pydantic import BaseModel
from typing import List, Optional

from services.search_service import HybridSearchService
from services.llm_client import ask_llm
from api.ask import _direct_paragraph_search
from api.chat_prompt import (action_for_response, build_system_prompt, detect_intent, format_chunks,
                             format_zitatpruefung)

logger = logging.getLogger(__name__)

router = APIRouter(tags=["chat"])

_search_service = None


def _get_search():
    global _search_service
    if _search_service is None:
        _search_service = HybridSearchService()
    return _search_service


class ChatMessage(BaseModel):
    role: str  # "user" oder "assistant"
    content: str


class ChatRequest(BaseModel):
    messages: List[ChatMessage]
    mandant_context: Optional[str] = None


class ChatResponse(BaseModel):
    content: str
    intent: str
    suggested_action: str


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
    content = await asyncio.to_thread(ask_llm, ollama_messages, max_tokens=6000)
    if _looks_like_reasoning_leak(content):
        logger.warning("Reasoning-Leak erkannt (%d Zeichen) — Retry mit verstärkter Anweisung", len(content))
        reinforced = ollama_messages + [{
            "role": "system",
            "content": "Wichtig: Antworte NUR mit der fertigen Antwort auf Deutsch, "
                       "ohne jegliche Zwischengedanken oder Erklärung deines Vorgehens.",
        }]
        content = await asyncio.to_thread(ask_llm, reinforced, max_tokens=6000)
        if _looks_like_reasoning_leak(content):
            logger.warning("Reasoning-Leak nach Retry weiterhin vorhanden — gebe Fallback-Antwort")
            content = (
                "Entschuldigung, ich konnte dazu gerade keine klare Antwort formulieren. "
                "Können Sie die Frage etwas anders formulieren oder präzisieren?"
            )

    # 7. Intent erkennen und Aktion ableiten
    intent = detect_intent(last_user_msg)
    suggested_action = action_for_response(content, intent)

    # 8. Zitierte Normen/Aktenzeichen gegen die Datenbank prüfen (wie beim Schriftsatz)
    try:
        from api.akte import pruefe_belege
        content += format_zitatpruefung(await asyncio.to_thread(pruefe_belege, content, 0))
    except Exception as e:
        logger.warning(f"Zitatprüfung im Chat fehlgeschlagen: {e}")

    from services.llm_client import using_temporary_test_backend, TEST_MODE_DISCLAIMER
    if using_temporary_test_backend():
        content += TEST_MODE_DISCLAIMER

    return ChatResponse(
        content=content,
        intent=intent,
        suggested_action=suggested_action,
    )
