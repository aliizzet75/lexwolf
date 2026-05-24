from fastapi import APIRouter, HTTPException, Depends
from typing import List, Optional
from pydantic import BaseModel
from services.search_service import HybridSearchService
from services.database_service import DatabaseService
from services.embedding_service import EmbeddingService

router = APIRouter(prefix="/legal-db", tags=["legal_database"])

# Pydantic models for request/response
class SearchRequest(BaseModel):
    query: str
    limit: Optional[int] = 10

class SearchResult(BaseModel):
    id: int
    text: str
    title: str
    score: float
    source: str
    url: str

class ChunkResponse(BaseModel):
    id: int
    document_id: int
    text: str
    title: str
    court: Optional[str]
    case_number: Optional[str]
    date: Optional[str]
    legal_field: Optional[str]
    tags: Optional[str]
    chunk_hash: str
    parent_id: Optional[int]
    is_parent: bool
    created_at: str

class StyleProfileRequest(BaseModel):
    profile_id: str
    vector: List[float]

# Initialize services
search_service = HybridSearchService()
database_service = DatabaseService()
embedding_service = EmbeddingService()

@router.post("/search", response_model=List[SearchResult])
async def search_legal_database(request: SearchRequest):
    """
    Perform hybrid search on legal database
    """
    try:
        results = search_service.search(request.query, request.limit)
        return results
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Search error: {str(e)}")

@router.get("/chunks/{chunk_id}", response_model=ChunkResponse)
async def get_chunk(chunk_id: int):
    """
    Get a specific chunk by ID
    """
    try:
        chunk = database_service.get_chunk_by_id(chunk_id)
        if chunk:
            return chunk
        else:
            raise HTTPException(status_code=404, detail="Chunk not found")
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error fetching chunk: {str(e)}")

@router.post("/style-profiles")
async def store_style_profile(request: StyleProfileRequest):
    """
    Store a style profile
    """
    try:
        database_service.store_style_profile(request.profile_id, request.vector)
        return {"message": "Style profile stored successfully"}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error storing style profile: {str(e)}")

@router.get("/health")
async def health_check():
    """
    Health check endpoint
    """
    return {"status": "healthy", "service": "legal_database"}