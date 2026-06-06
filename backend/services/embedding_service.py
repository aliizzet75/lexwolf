import os
import numpy as np
from typing import List
from sentence_transformers import SentenceTransformer

MODEL_NAME = os.getenv(
    "EMBEDDING_MODEL",
    "paraphrase-multilingual-mpnet-base-v2"  # 768-dim, Deutsch-fähig, lokal
)
DIMENSIONS = 768


class EmbeddingService:
    """Lokale Embeddings via sentence-transformers — kein API-Key nötig."""

    _model: SentenceTransformer | None = None  # lazy singleton

    def __init__(self):
        self.dimensions = DIMENSIONS

    @property
    def model(self) -> SentenceTransformer:
        if EmbeddingService._model is None:
            EmbeddingService._model = SentenceTransformer(MODEL_NAME)
        return EmbeddingService._model

    def generate_embedding(self, text: str) -> List[float]:
        try:
            vec = self.model.encode(text[:8000], normalize_embeddings=True)
            return vec.tolist()
        except Exception as e:
            print(f"Embedding error: {e}")
            return [0.0] * self.dimensions

    def generate_embeddings_batch(self, texts: List[str]) -> List[List[float]]:
        try:
            truncated = [t[:8000] for t in texts]
            vecs = self.model.encode(truncated, normalize_embeddings=True, batch_size=32)
            return [v.tolist() for v in vecs]
        except Exception as e:
            print(f"Batch embedding error: {e}")
            return [[0.0] * self.dimensions for _ in texts]


class MockEmbeddingService:
    """Für Tests — zufällige Vektoren, kein Modell nötig."""

    def __init__(self):
        self.dimensions = DIMENSIONS

    def generate_embedding(self, text: str) -> List[float]:
        return np.random.rand(self.dimensions).tolist()

    def generate_embeddings_batch(self, texts: List[str]) -> List[List[float]]:
        return [np.random.rand(self.dimensions).tolist() for _ in texts]
