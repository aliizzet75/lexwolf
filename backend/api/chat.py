"""
/chat — Multi-Turn-Konversation mit Gesprächsgedächtnis.
Nimmt eine Liste von Messages entgegen (max. 10) und gibt
eine Antwort des Rechtsassistenten zurück.
"""
import os
import json
import logging
import urllib.request as _urlreq
from fastapi import APIRouter
from pydantic import BaseModel
from typing import List, Optional

from services.search_service import HybridSearchService
from api.ask import _direct_paragraph_search

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


def _call_ollama(messages: list, max_tokens: int = 6000) -> str:
    """Ruft Ollama Chat Completions API auf und gibt den Antworttext zurück.
    deepseek-v3.2:cloud wurde am 2026-07-15 retired (lieferte nur noch API-Fehler,
    /chat war dadurch komplett down). Ersetzt durch kimi-k2.7-code:cloud (~2s,
    mit DB-Grounding im System-Prompt getestet: liefert korrekte, saubere Antworten).
    max_tokens war 2000 — bei verschachtelten Tabellen (z.B. Zahlbeträge-Aufstellung
    in einer Unterhaltsberechnung) verbraucht das "denkende" Modell das komplette
    Budget fürs Durchrechnen im reasoning-Feld und "content" bleibt leer, was der
    Reasoning-Leak-Schutz dann als Fallback-Antwort auffängt statt der eigentlich
    fast fertig durchgerechneten Lösung."""
    model = os.environ.get("CHAT_MODEL", "kimi-k2.7-code:cloud")
    ollama_url = os.environ.get("OLLAMA_URL", "http://localhost:11434")

    payload = json.dumps({
        "model": model,
        "messages": messages,
        "stream": False,
        "max_tokens": max_tokens,
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
    chunks_text = _search_chunks(last_user_msg) if last_user_msg else ""

    # 4. System-Prompt aufbauen
    system_parts = ["Du bist ein juristischer Rechtsassistent für deutschsprachige Anwälte."]
    if chunks_text:
        system_parts.append(f"\nRelevante Rechtsgrundlagen aus der Datenbank:\n{chunks_text}")
    if request.mandant_context:
        system_parts.append(f"\nMandanteninformation: {request.mandant_context}")
    system_parts.append(
        "\nBeantworte Fragen präzise auf Basis der Rechtsgrundlagen, auf Deutsch. "
        "Stelle Rückfragen wenn wichtige Informationen fehlen. "
        "Gib ausschließlich die fertige Antwort aus — keine Zwischengedanken, "
        "keine Meta-Kommentare zur Aufgabenstellung, kein Denkprotokoll.\n"
        "\nWichtig zu Mandanten-Dokumenten:\n"
        "- Ein Dokument gilt nur dann als NICHT lesbar, wenn sein Text explizit eine "
        "Fehlermeldung in eckigen Klammern enthält (z.B. '[Datei: PDF konnte nicht "
        "gelesen werden ...]' oder '[... OCR ...]'). In jedem anderen Fall wurde es "
        "erfolgreich gelesen — behaupte dann NIEMALS, es sei 'nicht auswertbar' oder "
        "'nicht lesbar'. Wenn die gesuchte Information/Person darin schlicht nicht "
        "vorkommt, sag das direkt (z.B. 'Dieses Dokument betrifft eine andere Person/"
        "einen anderen Sachverhalt: ...'), statt einen Lesefehler zu unterstellen.\n"
        "- Mehrere Dokumente desselben Mandanten gehören oft zusammen. Rollen- oder "
        "Kürzel-Bezeichnungen in einem Dokument (z.B. 'Mann'/'Frau', 'Hauptverdiener'/"
        "'Zweitverdiener', 'Antragsteller'/'Antragsgegnerin') können sich auf konkret "
        "benannte Personen aus einem anderen Dokument desselben Mandanten beziehen — "
        "prüfe das und verknüpfe die Angaben, statt jedes Dokument isoliert zu lesen."
    )
    system_prompt = "\n".join(system_parts)

    # 5. Vollständige Message-History für Ollama aufbauen
    ollama_messages = [{"role": "system", "content": system_prompt}]
    for m in messages:
        ollama_messages.append({"role": m.role, "content": m.content})

    # 6. Ollama aufrufen — bei Denkprotokoll-Leckage einmal mit Verstärkung retryen
    content = _call_ollama(ollama_messages)
    if _looks_like_reasoning_leak(content):
        logger.warning("Reasoning-Leak erkannt (%d Zeichen) — Retry mit verstärkter Anweisung", len(content))
        reinforced = ollama_messages + [{
            "role": "system",
            "content": "Wichtig: Antworte NUR mit der fertigen Antwort auf Deutsch, "
                       "ohne jegliche Zwischengedanken oder Erklärung deines Vorgehens.",
        }]
        content = _call_ollama(reinforced)
        if _looks_like_reasoning_leak(content):
            logger.warning("Reasoning-Leak nach Retry weiterhin vorhanden — gebe Fallback-Antwort")
            content = (
                "Entschuldigung, ich konnte dazu gerade keine klare Antwort formulieren. "
                "Können Sie die Frage etwas anders formulieren oder präzisieren?"
            )

    # 7. Intent erkennen und Aktion ableiten
    intent = _detect_intent(last_user_msg)
    suggested_action = _intent_to_action(intent)

    return ChatResponse(
        content=content,
        intent=intent,
        suggested_action=suggested_action,
    )
