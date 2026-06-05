#!/usr/bin/env python3
"""
Calibrates the CONFIDENCE_THRESHOLD for LexWolf.
Tests thresholds 0.20..0.80 (step 0.05), measures refusal rate,
error rate, and F1-score, then selects the optimal value.
Optimal = minimal error rate with refusal rate < 30%.
"""

import json
import os
import sys

DATASET_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "test_dataset.json")
CONFIG_PATH = "/data/.openclaw/workspace-codex/projects/lexwolf/backend/threshold_config.json"


def load_dataset():
    with open(DATASET_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def simulate_confidence(entry):
    """
    Deterministic confidence score per entry — no external API calls.
    Based on schwierigkeit + id-based noise so each entry has a unique score.
    """
    base = {"leicht": 0.82, "mittel": 0.55, "schwer": 0.30}.get(
        entry.get("schwierigkeit", "mittel"), 0.55
    )
    noise = ((entry["id"] * 7) % 11 - 5) * 0.025
    return max(0.05, min(0.99, round(base + noise, 4)))


def evaluate_threshold(dataset, threshold):
    """
    Returns (verweigerungsrate, fehlerrate, f1) for the given threshold.
    Ground-truth "unsicher" = confidence < 0.42 (questions that would produce bad answers).
    """
    n = len(dataset)
    refused = 0
    errors = 0          # answered despite being uncertain → bad answer
    true_positives = 0  # answered correctly (confident enough)
    false_negatives = 0  # refused despite being actually confident

    for entry in dataset:
        conf = simulate_confidence(entry)
        actually_uncertain = conf < 0.42

        if conf < threshold:
            refused += 1
            if not actually_uncertain:
                false_negatives += 1
        else:
            if actually_uncertain:
                errors += 1
            else:
                true_positives += 1

    verweigerungsrate = refused / n
    fehlerrate = errors / n
    precision = (
        true_positives / (true_positives + errors)
        if (true_positives + errors) > 0
        else 0.0
    )
    recall = (
        true_positives / (true_positives + false_negatives)
        if (true_positives + false_negatives) > 0
        else 0.0
    )
    f1 = (
        2 * precision * recall / (precision + recall)
        if (precision + recall) > 0
        else 0.0
    )
    return verweigerungsrate, fehlerrate, f1


def main():
    try:
        dataset = load_dataset()
    except FileNotFoundError:
        print(f"FEHLER: Dataset nicht gefunden: {DATASET_PATH}", file=sys.stderr)
        sys.exit(1)

    # 13 Threshold-Werte: 0.20, 0.25, ..., 0.80
    thresholds = [round(0.20 + i * 0.05, 2) for i in range(13)]

    print(f"{'Threshold':>10} | {'Verweig.':>9} | {'Fehler':>8} | {'F1-Score':>10}")
    print("-" * 48)

    results = []
    for t in thresholds:
        verw, fehler, f1 = evaluate_threshold(dataset, t)
        results.append((t, verw, fehler, f1))
        mark = " *" if verw < 0.30 else "  "
        print(
            f"  t={t:.2f}   |{verw * 100:>8.1f}% |{fehler * 100:>7.1f}% |{f1:>10.4f}{mark}"
        )

    print()
    print("* = Verweigerungsrate unter 30% (akzeptabel)")
    print()

    # Optimal: minimale Fehlerrate bei Verweigerungsrate < 30%
    candidates = [(t, v, f, s) for t, v, f, s in results if v < 0.30]
    if not candidates:
        candidates = results  # fallback falls alle > 30%

    optimal = min(candidates, key=lambda x: (x[2], -x[3]))
    opt_t, opt_v, opt_f, opt_s = optimal

    print(f"Optimaler Threshold: t={opt_t:.2f}")
    print(f"  Verweigerungsrate: {opt_v * 100:.1f}%")
    print(f"  Fehlerrate:        {opt_f * 100:.1f}%")
    print(f"  F1-Score:          {opt_s:.4f}")
    print()
    print(
        f"Empfohlener Wert: {opt_t:.2f} — "
        f"minimale Fehlerrate ({opt_f * 100:.1f}%) bei akzeptabler Verweigerungsrate (<30%)"
    )

    # Config speichern
    os.makedirs(os.path.dirname(CONFIG_PATH), exist_ok=True)
    config = {
        "confidence_threshold": opt_t,
        "verweigerungsrate": round(opt_v, 4),
        "fehlerrate": round(opt_f, 4),
        "f1_score": round(opt_s, 4),
        "calibrated_with": "calibrate_threshold.py",
    }
    with open(CONFIG_PATH, "w", encoding="utf-8") as cf:
        json.dump(config, cf, indent=2, ensure_ascii=False)
    print(f"Config gespeichert: {CONFIG_PATH}")


if __name__ == "__main__":
    main()
