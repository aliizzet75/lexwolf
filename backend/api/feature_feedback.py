"""
/feature-feedback — Anwalts-Feedback zu neuen Client-Features oder Bugs.

Nimmt einen freitextlichen Feature-Wunsch oder eine Bug-Meldung entgegen,
klärt sie im Dialog mit dem Anwalt (Rückfragen bei Unklarheit), bestätigt sie
per Zusammenfassung und legt sie nach Bestätigung als Milestone+Task im
VentureOS-Board an — die Aligator/Codex/Claude-Pipeline übernimmt danach
automatisch Umsetzung, Build und Deployment (siehe deploy_lexwolf-Hook in
aligator.py).

Der Kontext darüber, was LexWolf bereits kann bzw. gerade umsetzt, wird bei
jeder Anfrage live vom Board geholt (keine separate, potenziell veraltete
Dokumentationsquelle).
"""
import asyncio
import json
import logging
import os
import re
import subprocess
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import List, Optional

import requests
from fastapi import APIRouter, File, Form, HTTPException, UploadFile
from pydantic import BaseModel, field_validator

from api.chat import _call_ollama

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/feature-feedback", tags=["feature-feedback"])

BOARD_URL = "http://localhost:8082"
QUEUE_FILE = Path("/docker/openclaw-oo5q/data/lexwolf-wiki/coding_queue.jsonl")
PROJEKT_ID = 1  # LexWolf
AUTODEPLOY_MARKER = "[AUTODEPLOY:ANWALT-FEEDBACK]"
REPO_ROOT = Path(__file__).resolve().parent.parent.parent
README_PATH = REPO_ROOT / "README.md"
SOURCE_SEARCH_DIRS = ["backend", "desktop"]
ATTACHMENT_DIR = Path(os.environ.get("LEXWOLF_ATTACHMENTS_DIR", str(REPO_ROOT / "attachments")))
MAX_ATTACHMENT_SIZE = int(os.environ.get("LEXWOLF_MAX_ATTACHMENT_SIZE", "10485760"))  # 10 MB
# Basis-URL unter der das Backend seine /attachments/<id>-Dateien ausliefert.
# Wird in Board-Beschreibung/Task-Daten als vollqualifizierter Link eingebettet.
BACKEND_BASE_URL = os.environ.get("LEXWOLF_BACKEND_BASE_URL", "http://localhost:8000").rstrip("/")

def _full_attachment_url(relative_url: str) -> str:
    """Verknüpft relative Screenshot-URL mit BACKEND_BASE_URL ohne doppelten Slash."""
    base = BACKEND_BASE_URL.rstrip("/")
    rel = relative_url.lstrip("/")
    return f"{base}/{rel}"
ALLOWED_IMAGE_TYPES = {"image/png", "image/jpeg", "image/gif", "image/webp"}
_STOPWORDS = {
    "und", "oder", "der", "die", "das", "den", "dem", "des", "ein", "eine",
    "einen", "einem", "einer", "ich", "haette", "hätte", "gerne", "gern",
    "mit", "fuer", "für", "von", "auf", "im", "in", "zu", "dass", "wenn",
    "soll", "sollte", "kann", "koennte", "könnte", "bitte", "mehr", "noch",
    "nicht", "auch", "nur", "wie", "was", "wer", "wo", "warum", "dann",
    "also", "aber", "dieser", "diese", "dieses", "einfach", "immer", "schon",
}

# Serverseitiger Speicher für Feature-Wünsche pro Session/Anwalt.
# Der Key ist eine anonyme Session-ID (keine personenbezogenen Daten).
_feature_wunsch_storage: dict[str, list[dict]] = {}


def _extract_keywords(text: str, max_keywords: int = 6) -> list:
    """Simple Heuristik statt LLM-Aufruf: nimmt längere, nicht-triviale Wörter
    aus der Anwalts-Nachricht als Grep-Suchbegriffe für den Quellcode."""
    words = re.findall(r"[A-Za-zÄÖÜäöüß]{4,}", text)
    keywords = []
    for w in words:
        lw = w.lower()
        if lw in _STOPWORDS or lw in keywords:
            continue
        keywords.append(lw)
        if len(keywords) >= max_keywords:
            break
    return keywords


