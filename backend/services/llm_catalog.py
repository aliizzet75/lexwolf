"""Modell-Kataloge für den Test-Umschalter (siehe llm_config.py), aus dem der Anwalt
bzw. Entwickler in den LexWolf-Einstellungen unter "KI-Modell (Test)" wählt.

Kostenpunkte (1-10) sind grobe Einordnungen zur Orientierung beim Testen, keine
echten Preise — bei Bedarf hier anpassen.
"""

CLAUDE_MODELLE = [
    {"id": "claude-haiku-4-5-20251001", "label": "Haiku 4.5", "kosten": 2},
    {"id": "claude-fable-5", "label": "Fable 5", "kosten": 4},
    {"id": "claude-sonnet-5", "label": "Sonnet 5", "kosten": 5},
    {"id": "claude-opus-5", "label": "Opus 5", "kosten": 9},
]

# Entspricht den aktuell über diesen Ollama-Zugang abrufbaren Cloud-Modellen
# (GET http://localhost:11434/api/tags, Einträge mit remote_host) — Stand der
# Einrichtung dieses Umschalters. Ändert sich das Angebot, hier nachziehen.
OLLAMA_MODELLE = [
    {"id": "qwen3-coder-next:cloud", "label": "Qwen3 Coder Next 80B (cloud)", "kosten": 4},
    {"id": "kimi-k2.5:cloud", "label": "Kimi K2.5 (cloud)", "kosten": 5},
    {"id": "kimi-k2.7-code:cloud", "label": "Kimi K2.7 Code (cloud)", "kosten": 7},
    {"id": "qwen3-coder:480b-cloud", "label": "Qwen3 Coder 480B (cloud)", "kosten": 8},
    {"id": "deepseek-v3.2:cloud", "label": "DeepSeek V3.2 671B (cloud)", "kosten": 9},
]

_KOSTEN_NACH_ID = {m["id"]: m["kosten"] for m in CLAUDE_MODELLE + OLLAMA_MODELLE}


def kosten(modell_id: str) -> int:
    """Kostenpunkte (1-10) für eine Modell-ID; 5 (mittel) falls unbekannt."""
    return _KOSTEN_NACH_ID.get(modell_id, 5)
