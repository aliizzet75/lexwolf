import openai
import os
import numpy as np
from typing import List, Union

class EmbeddingService:
    """
    Service for generating text embeddings
    """
    
    def __init__(self):
        # Initialize OpenAI client
        openai.api_key = os.getenv("OPENAI_API_KEY", "your_openai_api_key_here")
        self.model = "text-embedding-3-small"  # 1536-dim embeddings
        self.dimensions = 1536
    
    def generate_embedding(self, text: str) -> List[float]:
        """
        Generate embedding for text using OpenAI API
        """
        try:
            # Truncate text to avoid token limits
            truncated_text = text[:8000]  # Max tokens for text-embedding-3-small
            
            response = openai.embeddings.create(
                input=truncated_text,
                model=self.model,
                dimensions=self.dimensions
            )
            
            embedding = response.data[0].embedding
            return embedding
        except Exception as e:
            print(f"Error generating embedding: {e}")
            # Return zero vector as fallback
            return [0.0] * self.dimensions
    
    def generate_embeddings_batch(self, texts: List[str]) -> List[List[float]]:
        """
        Generate embeddings for a batch of texts
        """
        try:
            # Truncate texts to avoid token limits
            truncated_texts = [text[:8000] for text in texts]
            
            response = openai.embeddings.create(
                input=truncated_texts,
                model=self.model,
                dimensions=self.dimensions
            )
            
            embeddings = [item.embedding for item in response.data]
            return embeddings
        except Exception as e:
            print(f"Error generating batch embeddings: {e}")
            # Return zero vectors as fallback
            return [[0.0] * self.dimensions for _ in texts]

# For local testing without OpenAI API
class MockEmbeddingService:
    """
    Mock service for generating random embeddings (for testing)
    """
    
    def __init__(self):
        self.dimensions = 1536
    
    def generate_embedding(self, text: str) -> List[float]:
        """
        Generate random embedding (for testing)
        """
        # Return random vector for testing
        return np.random.rand(self.dimensions).tolist()
    
    def generate_embeddings_batch(self, texts: List[str]) -> List[List[float]]:
        """
        Generate random embeddings for a batch of texts (for testing)
        """
        return [np.random.rand(self.dimensions).tolist() for _ in texts]