def _search_source_code(user_message: str) -> str:
    """Schnelle Stichwortsuche über den Quellcode als Zusatzsignal zum Board —
    für den Fall, dass das Board etwas nicht (mehr) korrekt widerspiegelt.
    Bewusst ungenau/heuristisch: Anwaltssprache trifft selten exakte
    Code-Bezeichner, das ist nur ein Zusatzhinweis, kein verlässlicher Beweis."""
    keywords = _extract_keywords(user_message)
    if not keywords:
        return "(keine eindeutigen Suchbegriffe aus der Nachricht extrahiert)"

    search_paths = [str(REPO_ROOT / d) for d in SOURCE_SEARCH_DIRS if (REPO_ROOT / d).is_dir()]
    if not search_paths:
        return "(Quellcode-Verzeichnis nicht erreichbar)"

    exclude_dirs = ["--exclude-dir=bin", "--exclude-dir=obj", "--exclude-dir=__pycache__",
                    "--exclude-dir=node_modules", "--exclude-dir=venv", "--exclude-dir=venv_test",
                    "--exclude-dir=.venv", "--exclude-dir=.git"]

    # Pro Keyword einzeln suchen und Treffer zählen statt einer naiven OR-Suche:
    # eine Datei, die MEHRERE Suchbegriffe enthält, ist relevanter als eine, die
    # nur zufällig ein einzelnes generisches Wort (z.B. "mandanten") trifft —
    # sonst gewinnt oft nur die alphabetisch erste Zufallsdatei.
    hit_counts: dict = {}
    try:
        for kw in keywords:
            result = subprocess.run(
                ["grep", "-rIli", *exclude_dirs, "-e", kw] + search_paths,
                capture_output=True, text=True, timeout=5,
            )
            for f in result.stdout.splitlines():
                if f.strip():
                    hit_counts[f] = hit_counts.get(f, 0) + 1
    except Exception as e:
        logger.warning(f"Quellcode-Suche fehlgeschlagen: {e}")
        return "(Quellcode-Suche aktuell nicht verfügbar)"

    if not hit_counts:
        return f"(keine Treffer im Quellcode für: {', '.join(keywords)})"

    ranked = sorted(hit_counts, key=lambda f: hit_counts[f], reverse=True)[:5]

    # Schnipsel: die Zeile mit den MEISTEN Keyword-Treffern im Dokument, nicht
    # einfach die erste Trefferzeile — sonst zeigt eine mehrfach relevante Datei
    # (z.B. MainWindow.xaml mit vielen Buttons) zufällig eine irrelevante Zeile
    # und führt die KI in die Irre statt ihr zu helfen.
    lines = []
    for f in ranked:
        snippet = ""
        try:
            with open(f, "r", encoding="utf-8", errors="ignore") as fh:
                best_line, best_score = "", 0
                for file_line in fh:
                    lower = file_line.lower()
                    score = sum(1 for kw in keywords if kw in lower)
                    if score > best_score:
                        best_score, best_line = score, file_line.strip()
                snippet = best_line
        except Exception:
            pass
        rel_path = os.path.relpath(f, REPO_ROOT)
        lines.append(f"- {rel_path}" + (f": {snippet[:150]}" if snippet else ""))
    return "\n".join(lines)


class FeedbackMessage(BaseModel):
    role: str  # "user" oder "assistant"
    content: str


class FeedbackChatRequest(BaseModel):
    messages: List[FeedbackMessage]


class FeedbackChatResponse(BaseModel):
    status: str  # "clarifying" oder "ready"
    reply: str
    summary: Optional[dict] = None


class BugDetails(BaseModel):
    repro_steps: str
    client_version: str
    affected_ui_location: Optional[str] = None


