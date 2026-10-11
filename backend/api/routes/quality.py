"""Quality Dashboard API — live Qualitätsmetriken."""
import json
import os
from pathlib import Path

from fastapi import APIRouter
from fastapi.responses import JSONResponse

router = APIRouter(prefix="/api/quality", tags=["quality"])

RESULTS_PATH = os.environ.get(
    "QUALITY_RESULTS_PATH",
    str(Path(__file__).parent.parent.parent.parent / "tests" / "quality" / "results.json"),
)


def _load_results() -> list:
    path = os.environ.get("QUALITY_RESULTS_PATH", RESULTS_PATH)
    try:
        with open(path) as f:
            return json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return []


def _compute_trend(results: list) -> str:
    if len(results) < 2:
        return "unbekannt"
    prev = results[-2]
    curr = results[-1]
    score_prev = (1 - prev.get("halluzinations_rate", 0)) + prev.get("quellen_genauigkeit", 0) + prev.get("vollstaendigkeits_score", 0)
    score_curr = (1 - curr.get("halluzinations_rate", 0)) + curr.get("quellen_genauigkeit", 0) + curr.get("vollstaendigkeits_score", 0)
    if score_curr > score_prev + 0.05:
        return "steigend"
    if score_curr < score_prev - 0.05:
        return "fallend"
    return "stabil"


@router.get("/dashboard")
def dashboard():
    """Gibt aktuelle Qualitätsmetriken und Trend zurück."""
    results = _load_results()
    if not results:
        return JSONResponse({
            "halluzinations_rate": None,
            "quellen_genauigkeit": None,
            "vollstaendigkeits_score": None,
            "trend": "keine Daten",
            "eintraege": 0,
        })
    latest = results[-1]
    return JSONResponse({
        "halluzinations_rate": latest.get("halluzinations_rate"),
        "quellen_genauigkeit": latest.get("quellen_genauigkeit"),
        "vollstaendigkeits_score": latest.get("vollstaendigkeits_score"),
        "trend": _compute_trend(results),
        "eintraege": len(results),
    })
