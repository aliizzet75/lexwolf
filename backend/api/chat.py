"""
/chat — Multi-Turn-Konversation mit Gesprächsgedächtnis.
Nimmt eine Liste von Messages entgegen (max. 10) und gibt
eine Antwort des Rechtsassistenten zurück.
"""
import os
import json
import urllib.request as _urlreq
from fastapi import APIRouter
from pydantic import BaseModel
from typing import List, Optional

from services.search_service import HybridSearchService

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


def _detect_intent(text: str) -> str:
    """Einfache Intent-Erkennung für Chat ohne schwere Embedding-Last."""
    lower = text.lower()
    if any(k in lower for k in ("erstell", "schreib", "formulier", "aufsetzen", "entwurf", "verfass")):
        return "erstelle"
    if any(k in lower for k in ("berechne", "berechnung", "höhe", "betrag", "unterhalt", "wie viel", "wieviel")):
        return "berechne"
    return "frage"


def _intent_to_action(intent: str) -> str:
    return {
        "erstelle": "erstelle_dokument",
        "berechne": "berechne_unterhalt",
        "frage": "frage",
    }.get(intent, "frage")


def _search_chunks(query: str) -> str:
    """Sucht relevante Chunks und gibt sie als kompakten Text zurück."""
    try:
        search = _get_search()
        results = search.hybrid_search_with_graph(query, limit=5, fast_mode=True)
        if not results:
            return ""
        parts = []
        for r in results[:5]:
            title = r.get("title", "")
            text = (r.get("text") or "")[:300].strip()
            if title or text:
                parts.append(f"[{title}] {text}")
        return "\n\n".join(parts)
    except Exception:
        return ""


def _call_ollama(messages: list) -> str:
    """Ruft Ollama Chat Completions API auf und gibt den Antworttext zurück.
    Nutzt deepseek-v3.2:cloud (3-4s) statt kimi (20-50s) für schnelle Chat-Antworten."""
    # Chat braucht snappy Antworten — deepseek ist ~10x schneller als kimi für kurze Texte
    model = "deepseek-v3.2:cloud"
    ollama_url = os.environ.get("OLLAMA_URL", "http://localhost:11434")

    payload = json.dumps({
        "model": model,
        "messages": messages,
        "stream": False,
        "max_tokens": 2000,
        "temperature": 0.3,
    }).encode()

    urls_to_try = [ollama_url]
    if "localhost" not in ollama_url:
        urls_to_try.append("http://localhost:11434")

    last_error = None
    for base_url in urls_to_try:
        try:
            req = _urlreq.Request(
                f"{base_url}/v1/chat/completions",
                data=payload,
                headers={"Content-Type": "application/json"},
            )
            with _urlreq.urlopen(req, timeout=60) as resp:
                data = json.loads(resp.read().decode())
                msg = data["choices"][0]["message"]
                # DeepSeek reasoning models put chain-of-thought in "reasoning";
                # fall back to it when "content" is empty (token budget exceeded reasoning phase)
                return msg.get("content") or msg.get("reasoning", "")
        except Exception as e:
            last_error = e
            continue

    raise RuntimeError(f"Ollama nicht erreichbar: {last_error}")


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
    chunks_text = _search_chunks(last_user_msg) if last_user_msg else ""

    # 4. System-Prompt aufbauen
    system_parts = ["Du bist ein juristischer Rechtsassistent für deutschsprachige Anwälte."]
    if chunks_text:
        system_parts.append(f"\nRelevante Rechtsgrundlagen aus der Datenbank:\n{chunks_text}")
    if request.mandant_context:
        system_parts.append(f"\nMandanteninformation: {request.mandant_context}")
    system_parts.append(
        "\nBeantworte Fragen präzise auf Basis der Rechtsgrundlagen. "
        "Stelle Rückfragen wenn wichtige Informationen fehlen."
    )
    system_prompt = "\n".join(system_parts)

    # 5. Vollständige Message-History für Ollama aufbauen
    ollama_messages = [{"role": "system", "content": system_prompt}]
    for m in messages:
        ollama_messages.append({"role": m.role, "content": m.content})

    # 6. Ollama aufrufen
    content = _call_ollama(ollama_messages)

    # 7. Intent erkennen und Aktion ableiten
    intent = _detect_intent(last_user_msg)
    suggested_action = _intent_to_action(intent)

    return ChatResponse(
        content=content,
        intent=intent,
        suggested_action=suggested_action,
    )
