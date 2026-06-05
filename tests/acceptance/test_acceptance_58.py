import os, sys, json, subprocess, pytest

SCRIPT = "/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/calibrate_threshold.py"
CONFIG = "/data/.openclaw/workspace-codex/projects/lexwolf/backend/threshold_config.json"
_missing = not os.path.exists(SCRIPT)
skip = pytest.mark.skipif(_missing, reason="calibrate_threshold.py nicht vorhanden")

@pytest.fixture(scope="module")
def run_result():
    return subprocess.run([sys.executable, SCRIPT], capture_output=True, text=True, timeout=60)

@skip
def test_script_laeuft_durch(run_result):
    assert run_result.returncode == 0, f"Script fehlgeschlagen:\n{run_result.stderr}"

@skip
def test_mindestens_10_thresholds(run_result):
    count = sum(1 for line in run_result.stdout.splitlines() if "0." in line)
    assert count >= 10, f"Weniger als 10 Schwellenwerte getestet: {count}"

@skip
def test_optimaler_threshold_ausgegeben(run_result):
    out = run_result.stdout.lower()
    assert "optimal" in out or "empfohlen" in out, "Kein optimaler Threshold im Output"

@skip
def test_config_datei_gespeichert(run_result):
    assert os.path.exists(CONFIG), f"Config-Datei fehlt: {CONFIG}"
    data = json.load(open(CONFIG))
    assert "confidence_threshold" in data and 0.0 < data["confidence_threshold"] <= 1.0
