from fastapi import APIRouter, HTTPException
from typing import Dict, List, Optional
from pydantic import BaseModel
import logging
from services.quality_verification import QualityVerificationService
from services.feedback_service import FeedbackValidationError

router = APIRouter(prefix="/quality", tags=["quality"])

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Pydantic models for request/response
class QualityCheckRequest(BaseModel):
    content: str
    context: Optional[Dict] = None

class QualityCheckResponse(BaseModel):
    passes_quality: bool
    quality_score: float
    source_verification: Dict
    completeness_check: Dict
    hallucination_check: Dict
    timestamp: str

class MetricsResponse(BaseModel):
    total_checks: int
    passed_checks: int
    failed_checks: int
    hallucinations_detected: int
    source_accuracy: float
    completeness_rate: float

class TestsetKandidat(BaseModel):
    id: int
    kategorie: str
    review_status: Optional[str] = None
    zeitstempel: Optional[str] = None

class TestsetKandidatenResponse(BaseModel):
    kandidaten: List[TestsetKandidat]
    anzahl: int

class ReviewEntscheidungRequest(BaseModel):
    entscheidung: str  # "angenommen" | "abgelehnt"

# Initialize quality verification service
quality_service = QualityVerificationService()

@router.post("/verify", response_model=QualityCheckResponse)
async def verify_content(request: QualityCheckRequest):
    """
    Verify generated content for quality, accuracy, and hallucinations
    """
    try:
        logger.info("Starting quality verification for content")
        
        # Verify the content
        result = quality_service.verify_generated_content(
            request.content, 
            request.context
        )
        
        # Handle errors
        if "error" in result:
            raise HTTPException(status_code=500, detail=result["error"])
        
        return QualityCheckResponse(**result)
        
    except Exception as e:
        logger.error(f"Error in quality verification: {e}")
        raise HTTPException(status_code=500, detail=f"Quality verification error: {str(e)}")

@router.get("/metrics", response_model=MetricsResponse)
async def get_quality_metrics():
    """
    Get current quality verification metrics
    """
    try:
        metrics = quality_service.get_metrics()
        return MetricsResponse(**metrics)
    except Exception as e:
        logger.error(f"Error getting quality metrics: {e}")
        raise HTTPException(status_code=500, detail=f"Error getting metrics: {str(e)}")

@router.post("/reset-metrics")
async def reset_metrics():
    """
    Reset quality verification metrics
    """
    try:
        quality_service.reset_metrics()
        return {"status": "success", "message": "Metrics reset successfully"}
    except Exception as e:
        logger.error(f"Error resetting metrics: {e}")
        raise HTTPException(status_code=500, detail=f"Error resetting metrics: {str(e)}")

@router.post("/batch-verify")
async def batch_verify_contents(requests: list[QualityCheckRequest]):
    """
    Verify multiple contents in batch
    """
    try:
        results = []
        for req in requests:
            result = quality_service.verify_generated_content(
                req.content, 
                req.context
            )
            results.append(result)
        
        return {"results": results}
    except Exception as e:
        logger.error(f"Error in batch verification: {e}")
        raise HTTPException(status_code=500, detail=f"Batch verification error: {str(e)}")

@router.get("/testset-kandidaten", response_model=TestsetKandidatenResponse)
async def get_testset_kandidaten(status: Optional[str] = "offen"):
    """
    T#298: Review-Queue für Ali - Anwalt-Korrekturen aus dem M5-Feedback-Loop,
    die eine fachliche (nicht nur stilistische) Änderung enthalten und als
    Kandidat für das 100-Fragen-Testset markiert wurden.
    `status=offen` (Default) zeigt nur unentschiedene Kandidaten,
    `status=angenommen`/`abgelehnt` zeigt bereits entschiedene,
    `status` leer/None zeigt alle.
    """
    try:
        kandidaten = quality_service.get_testset_kandidaten(status=status)
        return TestsetKandidatenResponse(kandidaten=kandidaten, anzahl=len(kandidaten))
    except Exception as e:
        logger.error(f"Error fetching testset candidates: {e}")
        raise HTTPException(status_code=500, detail=f"Error fetching testset candidates: {str(e)}")

@router.post("/testset-kandidaten/{kandidat_id}/review")
async def review_testset_kandidat(kandidat_id: int, request: ReviewEntscheidungRequest):
    """
    T#298: Ali trifft die Review-Entscheidung für einen Testset-Kandidaten.
    Erst nach "angenommen" darf der Fall ins offizielle 100-Fragen-Testset
    (tests/quality/test_dataset.json) übernommen werden.
    """
    try:
        ergebnis = quality_service.review_testset_kandidat(kandidat_id, request.entscheidung)
        return {"status": "gespeichert", **ergebnis}
    except FeedbackValidationError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Error reviewing testset candidate: {e}")
        raise HTTPException(status_code=500, detail=f"Error reviewing testset candidate: {str(e)}")

@router.get("/health")
async def health_check():
    """
    Health check endpoint
    """
    return {"status": "healthy", "service": "quality_verification"}

# Example usage
if __name__ == "__main__":
    # This would typically be run as part of the FastAPI app
    pass