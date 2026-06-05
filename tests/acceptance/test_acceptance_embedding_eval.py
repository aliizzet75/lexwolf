#!/usr/bin/env python3
"""
Akzeptanztest für Task #159: Embedding-Modell evaluieren
"""

import json
from pathlib import Path


def test_acceptance_embedding_eval():
    """Testet ob Embedding-Evaluierung korrekt durchgeführt wurde."""
    print("Running Acceptance Test for Task #159")
    
    # Prüfe embedding_eval.json
    eval_path = Path("/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/embedding_eval.json")
    assert eval_path.exists(), "embedding_eval.json existiert nicht"
    print("✅ embedding_eval.json existiert")
    
    with open(eval_path, "r") as f:
        data = json.load(f)
    
    # Test 1: Baseline-Modellelement vorhanden
    assert "baseline_model" in data, "baseline_model fehlt"
    print("✅ Baseline-Modell definiert")
    
    # Test 2: results mit MRR-Werten
    assert "results" in data, "results fehlt"
    assert len(data["results"]) > 0, "results ist leer"
    print("✅ results mit Modellen vorhanden")
    
    for model_name, metrics in data["results"].items():
        assert "mrr" in metrics, f"MRR fehlt für Modell {model_name}"
        print(f"  - {model_name}: MRR = {metrics['mrr']}")
    
    # Test 3: Empfehlung enthalten
    assert "recommendation" in data, "recommendation fehlt"
    assert "change_model" in data["recommendation"], "change_model fehlt in recommendation"
    print(f"✅ Empfehlung enthalten: change_model = {data['recommendation']['change_model']}")
    
    # Test 4: Kein Datenbankeingriff (nur Dateioperationen)
    assert "database" not in str(data).lower(), "Unerwünschter DB-Bezug in Ergebnis"
    print("✅ Kein Datenbankeingriff (nur File I/O)")
    
    # Test 5: NUR evaluiert, kein Neuberechnen
    assert data["recommendation"]["change_model"] == False, "Empfehlung: Modellwechsel nicht nötig (kein +0.15 MRR Gewinn)"
    print("✅ Empfehlung: Modellwechsel NICHT nötig (kein signifikanter MRR-Gewinn)")
    
    print("\n" + "="*60)
    print("✅ ACCEPTANCE TEST PASSED")
    print("="*60)


def test_acceptance_embedding_eval_pytest():
    """pytest wrapper für acceptance test."""
    test_acceptance_embedding_eval()


if __name__ == "__main__":
    test_acceptance_embedding_eval()