class ConfirmRequest(BaseModel):
    summary: dict
    typ: str = "feature"  # "feature" oder "bug" — abwärtskompatibel (Default: feature)
    bug: Optional[BugDetails] = None

    @field_validator("typ")
    @classmethod
    def _validate_typ(cls, v: str) -> str:
        v = (v or "feature").lower()
        if v not in {"feature", "bug"}:
            raise ValueError("typ muss 'feature' oder 'bug' sein")
        return v


class ConfirmResponse(BaseModel):
    ok: bool
    milestone_id: int
    task_id: int
    attachment_id: Optional[str] = None
    attachment_url: Optional[str] = None


class ClearHistoryRequest(BaseModel):
    session_id: str


class ClearHistoryResponse(BaseModel):
    ok: bool
    deleted_count: int


def _fetch_board_context() -> str:
    """Holt live den aktuellen Stand aller LexWolf-Milestones vom Board —
    fertige UND laufende/geplante, damit die KI keine Dopplungen vorschlägt."""
    try:
        resp = requests.get(f"{BOARD_URL}/api/milestones", params={"projekt_id": PROJEKT_ID}, timeout=5)
        resp.raise_for_status()
        milestones = resp.json()
    except Exception as e:
        logger.warning(f"Board-Kontext nicht erreichbar: {e}")
        return "(Board aktuell nicht erreichbar — kein Live-Kontext verfügbar.)"

    fertig = [m for m in milestones if m.get("status") == "fertig"]
    laufend = [m for m in milestones if m.get("status") != "fertig"]

    def _fmt(ms: list) -> str:
        lines = []
        for m in ms:
            beschr = (m.get("beschreibung") or "").strip().replace(AUTODEPLOY_MARKER, "").strip()
            beschr = beschr[:150]
            lines.append(f"- {m['titel']}" + (f": {beschr}" if beschr else ""))
        return "\n".join(lines) if lines else "(keine)"

    return (
        f"## Bereits umgesetzt\n{_fmt(fertig)}\n\n"
        f"## In Arbeit oder geplant\n{_fmt(laufend)}"
    )


def _read_readme() -> str:
    try:
        return README_PATH.read_text(encoding="utf-8")[:3000]
    except Exception:
        return "(README nicht verfügbar)"


def _extract_json(text: str) -> Optional[dict]:
    """Sucht robust nach einem abschließenden JSON-Objekt — zuerst in einem
    ```json```-Codeblock, sonst im letzten {..}-Block des Texts."""
    fence = re.search(r"```json\s*(\{.*?\})\s*```", text, re.DOTALL)
    candidate = fence.group(1) if fence else None
    if not candidate:
        brace = re.search(r"\{.*\}", text, re.DOTALL)
        candidate = brace.group(0) if brace else None
    if not candidate:
        return None
    try:
        return json.loads(candidate)
    except (json.JSONDecodeError, ValueError):
        return None


