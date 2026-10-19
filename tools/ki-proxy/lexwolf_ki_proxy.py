"""
Lokaler KI-Endpunkt für LexWolf auf dem Entwicklungsrechner.

Das Backend ruft für jede KI-Anfrage die zentrale Funktion ask_llm()
(backend/services/llm_client.py) auf. Mit LLM_PROVIDER=ki-proxy schickt
ask_llm die Nachrichten an diesen Proxy, der sie über die lokal angemeldete
Claude-Code-CLI (`claude -p`) beantwortet — ohne API-Key. Der Proxy macht
nur diesen einen Schritt, die Logik (Prompts, Suche, Feature-Feedback ...)
bleibt komplett im Backend. Genutzt vom lokalen Backend (lokales_backend.py).

Nur Python-Standardbibliothek — kein pip install nötig.
Start: siehe ensure-proxy.ps1 / start-lokal.ps1 im selben Ordner.
"""
import glob
import json
import os
import shutil
import subprocess
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(os.environ.get("LEXWOLF_PROXY_PORT", "8010"))
CLAUDE_MODEL = os.environ.get("LEXWOLF_CLAUDE_MODEL")  # leer = Standardmodell der CLI
CLAUDE_TIMEOUT_S = 280  # knapp unter dem 300-s-Timeout von ask_llm
MAX_PARALLEL_CLAUDE = 3


LOG_FILE = os.path.join(tempfile.gettempdir(), "lexwolf-ki-proxy.log")


def log(msg: str) -> None:
    line = f"{time.strftime('%Y-%m-%d %H:%M:%S')} {msg}"
    print(line, flush=True)
    with open(LOG_FILE, "a", encoding="utf-8") as f:
        f.write(line + "\n")


# ---------------------------------------------------------------- Claude-CLI

def _find_claude_exe() -> str:
    explicit = os.environ.get("LEXWOLF_CLAUDE_EXE")
    if explicit:
        return explicit
    on_path = shutil.which("claude")
    if on_path:
        return on_path
    pattern = os.path.expanduser(
        "~/.vscode/extensions/anthropic.claude-code-*/resources/native-binary/claude.exe")
    candidates = sorted(glob.glob(pattern), key=os.path.getmtime)
    if candidates:
        return candidates[-1]
    raise RuntimeError("Claude-Code-CLI nicht gefunden — LEXWOLF_CLAUDE_EXE setzen.")


CLAUDE_EXE = _find_claude_exe()
# Leeres Arbeitsverzeichnis, damit die CLI keine Projekt-CLAUDE.md o.ä. als
# Kontext einliest — die Antwort soll nur auf System-Prompt + Anfrage beruhen.
_CLAUDE_CWD = tempfile.mkdtemp(prefix="lexwolf-ki-")
_claude_slots = threading.Semaphore(MAX_PARALLEL_CLAUDE)


def _claude_env() -> dict:
    env = dict(os.environ)
    # Falls der Proxy aus einer Claude-Code-Sitzung heraus gestartet wurde:
    # sonst lehnt die CLI den verschachtelten Start ab.
    for key in ("CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SSE_PORT"):
        env.pop(key, None)
    return env


def ask_claude(system_prompt: str, user_text: str, model: str | None) -> str:
    # System-Prompt über eine Datei statt als Argument: mit Mandanten-Dokumenten
    # wird er schnell >100.000 Zeichen, Windows-Kommandozeilen sind aber auf
    # 32.767 Zeichen begrenzt (WinError 206). Die Anfrage selbst geht über stdin.
    fd, prompt_file = tempfile.mkstemp(suffix=".txt", dir=_CLAUDE_CWD)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.write(system_prompt)
        args = [CLAUDE_EXE, "-p", "--tools", "", "--system-prompt-file", prompt_file,
                "--no-session-persistence", "--strict-mcp-config", "--output-format", "json"]
        if model or CLAUDE_MODEL:
            args += ["--model", model or CLAUDE_MODEL]
        with _claude_slots:
            proc = subprocess.run(args, input=user_text, capture_output=True, text=True,
                                  encoding="utf-8", errors="replace", env=_claude_env(),
                                  cwd=_CLAUDE_CWD, timeout=CLAUDE_TIMEOUT_S)
    finally:
        os.remove(prompt_file)
    try:
        data = json.loads(proc.stdout)
    except json.JSONDecodeError:
        raise RuntimeError(f"Claude-CLI rc={proc.returncode}: {(proc.stderr or proc.stdout)[:300]}")
    if data.get("is_error"):
        raise RuntimeError(f"Claude-CLI meldet Fehler: {str(data.get('result'))[:300]}")
    return data.get("result") or ""


def split_messages(messages: list) -> tuple[str, str]:
    """ask_llm-Nachrichten -> (System-Prompt, Anfrage). Eine führende
    "system"-Nachricht ist der System-Prompt (wie in llm_client._ask_claude).
    Die CLI nimmt im Print-Modus nur eine Nachricht — bei mehreren Turns wird
    der Verlauf als Transkript vorangestellt."""
    system = ""
    if messages and messages[0]["role"] == "system":
        system, messages = messages[0]["content"], messages[1:]
    if len(messages) == 1 and messages[0]["role"] == "user":
        return system, messages[0]["content"]
    *history, last = messages
    names = {"user": "Anwalt", "assistant": "Assistent", "system": "System-Hinweis"}
    lines = ["Bisheriger Gesprächsverlauf:"]
    for m in history:
        lines.append(f"\n[{names.get(m['role'], m['role'])}]:\n{m['content']}")
    lines.append(f"\n\nAktuelle Nachricht (darauf antworten):\n{last['content']}")
    return system, "\n".join(lines)


# ------------------------------------------------------------------ Endpunkte

class ProxyHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):  # Standard-Log aus; eigenes Log ohne Inhalte
        pass

    def _send_json(self, status: int, data: dict) -> None:
        body = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:
        if self.path.split("?", 1)[0] == "/health":
            self._send_json(200, {"status": "ok"})
        else:
            self._send_json(404, {"detail": "Nur POST /llm"})

    def do_POST(self) -> None:
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length) if length else b""
        if self.path.split("?", 1)[0] != "/llm":
            self._send_json(404, {"detail": "Nur POST /llm"})
            return
        started = time.time()
        try:
            req = json.loads(body or b"{}")
            system, user_text = split_messages(req["messages"])
            content = ask_claude(system, user_text, req.get("model"))
            log(f"/llm: {len(req['messages'])} Nachrichten, System-Prompt {len(system)} Zeichen, "
                f"Antwort {len(content)} Zeichen ({time.time() - started:.1f}s)")
            self._send_json(200, {"content": content})
        except Exception as e:
            log(f"/llm FEHLER ({time.time() - started:.1f}s): {e}")
            self._send_json(500, {"detail": str(e)})


class ProxyServer(ThreadingHTTPServer):
    # HTTPServer setzt sonst SO_REUSEADDR — unter Windows dürfen damit mehrere
    # Prozesse denselben Port binden, ein zweiter Proxy-Start fiele nicht auf.
    allow_reuse_address = False


def main() -> None:
    try:
        server = ProxyServer(("127.0.0.1", PORT), ProxyHandler)
    except OSError:
        # Port belegt = Proxy läuft schon (z.B. aus start-lokal.ps1).
        log(f"LexWolf-KI-Proxy läuft bereits auf http://localhost:{PORT}")
        return
    log(f"LexWolf-KI-Proxy auf http://localhost:{PORT} (POST /llm) über {CLAUDE_EXE}")
    server.serve_forever()


if __name__ == "__main__":
    main()
