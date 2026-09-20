import logging
from typing import Optional, Dict, Any

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from services.feedback_service import FeedbackService, FeedbackValidationError

router = APIRouter(prefix="/api", tags=["feedback"])
logger = logging.getLogger(__name__)

_feedback_service: Optional[FeedbackService] = None


def get_feedback_service() -> FeedbackService:
    global _feedback_service
    if _feedback_service is None:
        _feedback_service = FeedbackService()
    return _feedback_service


class LegacyFeedbackRequest(BaseModel):
    original: str
    korrigiert: str
    zeitstempel: Optional[str] = None


class MetricsFeedbackRequest(BaseModel):
    satzlaengen_verteilung: Dict[str, int]
    wortklassen_haeufigkeiten: Dict[str, int]
    korrektur_kategorien: Dict[str, int]
    zeitstempel: Optional[str] = None


@router.post("/feedback/metrics", status_code=201)
async def submit_metrics_feedback(payload: MetricsFeedbackRequest):
    """
    Nimmt ausschließlich aggregierte, DSGVO-konforme Stil-Metriken entgegen.
    Es werden KEINE Original- oder Korrigiert-Texte akzeptiert oder gespeichert.
    """
    try:
        eintrag = get_feedback_service().process_metrics_feedback(payload.dict())
        return {"status": "gespeichert", **eintrag}
    except FeedbackValidationError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Fehler bei Metrik-Feedback-Verarbeitung: {e}")
        raise HTTPException(status_code=500, detail="Interner Fehler bei Feedback-Verarbeitung")


@router.post("/feedback", status_code=201)
async def submit_feedback(payload: LegacyFeedbackRequest):
    """
    Legacy-Endpunkt: akzeptiert weiterhin anonymisierte Diffs, speichert aber
    KEINE Rohtexte mehr auf dem Server. Stattdessen wird nur die abgeleitete
    Kategorie persistiert, um die DSGVO-Anforderung zu erfüllen.
    """
    try:
        eintrag = get_feedback_service().process_feedback(payload.dict())
        return {"status": "gespeichert", **eintrag}
    except FeedbackValidationError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Fehler bei Feedback-Verarbeitung: {e}")
        raise HTTPException(status_code=500, detail="Interner Fehler bei Feedback-Verarbeitung")
