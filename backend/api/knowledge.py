from fastapi import APIRouter, HTTPException, Depends
from typing import List, Optional, Dict, Any
from pydantic import BaseModel
import logging
from services.search_service import HybridSearchService
from services.database_service import DatabaseService
from services.embedding_service import EmbeddingService

router = APIRouter(prefix="/knowledge", tags=["knowledge"])

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Pydantic models for request/response
class KnowledgeSearchRequest(BaseModel):
    query: str
    limit: Optional[int] = 10
    legal_field: Optional[str] = None
    source: Optional[str] = None

class KnowledgeSearchResult(BaseModel):
    id: int
    text: str
    title: str
    score: float
    source: str
    url: str
    legal_field: Optional[str] = None
    court: Optional[str] = None
    case_number: Optional[str] = None
    date: Optional[str] = None

class KnowledgeChunkRequest(BaseModel):
    text: str
    title: str
    source: str
    legal_field: Optional[str] = None
    court: Optional[str] = None
    case_number: Optional[str] = None
    date: Optional[str] = None
    tags: Optional[str] = None
    parent_id: Optional[int] = None
    is_parent: Optional[bool] = False

class KnowledgeChunkResponse(BaseModel):
    id: int
    text: str
    title: str
    source: str
    legal_field: Optional[str] = None
    court: Optional[str] = None
    case_number: Optional[str] = None
    date: Optional[str] = None
    tags: Optional[str] = None
    chunk_hash: str
    parent_id: Optional[int] = None
    is_parent: bool
    created_at: str

class KnowledgeStatsResponse(BaseModel):
    total_documents: int
    total_chunks: int
    sources: Dict[str, int]
    legal_fields: Dict[str, int]

# Initialize services
search_service = HybridSearchService()
database_service = DatabaseService()
embedding_service = EmbeddingService()

@router.post("/search", response_model=List[KnowledgeSearchResult])
async def search_knowledge(request: KnowledgeSearchRequest):
    """
    Perform semantic search on legal knowledge database
    """
    try:
        logger.info(f"Searching knowledge database for query: {request.query}")
        
        # Perform hybrid search
        results = search_service.search(request.query, request.limit)
        
        # Filter by legal field and source if specified
        filtered_results = []
        for result in results:
            # In a real implementation, we would filter by legal_field and source
            # For now, we'll just return all results
            filtered_results.append(KnowledgeSearchResult(**result))
        
        logger.info(f"Found {len(filtered_results)} results")
        return filtered_results
    except Exception as e:
        logger.error(f"Error searching knowledge database: {e}")
        raise HTTPException(status_code=500, detail=f"Search error: {str(e)}")

@router.post("/chunks", response_model=KnowledgeChunkResponse)
async def store_knowledge_chunk(request: KnowledgeChunkRequest):
    """
    Store a knowledge chunk in the database
    """
    try:
        logger.info(f"Storing knowledge chunk: {request.title}")
        
        # Generate embedding for the chunk
        embedding = embedding_service.generate_embedding(request.text)
        
        # Prepare chunk data
        chunk_data = {
            "text": request.text,
            "title": request.title,
            "source": request.source,
            "legal_field": request.legal_field,
            "court": request.court,
            "case_number": request.case_number,
            "date": request.date,
            "tags": request.tags,
            "vector": embedding,
            "parent_id": request.parent_id,
            "is_parent": request.is_parent,
            "chunk_hash": str(hash(request.text + request.title))  # Simple hash for deduplication
        }
        
        # Store chunk in database
        database_service.store_chunk(chunk_data)
        
        # Return the stored chunk (in a real implementation, we would fetch it from DB)
        return KnowledgeChunkResponse(
            id=abs(hash(request.text + request.title)),  # Simple ID for demo
            text=request.text,
            title=request.title,
            source=request.source,
            legal_field=request.legal_field,
            court=request.court,
            case_number=request.case_number,
            date=request.date,
            tags=request.tags,
            chunk_hash=str(hash(request.text + request.title)),
            parent_id=request.parent_id,
            is_parent=request.is_parent,
            created_at="2023-01-01T00:00:00Z"  # Placeholder
        )
    except Exception as e:
        logger.error(f"Error storing knowledge chunk: {e}")
        raise HTTPException(status_code=500, detail=f"Error storing chunk: {str(e)}")

@router.get("/chunks/{chunk_id}", response_model=KnowledgeChunkResponse)
async def get_knowledge_chunk(chunk_id: int):
    """
    Get a specific knowledge chunk by ID
    """
    try:
        logger.info(f"Fetching knowledge chunk: {chunk_id}")
        
        # Get chunk from database
        chunk = database_service.get_chunk_by_id(chunk_id)
        
        if chunk:
            # Ensure all required fields are present
            required_fields = ["source"]
            for field in required_fields:
                if field not in chunk or chunk[field] is None:
                    chunk[field] = ""
            
            return KnowledgeChunkResponse(**chunk)
        else:
            raise HTTPException(status_code=404, detail="Chunk not found")
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error fetching knowledge chunk: {e}")
        raise HTTPException(status_code=500, detail=f"Error fetching chunk: {str(e)}")

@router.get("/stats", response_model=KnowledgeStatsResponse)
async def get_knowledge_stats():
    """
    Get statistics about the knowledge database
    """
    try:
        logger.info("Fetching knowledge database statistics")
        
        # In a real implementation, we would query the database for stats
        # For now, return placeholder data
        return KnowledgeStatsResponse(
            total_documents=1000,
            total_chunks=5000,
            sources={
                "gesetze-im-internet.de": 300,
                "openjur.de": 700
            },
            legal_fields={
                "Arbeitsrecht": 200,
                "Mietrecht": 150,
                "Familienrecht": 100
            }
        )
    except Exception as e:
        logger.error(f"Error fetching knowledge statistics: {e}")
        raise HTTPException(status_code=500, detail=f"Error fetching stats: {str(e)}")

@router.get("/health")
async def health_check():
    """
    Health check endpoint
    """
    return {"status": "healthy", "service": "knowledge_database"}

# Example usage and testing
if __name__ == "__main__":
    # This would typically be run as part of the FastAPI app
    pass