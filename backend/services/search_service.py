import lancedb
import pandas as pd
from typing import List, Dict
import numpy as np
from services.embedding_service import EmbeddingService
from services.database_service import DatabaseService

class HybridSearchService:
    """
    Hybrid search service combining dense vector search and sparse keyword search
    """
    
    def __init__(self):
        self.embedding_service = EmbeddingService()
        self.database_service = DatabaseService()
        
        # Initialize LanceDB
        self.db = lancedb.connect("/tmp/lexwolf_lancedb")
        
        # Create or open tables
        try:
            self.chunk_table = self.db.open_table("legal_chunks")
        except:
            # Create table if it doesn't exist
            schema = {
                "id": int,
                "text": str,
                "vector": np.array([0.0] * 1536),
                "title": str,
                "source": str,
                "legal_field": str,
                "tags": str
            }
            self.chunk_table = self.db.create_table("legal_chunks", schema=schema)
    
    def hyde_search(self, query: str, limit: int = 10) -> List[Dict]:
        """
        HyDE (Hypothetical Document Embeddings) search
        """
        try:
            # Generate hypothetical answer to the query
            hypothetical_answer = self._generate_hypothetical_answer(query)
            
            # Generate embedding for the hypothetical answer
            query_embedding = self.embedding_service.generate_embedding(hypothetical_answer)
            
            # Perform dense search
            results = self._dense_search(query_embedding, limit)
            
            return results
        except Exception as e:
            print(f"Error in HyDE search: {e}")
            return []
    
    def _generate_hypothetical_answer(self, query: str) -> str:
        """
        Generate a hypothetical answer to the query
        In a real implementation, this would use an LLM
        """
        # For now, just return the query as the hypothetical answer
        return query
    
    def _dense_search(self, query_vector: List[float], limit: int = 10) -> List[Dict]:
        """
        Perform dense vector search using LanceDB
        """
        try:
            # Search using LanceDB
            results = self.chunk_table.search(query_vector).limit(limit).to_list()
            
            return results
        except Exception as e:
            print(f"Error in dense search: {e}")
            return []
    
    def bm25_search(self, query: str, limit: int = 10) -> List[Dict]:
        """
        Perform BM25 keyword search
        """
        try:
            # For now, simulate BM25 search by searching in database
            # In a real implementation, this would use a proper BM25 engine
            chunks = self.database_service.search_chunks_hybrid(query, limit)
            
            # Add rank for RRF calculation
            for i, chunk in enumerate(chunks):
                chunk["bm25_rank"] = i + 1
            
            return chunks
        except Exception as e:
            print(f"Error in BM25 search: {e}")
            return []
    
    def reciprocal_rank_fusion(self, dense_results: List[Dict], sparse_results: List[Dict], limit: int = 10) -> List[Dict]:
        """
        Combine dense and sparse search results using Reciprocal Rank Fusion (RRF)
        """
        try:
            # Create a dictionary to store fused scores
            fused_scores = {}
            
            # Process dense results
            for i, result in enumerate(dense_results):
                chunk_id = result.get("id", f"dense_{i}")
                # RRF formula: 1 / (rank + k) where k is a constant (usually 60)
                fused_scores[chunk_id] = 1 / (i + 1 + 60)
            
            # Process sparse results
            for result in sparse_results:
                chunk_id = result.get("id", result.get("chunk_id"))
                bm25_rank = result.get("bm25_rank", 1)
                if chunk_id in fused_scores:
                    # Add to existing score
                    fused_scores[chunk_id] += 1 / (bm25_rank + 60)
                else:
                    # Create new score
                    fused_scores[chunk_id] = 1 / (bm25_rank + 60)
            
            # Sort by fused score
            sorted_results = sorted(fused_scores.items(), key=lambda x: x[1], reverse=True)
            
            # Return top results
            top_results = []
            for chunk_id, score in sorted_results[:limit]:
                # In a real implementation, we would fetch the actual chunk data
                top_results.append({
                    "id": chunk_id,
                    "score": score
                })
            
            return top_results
        except Exception as e:
            print(f"Error in RRF fusion: {e}")
            return []
    
    def search(self, query: str, limit: int = 10) -> List[Dict]:
        """
        Perform hybrid search combining HyDE, dense search, BM25, and RRF
        """
        try:
            # Step 1: HyDE - Generate hypothetical answer and embedding
            print("Step 1: HyDE search")
            hyde_results = self.hyde_search(query, limit * 2)  # Get more results for fusion
            
            # Step 2: BM25 - Keyword search
            print("Step 2: BM25 search")
            bm25_results = self.bm25_search(query, limit * 2)
            
            # Step 3: RRF - Reciprocal Rank Fusion
            print("Step 3: RRF fusion")
            fused_results = self.reciprocal_rank_fusion(hyde_results, bm25_results, limit)
            
            # Step 4: Return results
            return fused_results
            
        except Exception as e:
            print(f"Error in hybrid search: {e}")
            return []