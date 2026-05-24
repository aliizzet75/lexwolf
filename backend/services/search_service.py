from typing import List, Dict
from services.embedding_service import EmbeddingService
from services.database_service import DatabaseService
import logging

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class HybridSearchService:
    """
    Hybrid search service combining dense vector search and sparse keyword search
    """
    
    def __init__(self):
        self.embedding_service = EmbeddingService()
        self.database_service = DatabaseService()
    
    def search(self, query: str, limit: int = 10) -> List[Dict]:
        """
        Perform hybrid search combining dense vector search and sparse keyword search
        """
        try:
            logger.info(f"Performing hybrid search for query: {query}")
            
            # Step 1: Generate embedding for the query (HyDE approach)
            query_embedding = self.embedding_service.generate_embedding(query)
            logger.info("Generated query embedding")
            
            # Step 2: Perform dense vector search
            dense_results = self._dense_search(query_embedding, limit * 2)
            logger.info(f"Dense search returned {len(dense_results)} results")
            
            # Step 3: Perform sparse keyword search
            sparse_results = self._sparse_search(query, limit * 2)
            logger.info(f"Sparse search returned {len(sparse_results)} results")
            
            # Step 4: Combine results using Reciprocal Rank Fusion (RRF)
            fused_results = self._reciprocal_rank_fusion(dense_results, sparse_results, limit)
            logger.info(f"RRF fusion returned {len(fused_results)} results")
            
            return fused_results
            
        except Exception as e:
            logger.error(f"Error in hybrid search: {e}")
            # Fallback to simple database search
            return self.database_service.search_chunks_hybrid(query, limit)
    
    def _dense_search(self, query_vector: List[float], limit: int = 10) -> List[Dict]:
        """
        Perform dense vector search
        In a real implementation, this would use LanceDB or similar
        For now, we'll simulate it by returning database results with scores
        """
        try:
            # For demonstration, return database results with simulated scores
            results = self.database_service.search_chunks_hybrid("", limit)
            
            # Add dense search scores (simulated)
            for i, result in enumerate(results):
                result["dense_score"] = 1.0 / (i + 1)  # Simulated score
                result["dense_rank"] = i + 1
            
            return results
        except Exception as e:
            logger.error(f"Error in dense search: {e}")
            return []
    
    def _sparse_search(self, query: str, limit: int = 10) -> List[Dict]:
        """
        Perform sparse keyword search (BM25-like)
        """
        try:
            # For demonstration, return database results with simulated scores
            results = self.database_service.search_chunks_hybrid(query, limit)
            
            # Add sparse search scores (simulated)
            for i, result in enumerate(results):
                result["sparse_score"] = 1.0 / (i + 1)  # Simulated score
                result["sparse_rank"] = i + 1
            
            return results
        except Exception as e:
            logger.error(f"Error in sparse search: {e}")
            return []
    
    def _reciprocal_rank_fusion(self, dense_results: List[Dict], sparse_results: List[Dict], limit: int = 10) -> List[Dict]:
        """
        Combine dense and sparse search results using Reciprocal Rank Fusion (RRF)
        """
        try:
            # Create a dictionary to store fused scores
            fused_scores = {}
            result_lookup = {}
            
            # Process dense results
            for result in dense_results:
                chunk_id = result.get("id", hash(result.get("text", "")))
                dense_rank = result.get("dense_rank", 1)
                # RRF formula: 1 / (rank + k) where k is a constant (usually 60)
                fused_scores[chunk_id] = 1 / (dense_rank + 60)
                result_lookup[chunk_id] = result
            
            # Process sparse results
            for result in sparse_results:
                chunk_id = result.get("id", hash(result.get("text", "")))
                sparse_rank = result.get("sparse_rank", 1)
                if chunk_id in fused_scores:
                    # Add to existing score
                    fused_scores[chunk_id] += 1 / (sparse_rank + 60)
                else:
                    # Create new score
                    fused_scores[chunk_id] = 1 / (sparse_rank + 60)
                    result_lookup[chunk_id] = result
            
            # Sort by fused score
            sorted_results = sorted(fused_scores.items(), key=lambda x: x[1], reverse=True)
            
            # Return top results with combined scores
            top_results = []
            for chunk_id, score in sorted_results[:limit]:
                if chunk_id in result_lookup:
                    result = result_lookup[chunk_id].copy()
                    result["score"] = score  # Combined RRF score
                    # Ensure all required fields are present
                    if "legal_field" not in result:
                        result["legal_field"] = None
                    if "court" not in result:
                        result["court"] = None
                    if "case_number" not in result:
                        result["case_number"] = None
                    if "date" not in result:
                        result["date"] = None
                    top_results.append(result)
            
            return top_results
        except Exception as e:
            logger.error(f"Error in RRF fusion: {e}")
            return []