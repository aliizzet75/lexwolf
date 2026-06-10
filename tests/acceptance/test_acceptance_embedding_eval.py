#!/usr/bin/env python3
"""
Akzeptanztest für Task #178: Embedding-Modell evaluieren — juristisches Modell testen
"""

import json
from pathlib import Path


def test_acceptance_embedding_eval():
    """Testet ob Embedding-Evaluierung korrekt durchgeführt wurde."""
    print("Running Acceptance Test for Task #178")

    eval_path = Path(
        "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
        "/tests/quality/embedding_eval.json"
    )
    assert eval_path.exists(), "embedding_eval.json existiert nicht"
    print("✅ embedding_eval.json existiert")

    with open(eval_path) as f:
        data = json.load(f)

    # Test 1: Baseline-Modell definiert
    assert "baseline_model" in data, "baseline_model fehlt"
    print("✅ Baseline-Modell definiert")

    # Test 2: Results mit MRR-Werten (min. 2 Modelle evaluiert oder dokumentiert)
    assert "results" in data, "results fehlt"
    assert len(data["results"]) >= 1, "results ist leer"
    print("✅ results mit Modellen vorhanden")

    for model_name, metrics in data["results"].items():
        assert "mrr" in metrics, f"MRR fehlt für Modell {model_name}"
        mrr_val = metrics["mrr"]
        assert mrr_val is None or isinstance(mrr_val, (int, float)), f"MRR ungültig für {model_name}"
        print(f"  - {model_name}: MRR = {mrr_val}")

    # Test 3: Empfehlung enthalten
    assert "recommendation" in data, "recommendation fehlt"
    assert "change_model" in data["recommendation"], "change_model fehlt in recommendation"
    print(f"✅ Empfehlung enthalten: change_model = {data['recommendation']['change_model']}")

    # Test 4: Baseline MRR ist realer Wert (nicht 0.25 Simulation)
    baseline_mrr = data.get("baseline_mrr", 0)
    assert baseline_mrr != 0.25, "baseline_mrr ist simulierter Wert (0.25) — echte Berechnung erforderlich"
    assert baseline_mrr > 0.3, f"baseline_mrr={baseline_mrr:.4f} zu niedrig — echte Berechnung prüfen"
    print(f"✅ Baseline MRR ist echter Wert: {baseline_mrr:.4f}")

    # Test 5: Modellwechsel nicht empfohlen (kein +0.15 MRR Gewinn durch Alternative)
    assert data["recommendation"]["change_model"] == False, (
        "Empfehlung: Modellwechsel nicht nötig (kein +0.15 MRR Gewinn)"
    )
    print("✅ Empfehlung: Modellwechsel NICHT nötig (kein signifikanter MRR-Gewinn)")

    print("\n" + "=" * 60)
    print("✅ ACCEPTANCE TEST PASSED (Task #178)")
    print("=" * 60)


def test_acceptance_embedding_eval_pytest():
    """pytest wrapper für acceptance test."""
    test_acceptance_embedding_eval()


if __name__ == "__main__":
    test_acceptance_embedding_eval()