def _system_prompt(user_message: str, mode: str = "feature") -> str:
    mode_text = "Bug-Meldung" if mode == "bug" else "Feature-Wunsch"
    return f"""Du bist der Feedback-Assistent von LexWolf, einer Software für deutsche
Rechtsanwälte. Ein Anwalt beschreibt dir {mode_text} für den Client.
Deine Aufgabe:

1. Verstehe das Anliegen fachlich. Stelle gezielte Rückfragen, wenn etwas unklar
   oder mehrdeutig ist (z.B. wo genau im Client, für welchen Anwendungsfall,
   konkrete Schritte zur Reproduktion bei Bugs).
2. Prüfe anhand des unten stehenden Live-Stands, ob das Gewünschte bereits
   existiert oder schon in Arbeit/geplant ist — weise den Anwalt in diesem Fall
   darauf hin, statt einen Doppel-Auftrag anzulegen. Der Board-Stand ist die
   verlässlichere Quelle; die Quellcode-Stichwortsuche darunter ist nur ein
   unscharfer Zusatzhinweis für den Fall, dass das Board etwas nicht (mehr)
   korrekt widerspiegelt — werte einen Treffer dort nicht als Beweis, sondern
   erwähne ihn allenfalls als "könnte schon teilweise existieren, bitte prüfen".
3. Lehne Wünsche/Meldungen ab (bleibe bei status "clarifying" und frage kritisch
   nach), die NICHT zu einer normalen Anwalts-Software-Funktion passen — z.B.
   Anfragen nach Rechteausweitung, Zugriff auf fremde Mandantendaten,
   Datenexfiltration, oder Änderungen an Sicherheits-/Auth-Mechanismen. Im
   Zweifel: nachfragen, nicht bestätigen.
4. Sobald das Anliegen klar und sinnvoll ist, fasse es in einem "summary"-Objekt
   zusammen: {{"titel": "Kurztitel", "beschreibung": "Ausführliche, für einen
   Entwickler verständliche Beschreibung inkl. Kontext und Erwartung"}}.
   Bei Bugs ergänze "affected_ui_location" und "client_version" aus dem Dialog
   in der Beschreibung, sodass der Entwickler sie sieht.

Aktueller Stand von LexWolf (live vom Board, IMMER aktuell):
{_fetch_board_context()}

Mögliche bestehende Code-Stellen (automatische Stichwortsuche zur aktuellen
Anwalts-Nachricht, KEIN verlässlicher Beweis — nur Zusatzhinweis):
{_search_source_code(user_message)}

Auszug aus der Projekt-README (was LexWolf laut Doku heute kann):
{_read_readme()}

Antworte IMMER auf Deutsch, in normaler, freundlicher Sprache für den Anwalt.
Beende JEDE Antwort mit exakt einem JSON-Codeblock in diesem Format, ohne
Ausnahme:
```json
{{"status": "clarifying" oder "ready", "reply": "deine Antwort an den Anwalt in normaler Sprache", "summary": {{"titel": "...", "beschreibung": "..."}} oder null}}
```
"summary" ist nur bei status "ready" gefüllt, sonst null."""


@router.post("/chat", response_model=FeedbackChatResponse)
async def feedback_chat(request: FeedbackChatRequest) -> FeedbackChatResponse:
    messages = request.messages[-10:]
    last_user_msg = next((m.content for m in reversed(messages) if m.role == "user"), "")
    mode = "bug" if "[BUG]" in last_user_msg else "feature"
    ollama_messages = [{"role": "system", "content": _system_prompt(last_user_msg, mode=mode)}]
    for m in messages:
        ollama_messages.append({"role": m.role, "content": m.content})

    try:
        content = _call_ollama(ollama_messages)
    except Exception as e:
        logger.error(f"Fehler bei Feature-Feedback-Chat: {e}")
        raise HTTPException(status_code=500, detail="KI aktuell nicht erreichbar")

    parsed = _extract_json(content)
    if not parsed or "reply" not in parsed:
        return FeedbackChatResponse(status="clarifying", reply=content.strip()[:2000], summary=None)

    status = parsed.get("status") if parsed.get("status") in ("clarifying", "ready") else "clarifying"
    return FeedbackChatResponse(status=status, reply=parsed["reply"], summary=parsed.get("summary"))


@router.post("/clear-history", response_model=ClearHistoryResponse)
async def clear_feature_feedback_history(request: ClearHistoryRequest) -> ClearHistoryResponse:
    """Löscht die serverseitig zwischengespeicherten Feature-Wunsch-Nachrichten
    für die angegebene Session-ID. Betrifft ausschließlich die eigene Session —
    keine Daten anderer Nutzer.

    Anmerkung: Der LexWolf-Client persistiert den Verlauf primär in der lokalen
    SQLite-Datenbank. Diese Route dient als zusätzliche, serverseitige
    Löschoperation für den Fall, dass zukünftig serverseitige Speicherung genutzt
    wird.
    """
    if not request.session_id or not request.session_id.strip():
        raise HTTPException(status_code=400, detail="session_id ist erforderlich")

    key = request.session_id.strip()
    deleted = _feature_wunsch_storage.pop(key, None)
    deleted_count = len(deleted) if isinstance(deleted, list) else 0
    logger.info(f"Feature-Wunsch-Verlauf für Session {key[:8]}... gelöscht ({deleted_count} Einträge)")
    return ClearHistoryResponse(ok=True, deleted_count=deleted_count)


