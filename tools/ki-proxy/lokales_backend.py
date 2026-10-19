"""
Startet das LexWolf-Backend auf dem Entwicklungsrechner — mit den Daten des
Servers, aber ohne am Server etwas zu ändern.

- SSH-Tunnel zu PostgreSQL und Neo4j auf dem Server (lokal 15432 / 17687).
- Zugangsdaten kommen zur Laufzeit aus der Umgebung des Server-Dienstes
  (systemctl show) und werden nirgends gespeichert oder ausgegeben.
- LLM_PROVIDER=ki-proxy: jede KI-Anfrage läuft über die zentrale ask_llm()
  zum lokalen KI-Proxy (Claude Code) auf Port 8010.
- Backend auf http://localhost:8000; LexWolf mit
  LEXWOLF_BACKEND_URL=http://localhost:8000 starten (start-lokal.ps1 / F5).

Läuft im Vordergrund, Strg+C beendet Backend und Tunnel.
Voraussetzung: backend/venv (python -m venv backend/venv, pip install -r ...).
"""
import os
import socket
import subprocess
import sys
import time

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BACKEND_DIR = os.path.join(REPO_ROOT, "backend")
VENV_PYTHON = os.path.join(BACKEND_DIR, "venv", "Scripts", "python.exe")
SSH_TARGET = os.environ.get("LEXWOLF_SSH_TARGET", "root@212.227.180.66")
SERVER_SERVICE = "lexwolf-backend"
BACKEND_PORT = 8000
KI_PROXY_URL = "http://127.0.0.1:8010"
BOARD_URL = "http://212.227.180.66:8082"
# Server-Port -> lokaler Port des Tunnels
TUNNELS = {5432: 15432, 7687: 17687}

SSH_OPTS = ["-o", "BatchMode=yes", "-o", "ConnectTimeout=15"]


def log(msg: str) -> None:
    print(f"{time.strftime('%H:%M:%S')} {msg}", flush=True)


def port_open(port: int) -> bool:
    with socket.socket() as s:
        s.settimeout(1)
        return s.connect_ex(("127.0.0.1", port)) == 0


def server_env() -> dict:
    """Environment des Backend-Dienstes auf dem Server, mit auf die lokalen
    Tunnel-Ports umgeschriebenen Adressen."""
    out = subprocess.run(
        ["ssh", *SSH_OPTS, SSH_TARGET, f"systemctl show {SERVER_SERVICE} -p Environment --value"],
        capture_output=True, text=True, encoding="utf-8", timeout=30, check=True).stdout
    env = dict(item.split("=", 1) for item in out.split() if "=" in item)
    for key, value in env.items():
        for remote, local in TUNNELS.items():
            for host in ("localhost", "127.0.0.1"):
                value = value.replace(f"{host}:{remote}", f"127.0.0.1:{local}")
        env[key] = value
    return env


def main() -> int:
    if not os.path.exists(VENV_PYTHON):
        log(f"{VENV_PYTHON} fehlt — erst backend/venv anlegen (siehe Docstring).")
        return 1
    if port_open(BACKEND_PORT):
        log(f"Port {BACKEND_PORT} ist schon belegt — läuft das lokale Backend bereits?")
        return 1

    log(f"Lese Dienst-Umgebung von {SSH_TARGET} ...")
    env = dict(os.environ)
    env.update(server_env())
    env.update({
        "LLM_PROVIDER": "ki-proxy",
        "KI_PROXY_URL": KI_PROXY_URL,
        "LEXWOLF_BOARD_URL": BOARD_URL,
        "LEXWOLF_BACKEND_BASE_URL": f"http://localhost:{BACKEND_PORT}",
        "PYTHONIOENCODING": "utf-8",
    })

    forwards = [arg for remote, local in TUNNELS.items()
                for arg in ("-L", f"127.0.0.1:{local}:127.0.0.1:{remote}")]
    tunnel = subprocess.Popen(
        ["ssh", "-N", *SSH_OPTS, "-o", "ExitOnForwardFailure=yes",
         "-o", "ServerAliveInterval=30", *forwards, SSH_TARGET])
    try:
        for _ in range(30):
            if all(port_open(p) for p in TUNNELS.values()):
                break
            if tunnel.poll() is not None:
                log(f"SSH-Tunnel beendet (rc={tunnel.returncode})")
                return 1
            time.sleep(1)
        else:
            log("SSH-Tunnel nach 30 s nicht bereit")
            return 1
        log(f"Tunnel zu PostgreSQL/Neo4j steht, starte Backend auf http://localhost:{BACKEND_PORT}")
        return subprocess.call(
            [VENV_PYTHON, "-m", "uvicorn", "main:app", "--host", "127.0.0.1",
             "--port", str(BACKEND_PORT)],
            cwd=BACKEND_DIR, env=env)
    except KeyboardInterrupt:
        return 0
    finally:
        tunnel.kill()
        log("Tunnel geschlossen")


if __name__ == "__main__":
    sys.exit(main())
