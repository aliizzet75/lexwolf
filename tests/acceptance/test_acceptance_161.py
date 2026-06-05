import json
import os
import socket
import pytest

_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
RESULTS_PATH = f"{_ROOT}/tests/quality/results.json"
DATASET_PATH = f"{_ROOT}/tests/quality/test_dataset.json"


def _pg_ok():
    s = socket.socket(); s.settimeout(2); r = s.connect_ex(("localhost", 5432)); s.close(); return r == 0


def test_dataset_100_fragen():
    assert os.path.exists(DATASET_PATH)
    with open(DATASET_PATH, encoding="utf-8") as f:
        data = json.load(f)
    assert len(data) >= 100, f"Nur {len(data)} Fragen, 100 erwartet"


def test_alle_100_fragen_gelaufen():
    if not os.path.exists(RESULTS_PATH):
        pytest.skip("results.json fehlt — Test-Run noch nicht ausgeführt")
    with open(RESULTS_PATH, encoding="utf-8") as f:
        r = json.load(f)
    n = len(r.get("ergebnisse", []))
    assert n >= 100, f"Nur {n}/100 Fragen gelaufen (Absturz?)"


def test_dod_metriken():
    if not os.path.exists(RESULTS_PATH):
        pytest.skip("results.json fehlt — Test-Run noch nicht ausgeführt")
    with open(RESULTS_PATH, encoding="utf-8") as f:
        r = json.load(f)
    if len(r.get("ergebnisse", [])) < 100:
        pytest.skip(f"Test-Run unvollständig: {len(r.get('ergebnisse',[]))}/100")
    m = r["metriken"]
    assert m.get("vollstaendigkeits_score", 0) >= 0.75, f"Vollständigkeit {m.get('vollstaendigkeits_score')} < 0.75"
    assert m.get("halluzinations_rate", 1) <= 0.05, f"Halluzination {m.get('halluzinations_rate')} > 0.05"
    assert m.get("quellen_genauigkeit", 0) >= 0.90, f"Quellen-Genauigkeit {m.get('quellen_genauigkeit')} < 0.90"


def test_kein_thema_unter_060():
    if not os.path.exists(RESULTS_PATH):
        pytest.skip("results.json fehlt — Test-Run noch nicht ausgeführt")
    with open(RESULTS_PATH, encoding="utf-8") as f:
        r = json.load(f)
    if len(r.get("ergebnisse", [])) < 100:
        pytest.skip("Test-Run unvollständig")
    with open(DATASET_PATH, encoding="utf-8") as f:
        dataset = {d["id"]: d for d in json.load(f)}
    thema_scores: dict = {}
    for e in r["ergebnisse"]:
        themen = dataset.get(e["frage_id"], {}).get("erwartete_themen", ["Unbekannt"])
        hauptthema = themen[-1] if themen else "Unbekannt"
        thema_scores.setdefault(hauptthema, []).append(
            len(e.get("korrekte_quellen", [])) / max(len(dataset.get(e["frage_id"], {}).get("erwartete_paragraphen", [1])), 1)
        )
    schwach = {t: sum(s)/len(s) for t, s in thema_scores.items() if sum(s)/len(s) < 0.60}
    assert not schwach, f"Themen mit Score < 0.60: {schwach}"


@pytest.mark.skipif(not _pg_ok(), reason="PostgreSQL nicht erreichbar auf port 5432")
def test_pg_verbindung():
    import psycopg2
    conn = psycopg2.connect("postgresql://postgres:postgres@localhost:5432/lexwolf")
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM legal_chunks")
    count = cur.fetchone()[0]
    conn.close()
    assert count > 0, "legal_chunks Tabelle ist leer"