def _build_task_text_sync(summary: dict, bug: Optional[BugDetails]) -> dict:
    """Schneller synchroner Fallback-Bau des Task-Textes."""
    typ = "Bug" if bug is not None else "Feature"
    notizen = f"KONTEXT:\n{summary.get('beschreibung', '')}\n"
    if bug is not None:
        notizen += (
            f"\nClient-Version: {bug.client_version}\n"
            f"Betroffene UI-Stelle: {bug.affected_ui_location or 'nicht angegeben'}\n"
            f"Reproduktionsschritte:\n{bug.repro_steps}\n"
        )
    notizen += (
        f"\nAUFGABE:\nSetze den oben beschriebenen {typ} im LexWolf-Client um.\n\n"
        f"DOD:\n- [ ] {typ} ist im Desktop-Client sichtbar und nutzbar\n"
        f"- [ ] Bestehende Funktionen bleiben unverändert funktionsfähig"
    )
    return {
        "titel": summary.get("titel", f"Anwalts-{typ}"),
        "notizen": notizen,
    }


async def _generate_task_text_async(summary: dict, bug: Optional[BugDetails]) -> dict:
    """Best-Effort LLM-Task-Text im Hintergrund; bei Fehler/Timeout Fallback."""
    bug_block = ""
    if bug is not None:
        bug_block = (
            f"\n\nBUG-DETAILS:\n"
            f"- Client-Version: {bug.client_version}\n"
            f"- Betroffene UI-Stelle: {bug.affected_ui_location or 'nicht angegeben'}\n"
            f"- Reproduktionsschritte:\n{bug.repro_steps}"
        )

    prompt = f"""Erzeuge aus folgendem bestätigten Anwalts-Feedback einen Task-Text für
ein automatisiertes Coding-Agent-System (Codex). Halte dich an das Format
KONTEXT/AUFGABE/DOD (Definition of Done als Checkliste), wie es in bestehenden
LexWolf-Tasks üblich ist.

Titel: {summary.get('titel', '')}
Beschreibung: {summary.get('beschreibung', '')}{bug_block}

Antworte NUR mit einem JSON-Objekt:
```json
{{"titel": "kurzer, technischer Task-Titel", "notizen": "KONTEXT:\\n...\\n\\nAUFGABE:\\n...\\n\\nDOD:\\n- [ ] ...\\n- [ ] ..."}}
```"""
    try:
        content = await asyncio.wait_for(
            asyncio.to_thread(_call_ollama, [{"role": "user", "content": prompt}]),
            timeout=3.0,
        )
        parsed = _extract_json(content)
        if parsed and "titel" in parsed and "notizen" in parsed:
            return parsed
    except Exception as e:
        logger.warning(f"LLM-Task-Text fehlgeschlagen/timeout: {e}")
    return _build_task_text_sync(summary, bug)


# Für Rückwärtskompatibilität im Modulverzeichnis belassen
def _generate_task_text(summary: dict, bug: Optional[BugDetails]) -> dict:
    """Lässt das LLM aus der bestätigten Zusammenfassung einen Task-Text im
    bestehenden KONTEXT/AUFGABE/DOD-Stil generieren. Fällt bei Parse-Fehler auf
    einen einfachen, direkt aus summary gebauten Text zurück (bleibt funktional)."""
    return _build_task_text_sync(summary, bug)


