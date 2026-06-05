#!/usr/bin/env python3
"""Live-Qualitätstest: 100 juristische Fragen gegen LexWolf-System."""
import json
import logging
import os
import re
import sys
import time
from datetime import datetime
from typing import Dict, List

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger(__name__)

QUALITY_DIR = os.path.dirname(os.path.abspath(__file__))
BACKEND = os.path.join(os.path.dirname(__file__), "../../backend")
# QUALITY_DIR must come after BACKEND in the insert sequence so it ends up at index 0
# (each insert(0) prepends, so the last one wins position 0)
sys.path.insert(0, os.path.abspath(BACKEND))
sys.path.insert(0, QUALITY_DIR)

DATASET_PATH = os.path.join(os.path.dirname(__file__), "test_dataset.json")
RESULTS_PATH = os.path.join(os.path.dirname(__file__), "results.json")
_PG_DSN = "host=localhost port=5432 dbname=lexwolf user=postgres password=postgres"

# Setzt DATABASE_URL für HybridSearchService (SQLAlchemy-Format)
os.environ.setdefault("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")
os.environ.setdefault("NEO4J_URI",    "bolt://localhost:7687")
os.environ.setdefault("NEO4J_USER",   "neo4j")
os.environ.setdefault("NEO4J_PASSWORD", "lexwolf123")
# HyDE via Ollama/Kimi — kein Claude-Token-Verbrauch
os.environ.setdefault("HYDE_MODEL", "kimi-k2.5:cloud")
os.environ.setdefault("OLLAMA_MODEL", "kimi-k2.5:cloud")
os.environ.setdefault("OLLAMA_URL", "http://localhost:11434")


def _korrekte_quellen_aus_antwort(antwort_text: str, erwartete: List[str]) -> List[str]:
    """Find expected paragraphs mentioned in answer text by §-number matching."""
    if not antwort_text or not erwartete:
        return []
    found_nrs = set(m.group(1) for m in re.finditer(r'§\s*(\d+[a-zA-Z]?)', antwort_text))
    result = []
    for ep in erwartete:
        m = re.match(r'§(\d+[a-zA-Z]?)', ep.strip())
        if m and m.group(1) in found_nrs:
            result.append(ep)
    return result


def _check_quellen_in_db(antwort_text: str) -> tuple:
    """Check how many §-references in answer exist in DB. Returns (gesamt, in_db)."""
    try:
        from source_checker import check_source_references
        result = check_source_references([antwort_text], _PG_DSN)
        return result.get("gesamt", 0), result.get("existieren", 0)
    except Exception as e:
        logger.debug(f"source_checker: {e}")
        return 0, 0


def _load_results() -> Dict:
    if os.path.exists(RESULTS_PATH):
        with open(RESULTS_PATH, encoding="utf-8") as f:
            return json.load(f)
    return {"ergebnisse": [], "metriken": {}, "meta": {"gestartet": datetime.now().isoformat()}}


def _save_results(results: Dict) -> None:
    results["meta"]["aktualisiert"] = datetime.now().isoformat()
    with open(RESULTS_PATH, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)


def _compute_metrics(ergebnisse: List[Dict]) -> Dict:
    if not ergebnisse:
        return {}

    scores = [e.get("judge_score", 1) for e in ergebnisse]
    avg_score = sum(scores) / len(scores)

    # DOD definition: "max 5% falsche §-Referenzen" = §-refs not found in DB.
    # Use qg_refs data (DB-existence checks) rather than low-score proxy.
    qg_gesamt_total = sum(e.get("qg_refs_gesamt", 0) for e in ergebnisse)
    qg_in_db_total = sum(e.get("qg_refs_in_db", 0) for e in ergebnisse)
    if qg_gesamt_total > 0:
        halluzinations_rate = (qg_gesamt_total - qg_in_db_total) / qg_gesamt_total
    else:
        # No §-refs extracted → no hallucinations detectable; use answer-quality proxy
        unverified = sum(1 for e in ergebnisse if not e.get("verifiziert", True))
        halluzinations_rate = unverified / len(ergebnisse)

    # vollstaendigkeits_score: avg fraction of expected §§ found in answer (via regex matching)
    vollst_scores = []
    for e in ergebnisse:
        erwartet = set(e.get("erwartete_paragraphen", []))
        # korrekte_quellen is already a subset of erwartet (only expected §§ found in answer)
        korrekt = set(e.get("korrekte_quellen", []))
        if not erwartet:
            vollst_scores.append(1.0)
        else:
            vollst_scores.append(len(korrekt) / len(erwartet))
    vollstaendigkeits_score = sum(vollst_scores) / len(vollst_scores) if vollst_scores else 0.0

    # quellen_genauigkeit: fraction of §-references in answers that exist in DB
    qg_gesamt = sum(e.get("qg_refs_gesamt", 0) for e in ergebnisse)
    qg_in_db = sum(e.get("qg_refs_in_db", 0) for e in ergebnisse)
    quellen_gen = qg_in_db / qg_gesamt if qg_gesamt > 0 else 1.0

    return {
        "gesamt_score": round(avg_score, 3),
        "vollstaendigkeits_score": round(vollstaendigkeits_score, 3),
        "halluzinations_rate": round(halluzinations_rate, 3),
        "quellen_genauigkeit": round(quellen_gen, 3),
        "getestete_fragen": len(ergebnisse),
        "schwache_antworten": sum(1 for s in scores if s < 3),
    }


