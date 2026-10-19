"""Zentrale KI-Schnittstelle für LexWolf.

Jede Stelle im Code, die eine KI-Antwort braucht (Chat, Feature-Feedback,
LLM-Judge, ReAct-Engine, Search/HyDE/Reranking), ruft ausschliesslich
ask_llm() auf statt direkt einen Provider anzusprechen.

LLM_PROVIDER steuert den Anbieter — "claude" (Default, Anthropic-API mit
ANTHROPIC_API_KEY), "ki-proxy" (Claude Code auf dem Entwicklungsrechner
über den lokalen KI-Proxy, siehe tools/ki-proxy)
oder "ollama" (der für den kommerziellen Einsatz vorgesehene Kimi/Ollama-
Zugang). Umschalten = nur diese Env-Var ändern, kein Code-Edit nötig.
"""
import json as _json
import os
import urllib.request as _urlreq
from typing import Optional, Union

import anthropic

_PROVIDER = os.environ.get("LLM_PROVIDER", "claude").strip().lower()
_DEFAULT_MODEL = os.environ.get("LLM_MODEL", "claude-opus-5-5")
# Aktuelle Claude-Modelle denken immer (adaptive thinking), und das Denken zählt
# gegen max_tokens. Die max_tokens der Aufrufer meinen aber nur die sichtbare
# Antwort — z.B. llm_judge mit 200 bekäme sonst eine leere Antwort, weil das
# Budget schon beim Denken aufgebraucht ist. Bezahlt werden nur echte Tokens.
_THINKING_HEADROOM = 10000
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
    if _PROVIDER == "ki-proxy":
        return _ask_ki_proxy(prompt_or_messages, max_tokens, model)
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

    kwargs = {
        "model": model or _DEFAULT_MODEL,
        "max_tokens": max_tokens + _THINKING_HEADROOM,
        "messages": messages,
    }
    if system is not None:
        kwargs["system"] = system

    response = _client.messages.create(**kwargs)
    return "".join(block.text for block in response.content if block.type == "text")


def _ask_ki_proxy(prompt_or_messages: Union[str, Messages], max_tokens: int, model: Optional[str]) -> str:
    """Claude Code auf dem Entwicklungsrechner (tools/ki-proxy) — läuft der
    Proxy nicht, schlägt die Anfrage fehl wie bei jedem anderen nicht
    erreichbaren Provider."""
    if isinstance(prompt_or_messages, str):
        messages = [{"role": "user", "content": prompt_or_messages}]
    else:
        messages = prompt_or_messages

    proxy_url = os.environ.get("KI_PROXY_URL", "http://127.0.0.1:8010").rstrip("/")
    payload = _json.dumps({"messages": messages, "max_tokens": max_tokens, "model": model}).encode()
    req = _urlreq.Request(f"{proxy_url}/llm", data=payload, headers={"Content-Type": "application/json"})
    try:
        # Die Claude-Code-CLI braucht für lange Antworten gut eine Minute.
        with _urlreq.urlopen(req, timeout=300) as resp:
            return _json.loads(resp.read().decode())["content"]
    except Exception as e:
        raise RuntimeError(f"KI-Proxy nicht erreichbar: {e}") from e


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