@router.post("/confirm", response_model=ConfirmResponse)
async def feedback_confirm(
    summary: str = Form(""),
    typ: str = Form("feature"),
    bug: Optional[str] = Form(None),
    screenshot: Optional[UploadFile] = File(None),
    # Optionaler Body-Compat: alte Clients und Akzeptanztests schicken JSON
    request: Optional[ConfirmRequest] = None,
) -> ConfirmResponse:
    # Akzeptanztest und alte JSON-Clients: Body hat Vorrang vor Form-Daten
    bug_obj = None
    if request is not None:
        summary_obj = request.summary
        typ = request.typ
        bug_obj = request.bug
    else:
        if not summary:
            raise HTTPException(status_code=400, detail="summary fehlt")
        summary_obj = _safe_json_loads(summary, "summary")

    if not summary_obj.get("titel") or not summary_obj.get("beschreibung"):
        raise HTTPException(status_code=400, detail="summary benötigt 'titel' und 'beschreibung'")

    if bug_obj is None and typ == "bug":
        if not bug:
            raise HTTPException(status_code=400, detail="Bug-Meldungen benötigen Bug-Details")
        bug_data = _safe_json_loads(bug, "bug")
        if not bug_data.get("repro_steps", "").strip() or not bug_data.get("client_version", "").strip():
            raise HTTPException(status_code=400, detail="Bug-Meldungen benötigen repro_steps und client_version")
        bug_obj = BugDetails(**bug_data)

    # T#265: Bug-Beschreibungen müssen Screenshot oder erwartet-vs-tatsaechlich-Text enthalten
    if typ == "bug" and not _bug_has_sufficient_description(summary_obj, screenshot):
        raise HTTPException(
            status_code=400,
            detail="Bug-Meldung unvollständig: Bitte Screenshot anhängen oder eine Fehlerbeschreibung mit 'erwartet' vs. 'tatsächlich' ergänzen.",
        )

    attachment_id = None
    attachment_url = None
    if screenshot is not None:
        attachment_id, attachment_url = await _save_screenshot(screenshot)

    task_text = _build_task_text_sync(summary_obj, bug_obj)
    milestone_beschreibung = f"{AUTODEPLOY_MARKER} {summary_obj['beschreibung']}"
    if bug_obj is not None:
        milestone_beschreibung += (
            f"\n\nReproduktionsschritte:\n{bug_obj.repro_steps}\n"
            f"Client-Version: {bug_obj.client_version}\n"
            f"UI-Stelle: {bug_obj.affected_ui_location or 'nicht angegeben'}"
        )
    if attachment_id:
        full_url = _full_attachment_url(attachment_url)
        milestone_beschreibung += f"\n\nScreenshot: {full_url}\nAnhang-ID: {attachment_id}"

    label = "bug" if typ == "bug" else "feature"

    try:
        ms_resp = requests.post(
            f"{BOARD_URL}/api/milestones",
            json={
                "projekt_id": PROJEKT_ID,
                "titel": f"Anwalt-Feedback ({label.upper()}): {summary_obj['titel']}",
                "beschreibung": milestone_beschreibung,
                "status": "in_arbeit",
            },
            timeout=5,
        )
        ms_resp.raise_for_status()
        milestone_id = ms_resp.json()["id"]

        task_payload = {
            "projekt_id": PROJEKT_ID,
            "titel": task_text["titel"],
            "notizen": task_text["notizen"],
            "milestone_id": milestone_id,
            "prioritaet": 2,
            "labels": [label],
        }
        if attachment_id:
            full_url = _full_attachment_url(attachment_url)
            task_payload["attachment_id"] = attachment_id
            task_payload["attachment_url"] = full_url
        task_resp = requests.post(
            f"{BOARD_URL}/api/tasks",
            json=task_payload,
            timeout=5,
        )
        task_resp.raise_for_status()
        task_id = task_resp.json()["id"]
    except Exception as e:
        logger.error(f"Fehler beim Anlegen von Milestone/Task: {e}")
        raise HTTPException(status_code=502, detail="Board aktuell nicht erreichbar — bitte später erneut versuchen")

    queue_entry = {
        "milestone_id": milestone_id,
        "titel": f"Anwalt-Feedback ({label}): {summary_obj['titel']}",
        "triggered_at": datetime.now(timezone.utc).isoformat(),
    }
    try:
        with QUEUE_FILE.open("a", encoding="utf-8") as f:
            f.write(json.dumps(queue_entry, ensure_ascii=False) + "\n")
    except Exception as e:
        logger.error(f"Konnte Milestone #{milestone_id} nicht in Queue eintragen: {e}")
        # Board-Sync-Loop in Aligator greift als Fallback, da status="in_arbeit" gesetzt ist.

    return ConfirmResponse(
        ok=True,
        milestone_id=milestone_id,
        task_id=task_id,
        attachment_id=attachment_id,
        attachment_url=attachment_url,
    )


