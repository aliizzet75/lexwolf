"""Laufzeit-umschaltbare KI-Modell-Konfiguration zum Testen (T#313) — unabhängig von der
LLM_PROVIDER-Env-Var in llm_client.py, ohne Neustart des Backends wirksam, über
GET/POST /api/llm-config (siehe api/routes/llm_config.py) aus den LexWolf-Einstellungen
("KI-Modell (Test)") gesetzt.

Drei Modi:
- "localhost": bisheriges Verhalten unverändert (LLM_PROVIDER-Env-Var entscheidet).
- "ein_modell": genau ein festes Modell (Claude Code oder Ollama Cloud) für alles.
- "hybrid": ein Claude-Code- und ein Ollama-Cloud-Modell gleichzeitig ausgewählt;
  anhand von max_tokens der jeweiligen ask_llm()-Anfrage wird automatisch das güns-
  tigere der beiden (nach Kostenpunkten aus llm_catalog) für kurze/einfache Antworten
  und das teurere für lange/aufwändige Entwürfe genommen — ohne jeden Aufrufer von
  ask_llm() einzeln mit einer Aufgabenart zu markieren.
"""
import json
import os
import threading
from pathlib import Path
from typing import Optional

from . import llm_catalog

_PATH = Path(os.environ.get(
    "LLM_CONFIG_PATH",
    str(Path(__file__).resolve().parent.parent / "data" / "llm_config.json"),
))
_lock = threading.Lock()

_DEFAULT: dict = {
    "modus": "localhost",
    "ein_modell_anbieter": "ollama",  # "claude_code" | "ollama"
    "ein_modell": "",
    "hybrid_claude_code_modell": "",
    "hybrid_ollama_modell": "",
    "claude_api_key": "",
}

# Ab diesem max_tokens-Budget gilt eine Anfrage im Hybrid-Modus als "aufwändig"
# (z.B. Schriftsatz-Entwürfe mit 8000) statt "kurz" (z.B. LLM-Judge mit 200).
SCHWELLE_AUFWAENDIG = 3000

# T#312: Diese Rollen bekommen im Hybrid-Modus IMMER das teurere der beiden
# Modelle, unabhängig von max_tokens — der LLM-Judge (max_tokens=200) würde
# sonst trotz kurzer Antwort immer auf der günstigen Seite landen, obwohl die
# Bewertungsqualität von der Modellwahl des Anwalts (Chat/Schriftsatz) abhängt.
ROLLEN_IMMER_HOCHWERTIG = {"judge", "chat", "schriftsatz"}


def laden() -> dict:
    with _lock:
        if _PATH.exists():
            try:
                gespeichert = json.loads(_PATH.read_text(encoding="utf-8"))
                return {**_DEFAULT, **gespeichert}
            except Exception:
                pass
        return dict(_DEFAULT)


def speichern(config: dict) -> dict:
    zusammengefuehrt = {**_DEFAULT, **config}
    with _lock:
        _PATH.parent.mkdir(parents=True, exist_ok=True)
        _PATH.write_text(json.dumps(zusammengefuehrt, indent=2, ensure_ascii=False), encoding="utf-8")
    return zusammengefuehrt


def kataloge() -> dict:
    return {"claude_code": llm_catalog.CLAUDE_MODELLE, "ollama": llm_catalog.OLLAMA_MODELLE}


def _claude_provider(cfg: dict) -> str:
    """"claude" (direkte Anthropic-API) sobald ein Key hinterlegt ist, bis dahin
    "ki-proxy" (lokale Claude-Code-CLI über tools/ki-proxy, kein Key nötig) —
    siehe Modulkommentar in llm_client.py."""
    return "claude" if cfg.get("claude_api_key", "").strip() else "ki-proxy"


def route(max_tokens: int, rolle: Optional[str] = None) -> tuple[Optional[str], Optional[str], Optional[str]]:
    """(provider, model, claude_api_key) für ask_llm() — provider None bedeutet
    "localhost": bisheriges LLM_PROVIDER-Verhalten unverändert verwenden.

    rolle (T#312): im Hybrid-Modus wählt eine Rolle aus ROLLEN_IMMER_HOCHWERTIG
    immer das teurere der beiden Modelle, unabhängig von max_tokens."""
    cfg = laden()
    modus = cfg.get("modus", "localhost")
    api_key = cfg.get("claude_api_key", "").strip() or None

    if modus == "ein_modell":
        modell = cfg.get("ein_modell") or None
        if not modell:
            return None, None, None
        if cfg.get("ein_modell_anbieter") == "claude_code":
            return _claude_provider(cfg), modell, api_key
        return "ollama", modell, None

    if modus == "hybrid":
        claude_modell = cfg.get("hybrid_claude_code_modell") or None
        ollama_modell = cfg.get("hybrid_ollama_modell") or None
        paar = [p for p in ((_claude_provider(cfg), claude_modell, api_key), ("ollama", ollama_modell, None)) if p[1]]
        if not paar:
            return None, None, None
        if len(paar) == 1:
            return paar[0]
        paar.sort(key=lambda p: llm_catalog.kosten(p[1]))
        if rolle in ROLLEN_IMMER_HOCHWERTIG:
            return paar[1]
        return paar[1] if max_tokens >= SCHWELLE_AUFWAENDIG else paar[0]

    return None, None, None
