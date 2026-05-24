from typing import List, Dict
from services.embedding_service import EmbeddingService
from services.database_service import DatabaseService

class HybridSearchService:
    """
    Simplified hybrid search service for testing
    """
    
    def __init__(self):
        # Don't initialize LanceDB for now
        self.embedding_service = EmbeddingService()
        self.database_service = DatabaseService()
    
    def search(self, query: str, limit: int = 10) -> List[Dict]:
        """
        Simple search implementation
        """
        try:
            # For now, just return database search results
            results = self.database_service.search_chunks_hybrid(query, limit)
            return results
        except Exception as e:
            print(f"Error in search: {e}")
            return []