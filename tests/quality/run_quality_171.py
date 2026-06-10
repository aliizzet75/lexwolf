#!/usr/bin/env python3
"""M12 DOD-Gate: 20-Fragen-Qualitätstest für T#171."""
import json
import os
import sys

QUALITY_DIR = os.path.dirname(os.path.abspath(__file__))
BACKEND = os.path.join(QUALITY_DIR, "../../backend")
sys.path.insert(0, os.path.abspath(BACKEND))
sys.path.insert(0, QUALITY_DIR)

os.environ.setdefault("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")
os.environ.setdefault("NEO4J_URI", "bolt://localhost:7687")
os.environ.setdefault("NEO4J_USER", "neo4j")
os.environ.setdefault("NEO4J_PASSWORD", "lexwolf123")
os.environ.setdefault("HYDE_MODEL", "kimi-k2.5:cloud")
os.environ.setdefault("OLLAMA_MODEL", "kimi-k2.5:cloud")
os.environ.setdefault("OLLAMA_URL", "http://localhost:11434")

# 20 Fragen: alle §1601/§1603/§535/§307-Fragen + Restfragen bis 20
_TARGET_IDS = {5, 6, 10, 31, 32, 36, 37, 43, 78, 82, 98, 1, 2, 3, 4, 7, 8, 9, 11, 12}

DATASET_PATH = os.path.join(QUALITY_DIR, "test_dataset.json")
RESULTS_PATH = os.path.join(QUALITY_DIR, "results_171.json")

if __name__ == "__main__":
    import run_live_quality_test as rqt

    # Patch paths for T#171
    rqt.RESULTS_PATH = RESULTS_PATH

    with open(DATASET_PATH, encoding="utf-8") as f:
        full_dataset = json.load(f)

    subset = [q for q in full_dataset if q.get("id") in _TARGET_IDS]
    assert len(subset) == 20, f"Erwartet 20 Fragen, erhalten: {len(subset)}"

    import tempfile
    tmp = tempfile.NamedTemporaryFile(mode="w", suffix=".json", delete=False, encoding="utf-8")
    json.dump(subset, tmp, ensure_ascii=False)
    tmp.close()

    rqt.run_test(dataset_path=tmp.name)
    os.unlink(tmp.name)
    print(f"\nErgebnisse gespeichert: {RESULTS_PATH}")
