from typing import List, Dict
from services.embedding_service import EmbeddingService
from services.database_service import DatabaseService
from sqlalchemy import text
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
        Perform dense vector search using pgvector cosine similarity with <-> operator
        """
        try:
            logger.info(f"Performing dense search with pgvector cosine similarity")
            
            # Get database session
            db = self.database_service.SessionLocal()
            
            # Execute pgvector cosine similarity search using <-> operator
            # The <-> operator computes cosine distance (1 - cosine similarity)
            # So lower scores are better (more similar)
            query = text("""
                SELECT *, vector <-> :vec AS score 
                FROM legal_chunks 
                ORDER BY score 
                LIMIT :k
            """)
            
            result = db.execute(query, {"vec": query_vector, "k": limit})
            rows = result.fetchall()
            
            # Convert rows to dictionaries
            results = []
            for i, row in enumerate(rows):
                result_dict = {
                    "id": row.id,
                    "document_id": row.document_id,
                    "text": row.text,
                    "title": row.title,
                    "court": row.court,
                    "case_number": row.case_number,
                    "date": row.date.isoformat() if row.date else None,
                    "legal_field": row.legal_field,
                    "tags": row.tags,
                    "chunk_hash": row.chunk_hash,
                    "parent_id": row.parent_id,
                    "is_parent": row.is_parent,
                    "dense_score": float(row.score),  # Cosine distance (lower is better)
                    "dense_rank": i + 1,
                    "created_at": row.created_at.isoformat() if row.created_at else None
                }
                results.append(result_dict)
            
            logger.info(f"Dense search completed with {len(results)} results")
            return results
            
        except Exception as e:
            logger.error(f"Error in dense search: {e}")
            return []
        finally:
            db.close()
    
    def _sparse_search(self, query: str, limit: int = 10) -> List[Dict]:
        """
        Perform sparse keyword search using PostgreSQL Full-Text Search
        """
        try:
            logger.info(f"Performing sparse search with PostgreSQL FTS for query: {query}")
            
            # Get database session
            db = self.database_service.SessionLocal()
            
            # Execute PostgreSQL FTS search using plainto_tsquery for exact matches
            # This is particularly important for legal citations like '§ 1 KSchG'
            query_sql = text("""
                SELECT *, 
                       ts_rank(ts_vector, plainto_tsquery('german', :query)) AS sparse_score
                FROM legal_chunks 
                WHERE ts_vector @@ plainto_tsquery('german', :query)
                ORDER BY ts_rank(ts_vector, plainto_tsquery('german', :query)) DESC
                LIMIT :k
            """)
            
            result = db.execute(query_sql, {"query": query, "k": limit})
            rows = result.fetchall()
            
            # Convert rows to dictionaries
            results = []
            for i, row in enumerate(rows):
                result_dict = {
                    "id": row.id,
                    "document_id": row.document_id,
                    "text": row.text,
                    "title": row.title,
                    "court": row.court,
                    "case_number": row.case_number,
                    "date": row.date.isoformat() if row.date else None,
                    "legal_field": row.legal_field,
                    "tags": row.tags,
                    "chunk_hash": row.chunk_hash,
                    "parent_id": row.parent_id,
                    "is_parent": row.is_parent,
                    "sparse_score": float(row.sparse_score),  # FTS relevance score
                    "sparse_rank": i + 1,
                    "created_at": row.created_at.isoformat() if row.created_at else None
                }
                results.append(result_dict)
            
            logger.info(f"Sparse search completed with {len(results)} results")
            return results
            
        except Exception as e:
            logger.error(f"Error in sparse search: {e}")
            return []
        finally:
            db.close()
    
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