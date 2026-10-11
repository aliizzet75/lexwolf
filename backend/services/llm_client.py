"""Zentrale KI-Schnittstelle für LexWolf.

Jede Stelle im Code, die eine KI-Antwort braucht (Chat, Feature-Feedback,
LLM-Judge, ReAct-Engine, Search/HyDE/Reranking), ruft ausschliesslich
ask_llm() auf statt direkt einen Provider anzusprechen.

LLM_PROVIDER steuert den Anbieter — "claude" (Default, aktuell für die
Entwicklungs-/Testphase) oder "ollama" (der für den kommerziellen Einsatz
vorgesehene Kimi/Ollama-Zugang). Umschalten = nur diese Env-Var ändern,
kein Code-Edit nötig.
"""
import json as _json
import os
import urllib.request as _urlreq
from typing import Optional, Union

import anthropic

_PROVIDER = os.environ.get("LLM_PROVIDER", "claude").strip().lower()
_DEFAULT_MODEL = os.environ.get("LLM_MODEL", "claude-opus-5")
_client = anthropic.Anthropic()

Messages = list[dict]

TEST_MODE_DISCLAIMER = (
    "\n\n---\n⚠️ Testmodus: Diese Antwort stammt von einer temporären "
    "KI-Anbindung zu Entwicklungszwecken, nicht von der für den "
    "produktiven Einsatz vorgesehenen Anbindung."
)


def using_temporary_test_backend() -> bool:
    """True, solange LLM_PROVIDER=claude (Platzhalter) statt "ollama"
    (der eigentlich für den kommerziellen Einsatz vorgesehene Kimi-Zugang)."""
    return _PROVIDER != "ollama"


def ask_llm(
    prompt_or_messages: Union[str, Messages],
    *,
    max_tokens: int = 4096,
    model: Optional[str] = None,
) -> str:
    """Sendet einen Prompt (str) oder eine Chat-History (Liste von
    {"role", "content"}-Dicts, z.B. [{"role": "system", ...}, {"role": "user", ...}])
    an die KI und gibt den reinen Antworttext zurück."""
    if _PROVIDER == "ollama":
        return _ask_ollama(prompt_or_messages, max_tokens, model)
    return _ask_claude(prompt_or_messages, max_tokens, model)


def _ask_claude(prompt_or_messages: Union[str, Messages], max_tokens: int, model: Optional[str]) -> str:
    """Eine führende {"role": "system"}-Nachricht wird als System-Prompt
    behandelt; weitere "system"-Nachrichten weiter hinten in der Liste
    (z.B. nachträgliche Verstärkungs-Hinweise) bleiben als Mid-Conversation-
    System-Message in der History erhalten."""
    if isinstance(prompt_or_messages, str):
        messages = [{"role": "user", "content": prompt_or_messages}]
        system = None
    else:
        messages = list(prompt_or_messages)
        system = None
        if messages and messages[0]["role"] == "system":
            system = messages[0]["content"]
            messages = messages[1:]

    kwargs = {"model": model or _DEFAULT_MODEL, "max_tokens": max_tokens, "messages": messages}
    if system is not None:
        kwargs["system"] = system

    response = _client.messages.create(**kwargs)
    return "".join(block.text for block in response.content if block.type == "text")


def _ask_ollama(prompt_or_messages: Union[str, Messages], max_tokens: int, model: Optional[str]) -> str:
    """Der für den kommerziellen Einsatz vorgesehene Kimi/Ollama-Zugang.
    Aktiv wenn LLM_PROVIDER=ollama gesetzt ist."""
    if isinstance(prompt_or_messages, str):
        messages = [{"role": "user", "content": prompt_or_messages}]
    else:
        messages = prompt_or_messages

    ollama_model = model or os.environ.get("OLLAMA_MODEL", "kimi-k2.7-code:cloud")
    ollama_url = os.environ.get("OLLAMA_URL", "http://localhost:11434")
    payload = _json.dumps({
        "model": ollama_model,
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
                data = _json.loads(resp.read().decode())
                msg = data["choices"][0]["message"]
                # Denkende Modelle legen das Chain-of-Thought manchmal in "reasoning" ab
                return msg.get("content") or msg.get("reasoning", "")
        except Exception as e:
            last_error = e
            continue

    raise RuntimeError(f"Ollama nicht erreichbar: {last_error}")
