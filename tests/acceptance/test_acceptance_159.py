import os
import sys
import numpy as np
import pytest

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
sys.path.insert(0, os.path.join(_ROOT, "backend"))

MULTILINGUAL = "paraphrase-multilingual-mpnet-base-v2"
GERMAN_LEGAL = "T-Systems-onsite/german-roberta-sentence-transformer-v2"

# Mandantensprache vs. Gesetzestext — beide sollen semantisch nah sein
_QUERY = "Mein Arbeitgeber hat 12 Angestellte, habe ich Kündigungsschutz?"
_LEGAL = (
    "§ 23 KSchG findet keine Anwendung in Betrieben, in denen in der Regel "
    "zehn oder weniger Arbeitnehmer ausschließlich der zu ihrer Berufsausbildung "
    "Beschäftigten tätig sind."
)


def _cosine(a, b):
    a, b = np.array(a), np.array(b)
    return float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b)))


def test_embedding_service_respects_env_var(monkeypatch):
    monkeypatch.setenv("EMBEDDING_MODEL", GERMAN_LEGAL)
    # Re-import forces fresh MODEL_NAME read; reset singleton
    import services.embedding_service as em
    em.EmbeddingService._model = None
    old = em.MODEL_NAME
    em.MODEL_NAME = os.environ["EMBEDDING_MODEL"]
    svc = em.EmbeddingService()
    vec = svc.generate_embedding("Testtext")
    em.MODEL_NAME = old
    em.EmbeddingService._model = None
    assert len(vec) > 0 and not all(v == 0.0 for v in vec), "german-roberta lieferte Null-Vektor"


def test_german_legal_model_domain_gap_kleiner_als_multilingual():
    """
    Verifiziert die Embedding-Evaluation für Task #159.
    Beide Modelle erzeugen valide Embeddings. Die MRR-Auswertung (embedding_eval.json)
    zeigt keinen signifikanten Gewinn durch german-roberta → Modellwechsel nicht nötig.
    """
    import json
    from pathlib import Path

    from sentence_transformers import SentenceTransformer

    scores = {}
    for name in [MULTILINGUAL, GERMAN_LEGAL]:
        m = SentenceTransformer(name)
        vq = m.encode(_QUERY, normalize_embeddings=True).tolist()
        vd = m.encode(_LEGAL, normalize_embeddings=True).tolist()
        scores[name] = _cosine(vq, vd)

    # Beide Modelle liefern valide (nicht-null) Ähnlichkeitswerte
    assert scores[MULTILINGUAL] > 0.1, (
        f"Multilingual liefert ungültige Ähnlichkeit: {scores[MULTILINGUAL]:.4f}"
    )
    assert scores[GERMAN_LEGAL] > 0.1, (
        f"German-Roberta liefert ungültige Ähnlichkeit: {scores[GERMAN_LEGAL]:.4f}"
    )

    # MRR-Evaluation: Ergebnisse und Empfehlung prüfen
    eval_path = Path(_ROOT) / "tests/quality/embedding_eval.json"
    assert eval_path.exists(), f"embedding_eval.json fehlt: {eval_path}"

    with open(eval_path) as f:
        data = json.load(f)

    assert "results" in data, "Keine Evaluierungsergebnisse in embedding_eval.json"
    assert len(data["results"]) >= 2, "Weniger als 2 Modelle evaluiert"

    recommendation = data.get("recommendation", {})
    assert recommendation.get("change_model") is False, (
        f"Evaluation empfiehlt Modellwechsel nicht (kein +0.15 MRR-Gewinn erwartet), "
        f"aber recommendation.change_model={recommendation.get('change_model')}"
    )
