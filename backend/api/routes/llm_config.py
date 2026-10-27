"""T#313: GET/POST /api/llm-config für den Test-Umschalter zwischen KI-Modellen in den
LexWolf-Einstellungen ("KI-Modell (Test)") — siehe services/llm_config.py für die
eigentliche Umschalt-Logik, die ask_llm() (services/llm_client.py) verwendet."""
from fastapi import APIRouter
from pydantic import BaseModel

from services import llm_config

router = APIRouter(prefix="/api/llm-config", tags=["llm-config"])


class LlmConfigRequest(BaseModel):
    modus: str = "localhost"  # "localhost" | "hybrid" | "ein_modell"
    ein_modell_anbieter: str = "ollama"  # "claude_code" | "ollama"
    ein_modell: str = ""
    hybrid_claude_code_modell: str = ""
    hybrid_ollama_modell: str = ""
    claude_api_key: str = ""
    claude_api_key_loeschen: bool = False


def _ohne_api_key(config: dict) -> dict:
    """Der Key selbst geht nie zurück an den Client — nur ob einer hinterlegt ist."""
    hat_key = bool(config.get("claude_api_key", "").strip())
    ohne_key = {k: v for k, v in config.items() if k != "claude_api_key"}
    return {**ohne_key, "hat_claude_api_key": hat_key}


@router.get("")
async def get_config():
    return {"config": _ohne_api_key(llm_config.laden()), "kataloge": llm_config.kataloge()}


@router.post("")
async def set_config(req: LlmConfigRequest):
    # Die GET-Antwort enthält den Key nie (siehe _ohne_api_key) — ein leeres Feld beim
    # Speichern heißt deshalb "unverändert lassen", nicht "löschen". Löschen geht nur
    # über claude_api_key_loeschen=true explizit (siehe LlmConfigRequest).
    neu = req.model_dump()
    loeschen = neu.pop("claude_api_key_loeschen")
    if not neu["claude_api_key"].strip() and not loeschen:
        neu["claude_api_key"] = llm_config.laden().get("claude_api_key", "")
    gespeichert = llm_config.speichern(neu)
    return {"config": _ohne_api_key(gespeichert), "kataloge": llm_config.kataloge()}
