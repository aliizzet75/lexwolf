from fastapi import APIRouter, HTTPException
from typing import Dict, Optional
from pydantic import BaseModel
import logging
from services.quality_verification import QualityVerificationService

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