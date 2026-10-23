#!/usr/bin/env python3
"""CI/CD Quality Gate für den Gutachten-Modus (T#278).

Analog zu tests/quality/ci_quality_gate.py (Entry 48/M3), aber mit
strengeren Schwellenwerten für die Einwände-Vollständigkeit UND einer
zusätzlichen Sperre: ohne schriftliche Freigabe des Anwalts
(docs/gutachten_freigabe_status.json) ist das Gate NIE grün, selbst wenn
alle Metriken bestehen — automatisierte Tests ersetzen nicht die fachliche
Verantwortung des Anwalts nach § 43 BRAO (siehe docs/freigabe_gutachten.md)."""
import json
import os
import sys
from pathlib import Path

BACKEND = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "backend"))
sys.path.insert(0, BACKEND)

EINWAENDE_VOLLSTAENDIGKEIT_THRESHOLD = 0.95  # strenger als allgemeine 0.80 (quality_protocol.md)
NORMEN_FEHLEN_MAX = 0                         # jede nicht in der DB gefundene Norm blockiert
BESTEHENSRATE_THRESHOLD = 1.0                 # jeder der 15-20 Testfälle muss bestehen


def load_results(results_file: str) -> dict:
    path = Path(results_file)
    if not path.exists():
        raise FileNotFoundError(f"Results-Datei nicht gefunden: {results_file}")
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def check_thresholds(results: dict) -> list:
    failures = []
    bestehensrate = results.get("bestehensrate", 0.0)
    if bestehensrate < BESTEHENSRATE_THRESHOLD:
        failures.append(f"bestehensrate={bestehensrate:.2%} < {BESTEHENSRATE_THRESHOLD:.0%}")

    avg_einwaende = results.get("avg_einwaende_vollstaendigkeit", 0.0)
    if avg_einwaende < EINWAENDE_VOLLSTAENDIGKEIT_THRESHOLD:
        failures.append(
            f"avg_einwaende_vollstaendigkeit={avg_einwaende:.2%} < {EINWAENDE_VOLLSTAENDIGKEIT_THRESHOLD:.0%}"
        )

    normen_fehlen = results.get("normen_fehlen_gesamt", 1)
    if normen_fehlen > NORMEN_FEHLEN_MAX:
        failures.append(f"normen_fehlen_gesamt={normen_fehlen} > {NORMEN_FEHLEN_MAX}")

    neg_korrekt = results.get("negativtests_korrekt_erkannt", 0)
    neg_gesamt = results.get("negativtests_gesamt", 1)
    if neg_korrekt < neg_gesamt:
        failures.append(f"negativtests_korrekt_erkannt={neg_korrekt}/{neg_gesamt} (Qualitätstor erkennt nicht alle Mängel-Fixtures)")

    return failures


def check_freigabe() -> list:
    from services.gutachten_quality import ist_produktionsfreigabe_erteilt

    if not ist_produktionsfreigabe_erteilt():
        return [
            "Keine schriftliche Freigabe durch den Anwalt in "
            "docs/gutachten_freigabe_status.json (freigegeben=true und "
            "freigegeben_durch gesetzt erforderlich, analog M3/Entry 48)."
        ]
    return []


def run_gate(results_file: str = "gutachten_results.json") -> int:
    print("=== LexWolf Gutachten-Qualitätstor (T#278) ===")
    print(f"Thresholds: bestehensrate>={BESTEHENSRATE_THRESHOLD:.0%}, "
          f"einwaende_vollstaendigkeit>={EINWAENDE_VOLLSTAENDIGKEIT_THRESHOLD:.0%}, "
          f"normen_fehlen<={NORMEN_FEHLEN_MAX}")
    try:
        results = load_results(results_file)
    except FileNotFoundError as e:
        print(f"ERROR: {e}")
        return 1

    failures = check_thresholds(results) + check_freigabe()
    if failures:
        print("GATE FAILED:")
        for f in failures:
            print(f"  - {f}")
        return 1
    print("GATE PASSED: Alle Metriken bestanden UND schriftliche Freigabe des Anwalts liegt vor.")
    return 0


if __name__ == "__main__":
    results_file = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        os.path.dirname(__file__), "gutachten_results.json"
    )
    sys.exit(run_gate(results_file))