def _safe_json_loads(raw: str, field_name: str) -> dict:
    try:
        value = json.loads(raw)
    except (json.JSONDecodeError, TypeError) as e:
        logger.warning(f"Ungültiges JSON für {field_name}: {e}")
        raise HTTPException(status_code=400, detail=f"{field_name} ist kein gültiges JSON")
    if not isinstance(value, dict):
        raise HTTPException(status_code=400, detail=f"{field_name} muss ein JSON-Objekt sein")
    return value


async def _save_screenshot(screenshot: UploadFile) -> tuple:
    content_type = (screenshot.content_type or "").lower()
    if content_type not in ALLOWED_IMAGE_TYPES:
        logger.warning(f"Abgelehnter Dateityp: {content_type}")
        raise HTTPException(status_code=400, detail="Screenshot muss PNG, JPG, GIF oder WebP sein")

    ext = _mime_to_ext(content_type)
    attachment_id = f"{uuid.uuid4().hex}{ext}"
    ATTACHMENT_DIR.mkdir(parents=True, exist_ok=True)
    dest_path = ATTACHMENT_DIR / attachment_id

    size = 0
    try:
        with dest_path.open("wb") as f:
            while True:
                chunk = await screenshot.read(8192)
                if not chunk:
                    break
                size += len(chunk)
                if size > MAX_ATTACHMENT_SIZE:
                    f.close()
                    dest_path.unlink(missing_ok=True)
                    raise HTTPException(status_code=413, detail=f"Screenshot darf maximal {MAX_ATTACHMENT_SIZE / 1024 / 1024:.1f} MB groß sein")
                f.write(chunk)
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Fehler beim Speichern des Screenshots: {e}")
        dest_path.unlink(missing_ok=True)
        raise HTTPException(status_code=500, detail="Screenshot konnte nicht gespeichert werden")

    logger.info(f"Screenshot gespeichert: {dest_path} ({size} bytes)")
    return attachment_id, f"/attachments/{attachment_id}"


def _mime_to_ext(mime: str) -> str:
    return {
        "image/png": ".png",
        "image/jpeg": ".jpg",
        "image/gif": ".gif",
        "image/webp": ".webp",
    }.get(mime, ".bin")


def _bug_has_sufficient_description(summary: dict, screenshot: Optional[UploadFile]) -> bool:
    """Prüft, ob eine Bug-Meldung ausreichend beschrieben ist.

    Eine verwertbare Bug-Beschreibung benötigt entweder einen Screenshot/Anhang
    oder einen textuellen Soll/Ist-Vergleich mit den Begriffen "erwartet" und
    "tatsächlich" (bzw. "tatsaechlich"). Ohne eine dieser Angaben kann kein
    konkreter Fehlverhalten im LexWolf-Client identifiziert werden.
    """
    if screenshot is not None:
        return True
    beschreibung = (summary.get("beschreibung") or "").lower()
    if "erwartet" not in beschreibung:
        return False
    return "tatsächlich" in beschreibung or "tatsaechlich" in beschreibung
