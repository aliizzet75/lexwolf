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

# Per Hand gegen http://localhost:11434/v1/chat/completions geprüfte, tatsächlich
# erreichbare Cloud-Modelle (Stand 2026-10-27) — die vorherige Liste (Qwen3-Coder,
# Kimi K2.5, DeepSeek V3.2) war zu diesem Zeitpunkt bereits von Ollama retired
# (HTTP 410 "was retired on ..."), was bei Nutzern als 500er ankam. Ändert sich
# das Angebot wieder, hier nachziehen und neu gegenprüfen statt nur aus /api/tags
# zu übernehmen — dort stehen auch längst retirete Modelle noch drin.
OLLAMA_MODELLE = [
    {"id": "gpt-oss:20b-cloud", "label": "GPT-OSS 20B (cloud)", "kosten": 3},
    {"id": "glm-5.3-flash:cloud", "label": "GLM 5.3 Flash (cloud)", "kosten": 3},
    {"id": "kimi-k2.7-code:cloud", "label": "Kimi K2.7 Code (cloud)", "kosten": 6},
    {"id": "glm-5.3:cloud", "label": "GLM 5.3 (cloud)", "kosten": 6},
    {"id": "gpt-oss:120b-cloud", "label": "GPT-OSS 120B (cloud)", "kosten": 7},
]

_KOSTEN_NACH_ID = {m["id"]: m["kosten"] for m in CLAUDE_MODELLE + OLLAMA_MODELLE}


def kosten(modell_id: str) -> int:
    """Kostenpunkte (1-10) für eine Modell-ID; 5 (mittel) falls unbekannt."""
    return _KOSTEN_NACH_ID.get(modell_id, 5)
