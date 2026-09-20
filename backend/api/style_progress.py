import logging
from typing import Any, Dict

from fastapi import APIRouter

from services.style_learner import StyleLearner

router = APIRouter(prefix="/api/style", tags=["style"])
logger = logging.getLogger(__name__)


@router.get("/progress")
async def get_style_progress() -> Dict[str, Any]:
    """
    Gibt den aktuellen Lern-Fortschritt für den Anwaltsstil zurück.
    Werte sind heuristisch und dienen der Motivation / Transparenz.
    """
    learner = StyleLearner()
    return learner.berechne_lernfortschritt()
