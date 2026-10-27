"""Zentrale KI-Schnittstelle für LexWolf.

Jede Stelle im Code, die eine KI-Antwort braucht (Chat, Feature-Feedback,
LLM-Judge, ReAct-Engine, Search/HyDE/Reranking), ruft ausschliesslich
ask_llm() auf statt direkt einen Provider anzusprechen.

LLM_PROVIDER steuert den Anbieter — "claude" (Default, Anthropic-API mit
ANTHROPIC_API_KEY), "ki-proxy" (Claude Code auf dem Entwicklungsrechner
über den lokalen KI-Proxy, siehe tools/ki-proxy)
oder "ollama" (der für den kommerziellen Einsatz vorgesehene Kimi/Ollama-
Zugang). Umschalten = nur diese Env-Var ändern, kein Code-Edit nötig.

T#313: Zusätzlich dazu kann in den LexWolf-Einstellungen ("KI-Modell (Test)") zum
Testen zwischen Localhost (diese Env-Var-Logik unverändert), einem festen Modell
oder Hybrid (zwei Modelle, automatisch nach Anfragegröße aufgeteilt) umgeschaltet
werden, ohne das Backend neu zu starten — siehe llm_config.route(). Das übersteuert
LLM_PROVIDER nur, wenn ein Modus ungleich "localhost" aktiv ist.
"""
import json as _json
import os
import urllib.request as _urlreq
from typing import Optional, Union

import anthropic

from . import llm_config as _llm_config

import logging as _logging

_LOGGER = _logging.getLogger(__name__)
_PROVIDER = os.environ.get("LLM_PROVIDER", "claude").strip().lower()
_DEFAULT_MODEL = os.environ.get("LLM_MODEL", "claude-opus-5-5")
_FALLBACK_PROVIDER = os.environ.get("LLM_FALLBACK_PROVIDER", "").strip().lower()
# Aktuelle Claude-Modelle denken immer (adaptive thinking), und das Denken zählt
# gegen max_tokens. Die max_tokens der Aufrufer meinen aber nur die sichtbare
# Antwort — z.B. llm_judge mit 200 bekäme sonst eine leere Antwort, weil das
# Budget schon beim Denken aufgebraucht ist. Bezahlt werden nur echte Tokens.
_THINKING_HEADROOM = 10000
_client = anthropic.Anthropic()
# Eigene Clients je abweichendem API-Key aus llm_config (T#313) — der globale
# _client oben bleibt für den Env-Var-Default (ANTHROPIC_API_KEY) unverändert.
_clients_by_key: dict[str, anthropic.Anthropic] = {}


def _claude_client(api_key: Optional[str]) -> anthropic.Anthropic:
    if not api_key:
        return _client
    if api_key not in _clients_by_key:
        _clients_by_key[api_key] = anthropic.Anthropic(api_key=api_key)
    return _clients_by_key[api_key]


Messages = list[dict]


def ask_llm(
    prompt_or_messages: Union[str, Messages],
    *,
    max_tokens: int = 4096,
    model: Optional[str] = None,
    rolle: Optional[str] = None,
) -> str:
    """Sendet einen Prompt (str) oder eine Chat-History (Liste von
    {"role", "content"}-Dicts, z.B. [{"role": "system", ...}, {"role": "user", ...}])
    an die KI und gibt den reinen Antworttext zurück.

    rolle (T#312, optional): z.B. "judge", "chat", "schriftsatz" — im Hybrid-
    Modus (siehe llm_config.ROLLEN_IMMER_HOCHWERTIG) erzwingt das das teurere
    der beiden konfigurierten Modelle unabhängig von max_tokens. Ohne rolle
    (HyDE/Reranking/Feature-Feedback) bleibt das bisherige größenbasierte
    Verhalten unverändert.

    T#316: Scheitert der primäre Provider, wird automatisch ein Versuch mit dem
    konfigurierten Fallback-Provider unternommen, bevor die Exception nach oben
    durchgereicht wird."""
    for idx, (provider, routed_model, api_key) in enumerate(_llm_config.route_mit_fallback(max_tokens, rolle=rolle)):
        if provider is None:
            # localhost-Modus: LLM_PROVIDER/LLM_FALLBACK_PROVIDER bestimmen Provider
            if idx == 0:
                provider = _PROVIDER
            else:
                provider = _FALLBACK_PROVIDER or _default_fallback_provider(_PROVIDER)
                if provider == _PROVIDER:
                    continue
        effective_model = model or routed_model
        try:
            return _ask_provider(provider, prompt_or_messages, max_tokens, effective_model, api_key)
        except Exception as e:
            _LOGGER.warning(f"KI-Provider '{provider}' fehlgeschlagen: {e}")
            last_error = e
            continue

    raise last_error


def _default_fallback_provider(primary: str) -> str:
    """Einfacher provider-übergreifender Fallback: claude/ki-proxy -> ollama,
    ollama -> claude."""
    if primary in ("claude", "ki-proxy"):
        return "ollama"
    return "claude"


def _ask_provider(
    provider: str,
    prompt_or_messages: Union[str, Messages],
    max_tokens: int,
    model: Optional[str],
    api_key: Optional[str] = None,
) -> str:
    if provider == "ollama":
        return _ask_ollama(prompt_or_messages, max_tokens, model)
    if provider == "ki-proxy":
        return _ask_ki_proxy(prompt_or_messages, max_tokens, model)
    return _ask_claude(prompt_or_messages, max_tokens, model, api_key)


def _ask_claude(
    prompt_or_messages: Union[str, Messages], max_tokens: int, model: Optional[str], api_key: Optional[str] = None,
) -> str:
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

    response = _claude_client(api_key).messages.create(**kwargs)
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
