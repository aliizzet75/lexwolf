import json
import os
import socket
import subprocess
import sys

# Add lexwolf project root to sys.path so 'backend' is importable as a package
_project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
if _project_root not in sys.path:
    sys.path.insert(0, _project_root)

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
_RESULTS = os.path.join(_ROOT, "tests/quality/results.json")
_SCRIPT = os.path.join(_ROOT, "tests/quality/run_live_quality_test.py")


def _pg_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 5432))
    s.close()
    return r == 0


def _quality_test_complete() -> bool:
    """Return True if results.json exists and has all 100 questions."""
    if not os.path.exists(_RESULTS):
        return False
    try:
        with open(_RESULTS, encoding="utf-8") as f:
            data = json.load(f)
        return len(data.get("ergebnisse", [])) >= 100
    except Exception:
        return False


def pytest_sessionstart(session):
    """Run quality test before acceptance tests if results are incomplete."""
    if _quality_test_complete():
        return
    if not os.path.exists(_SCRIPT):
        print(f"[conftest] Quality-Test-Script nicht gefunden: {_SCRIPT}", file=sys.stderr)
        return
    if not _pg_ok():
        print("[conftest] PostgreSQL nicht erreichbar – Quality-Test übersprungen", file=sys.stderr)
        return

    env = os.environ.copy()
    env.setdefault("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")
    env.setdefault("NEO4J_URI", "bolt://localhost:7687")
    env.setdefault("NEO4J_USER", "neo4j")
    env.setdefault("NEO4J_PASSWORD", "lexwolf123")
    env.setdefault("OLLAMA_URL", "http://localhost:11434")

    already = 0
    if os.path.exists(_RESULTS):
        try:
            with open(_RESULTS, encoding="utf-8") as f:
                already = len(json.load(f).get("ergebnisse", []))
        except Exception:
            pass

    print(f"[conftest] Starte Quality-Test ({already}/100 bereits getestet)…", file=sys.stderr)
    try:
        subprocess.run(
            [sys.executable, _SCRIPT],
            env=env,
            timeout=600,  # max 10 Minuten
        )
        print("[conftest] Quality-Test abgeschlossen.", file=sys.stderr)
    except subprocess.TimeoutExpired:
        print("[conftest] Quality-Test Timeout – partieller Fortschritt gespeichert.", file=sys.stderr)
    except Exception as e:
        print(f"[conftest] Quality-Test Fehler: {e}", file=sys.stderr)

    # Post result summary to Board API
    try:
        import http.client
        done = 0
        metriken = {}
        if os.path.exists(_RESULTS):
            with open(_RESULTS, encoding="utf-8") as f:
                data = json.load(f)
            done = len(data.get("ergebnisse", []))
            metriken = data.get("metriken", {})
        msg = (
            f"Quality-Test via conftest.py: {done}/100 Fragen getestet. "
            f"Vollständigkeit={metriken.get('vollstaendigkeits_score', 'n/a')}, "
            f"Halluzination={metriken.get('halluzinations_rate', 'n/a')}, "
            f"Quellen={metriken.get('quellen_genauigkeit', 'n/a')}"
        )
        payload = json.dumps({"inhalt": msg, "typ": "notiz", "autor": "aligator"}).encode()
        conn = http.client.HTTPConnection("localhost", 8082, timeout=5)
        conn.request("POST", "/api/tasks/154/history", payload,
                     {"Content-Type": "application/json"})
        conn.getresponse()
        conn.close()
    except Exception:
        pass  # Board post optional


def pytest_configure(config):
    """Create the users table (and enum type) before any tests run."""
    if not _pg_ok():
        return
    try:
        import psycopg2
        conn = psycopg2.connect(
            "postgresql://postgres:postgres@localhost:5432/lexwolf"
        )
        cur = conn.cursor()
        cur.execute(
            """
            DO $$ BEGIN
                CREATE TYPE subscription_status_enum AS ENUM ('active', 'inactive', 'trial');
            EXCEPTION WHEN duplicate_object THEN NULL;
            END $$;
            """
        )
        cur.execute(
            """
            CREATE TABLE IF NOT EXISTS users (
                id          SERIAL PRIMARY KEY,
                email       VARCHAR UNIQUE NOT NULL,
                subscription_status subscription_status_enum NOT NULL DEFAULT 'inactive',
                paid_until  DATE,
                created_at  TIMESTAMP DEFAULT NOW()
            )
            """
        )
        conn.commit()
        cur.close()
        conn.close()
    except Exception as e:
        print(f"[conftest] Warning: could not ensure users table: {e}")
