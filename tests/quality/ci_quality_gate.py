#!/usr/bin/env python3
"""CI/CD Quality Gate for LexWolf Production Releases."""
import sys
import json
from pathlib import Path

HALLUZINATION_THRESHOLD = 0.05   # < 5%
QUELLEN_THRESHOLD = 0.95          # > 95%
VOLLSTAENDIGKEIT_THRESHOLD = 0.80 # > 80%

def load_metrics(metrics_file: str) -> dict:
    path = Path(metrics_file)
    if not path.exists():
        raise FileNotFoundError(f"Metrics file not found: {metrics_file}")
    with open(path) as f:
        return json.load(f)

def check_thresholds(metrics: dict) -> list[str]:
    failures = []
    halluzination = metrics.get("halluzinations_rate", 1.0)
    if halluzination >= HALLUZINATION_THRESHOLD:
        failures.append(
            f"halluzinations_rate={halluzination:.3f} >= {HALLUZINATION_THRESHOLD} (max 5%)"
        )
    quellen = metrics.get("quellen_genauigkeit", 0.0)
    if quellen <= QUELLEN_THRESHOLD:
        failures.append(
            f"quellen_genauigkeit={quellen:.3f} <= {QUELLEN_THRESHOLD} (min 95%)"
        )
    vollstaendigkeit = metrics.get("vollstaendigkeits_score", 0.0)
    if vollstaendigkeit <= VOLLSTAENDIGKEIT_THRESHOLD:
        failures.append(
            f"vollstaendigkeits_score={vollstaendigkeit:.3f} <= {VOLLSTAENDIGKEIT_THRESHOLD} (min 80%)"
        )
    return failures

def run_gate(metrics_file: str = "quality_metrics.json") -> int:
    print("=== LexWolf Quality Gate ===")
    print(f"Thresholds: halluzinations_rate<{HALLUZINATION_THRESHOLD}, ")
    print(f"           quellen_genauigkeit>{QUELLEN_THRESHOLD}, ")
    print(f"           vollstaendigkeits_score>{VOLLSTAENDIGKEIT_THRESHOLD}")
    try:
        metrics = load_metrics(metrics_file)
    except FileNotFoundError as e:
        print(f"ERROR: {e}")
        return 1
    failures = check_thresholds(metrics)
    if failures:
        print("GATE FAILED:")
        for f in failures:
            print(f"  - {f}")
        return 1
    print("GATE PASSED: All quality thresholds met.")
    return 0

if __name__ == "__main__":
    metrics_file = sys.argv[1] if len(sys.argv) > 1 else "quality_metrics.json"
    sys.exit(run_gate(metrics_file))