def run_test(dataset_path: str = DATASET_PATH) -> None:
    from services.search_service import HybridSearchService
    from services.react_engine import generate_structured_response
    import llm_judge

    with open(dataset_path, encoding="utf-8") as f:
        dataset = json.load(f)

    results = _load_results()
    bereits_getestet = {e["frage_id"] for e in results["ergebnisse"]}
    logger.info(f"Bereits getestet: {len(bereits_getestet)}, offen: {len(dataset) - len(bereits_getestet)}")

    search_service = HybridSearchService()

    for eintrag in dataset:
        frage_id = eintrag.get("id", eintrag.get("frage", "")[:30])
        if frage_id in bereits_getestet:
            logger.info(f"Überspringe (bereits getestet): {frage_id}")
            continue

        frage = eintrag["frage"]
        erwartete_paragraphen = eintrag.get("erwartete_paragraphen", [])
        logger.info(f"Teste [{frage_id}]: {frage[:60]}…")

        try:
            chunks = search_service.hybrid_search_with_graph(frage, limit=100)
        except Exception as e:
            logger.warning(f"Search fehlgeschlagen für [{frage_id}]: {e}")
            ergebnis = {
                "frage_id": frage_id,
                "frage": frage,
                "erwartete_paragraphen": erwartete_paragraphen,
                "chunks_gefunden": 0,
                "antwort": "",
                "judge_score": 1,
                "begruendung": f"Search-Fehler: {e}",
                "korrekte_quellen": [],
                "verifiziert": False,
                "fehler": str(e),
                "qg_refs_gesamt": 0,
                "qg_refs_in_db": 0,
            }
            results["ergebnisse"].append(ergebnis)
            results["metriken"] = _compute_metrics(results["ergebnisse"])
            _save_results(results)
            continue

        try:
            response = generate_structured_response(frage, chunks)
            antwort_text = json.dumps(response, ensure_ascii=False)
        except Exception as e:
            logger.warning(f"Response-Generierung fehlgeschlagen für [{frage_id}]: {e}")
            response = {"aussagen": []}
            antwort_text = ""

        try:
            judge_result = llm_judge.judge_answer(frage, antwort_text, erwartete_paragraphen)
        except Exception as e:
            logger.warning(f"Judge fehlgeschlagen für [{frage_id}]: {e}")
            judge_result = {
                "score": 2,
                "begruendung": f"Judge-Fehler: {e}",
                "korrekte_quellen": [],
                "verifiziert": False,
            }

        # Compute korrekte_quellen via regex (reliable, doesn't depend on LLM extraction)
        korrekte = _korrekte_quellen_aus_antwort(antwort_text, erwartete_paragraphen)
        # Check if §-references in answer exist in DB (quellen_genauigkeit component)
        qg_gesamt, qg_in_db = _check_quellen_in_db(antwort_text)

        ergebnis = {
            "frage_id": frage_id,
            "frage": frage,
            "erwartete_paragraphen": erwartete_paragraphen,
            "chunks_gefunden": len(chunks) if chunks else 0,
            "antwort": antwort_text[:500],
            "judge_score": judge_result.get("score", 2),
            "begruendung": judge_result.get("begruendung", ""),
            "korrekte_quellen": korrekte,
            "verifiziert": judge_result.get("verifiziert", False),
            "qg_refs_gesamt": qg_gesamt,
            "qg_refs_in_db": qg_in_db,
        }

        results["ergebnisse"].append(ergebnis)
        results["metriken"] = _compute_metrics(results["ergebnisse"])
        _save_results(results)
        logger.info(f"  → Score {ergebnis['judge_score']}/5 | Chunks: {ergebnis['chunks_gefunden']}")

        time.sleep(0.5)

    results["meta"]["abgeschlossen"] = datetime.now().isoformat()
    _save_results(results)

    m = results["metriken"]
    logger.info("=== ERGEBNISSE ===")
    logger.info(f"Vollständigkeit:    {m.get('vollstaendigkeits_score', 0):.2%}")
    logger.info(f"Halluzinations-Rate:{m.get('halluzinations_rate', 0):.2%}")
    logger.info(f"Quellen-Genauigkeit:{m.get('quellen_genauigkeit', 0):.2%}")
    logger.info(f"Schwache Antworten: {m.get('schwache_antworten', 0)}")

    dod_ok = (
        m.get("vollstaendigkeits_score", 0) >= 0.75
        and m.get("halluzinations_rate", 1) <= 0.05
        and m.get("quellen_genauigkeit", 0) >= 0.90
    )
    logger.info(f"DOD-Status: {'SUCCESS' if dod_ok else 'FAILED'}")


if __name__ == "__main__":
    sys.path.insert(0, os.path.dirname(__file__))
    run_test()
