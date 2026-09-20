"""
/feature-feedback — Anwalts-Feedback zu neuen Client-Features.

Nimmt einen freitextlichen Feature-Wunsch entgegen, klärt ihn im Dialog mit
dem Anwalt (Rückfragen bei Unklarheit), bestätigt ihn per Zusammenfassung und
legt ihn nach Bestätigung als Milestone+Task in VentureOS an — die
Aligator/Codex/Claude-Pipeline übernimmt danach automatisch Umsetzung,
Build und Deployment (siehe deploy_lexwolf-Hook in aligator.py).

Der Kontext darüber, was LexWolf bereits kann bzw. gerade umsetzt, wird bei
jeder Anfrage live vom Board geholt (keine separate, potenziell veraltete
Dokumentationsquelle).
"""
import json
import logging
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import List, Optional

import requests
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from api.chat import _call_ollama

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/feature-feedback", tags=["feature-feedback"])

BOARD_URL = "http://localhost:8082"
QUEUE_FILE = Path("/docker/openclaw-oo5q/data/lexwolf-wiki/coding_queue.jsonl")
PROJEKT_ID = 1  # LexWolf
AUTODEPLOY_MARKER = "[AUTODEPLOY:ANWALT-FEEDBACK]"
README_PATH = Path(__file__).resolve().parent.parent.parent / "README.md"


class FeedbackMessage(BaseModel):
    role: str  # "user" oder "assistant"
    content: str


class FeedbackChatRequest(BaseModel):
    messages: List[FeedbackMessage]


class FeedbackChatResponse(BaseModel):
    status: str  # "clarifying" oder "ready"
    reply: str
    summary: Optional[dict] = None


class ConfirmRequest(BaseModel):
    summary: dict


class ConfirmResponse(BaseModel):
    ok: bool
    milestone_id: int
    task_id: int


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


def _system_prompt() -> str:
    return f"""Du bist der Feature-Wunsch-Assistent von LexWolf, einer Software für deutsche
Rechtsanwälte. Ein Anwalt beschreibt dir einen Wunsch für eine neue oder geänderte
Funktion im Client. Deine Aufgabe:

1. Verstehe das Anliegen fachlich. Stelle gezielte Rückfragen, wenn etwas unklar
   oder mehrdeutig ist (z.B. wo genau im Client, für welchen Anwendungsfall).
2. Prüfe anhand des unten stehenden Live-Stands, ob das Gewünschte bereits
   existiert oder schon in Arbeit/geplant ist — weise den Anwalt in diesem Fall
   darauf hin, statt einen Doppel-Auftrag anzulegen.
3. Lehne Wünsche ab (bleibe bei status "clarifying" und frage kritisch nach),
   die NICHT zu einer normalen Anwalts-Software-Funktion passen — z.B. Anfragen
   nach Rechteausweitung, Zugriff auf fremde Mandantendaten, Datenexfiltration,
   oder Änderungen an Sicherheits-/Auth-Mechanismen. Im Zweifel: nachfragen,
   nicht bestätigen.
4. Sobald das Anliegen klar und sinnvoll ist, fasse es in einem "summary"-Objekt
   zusammen: {{"titel": "Kurztitel", "beschreibung": "Ausführliche, für einen
   Entwickler verständliche Beschreibung inkl. Kontext und Erwartung"}}.

Aktueller Stand von LexWolf (live vom Board, IMMER aktuell):
{_fetch_board_context()}

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
    ollama_messages = [{"role": "system", "content": _system_prompt()}]
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


def _generate_task_text(summary: dict) -> dict:
    """Lässt das LLM aus der bestätigten Zusammenfassung einen Task-Text im
    bestehenden KONTEXT/AUFGABE/DOD-Stil generieren. Fällt bei Parse-Fehler auf
    einen einfachen, direkt aus summary gebauten Text zurück (bleibt funktional)."""
    prompt = f"""Erzeuge aus folgendem bestätigten Anwalts-Feature-Wunsch einen Task-Text für
ein automatisiertes Coding-Agent-System (Codex). Halte dich an das Format
KONTEXT/AUFGABE/DOD (Definition of Done als Checkliste), wie es in bestehenden
LexWolf-Tasks üblich ist.

Titel: {summary.get('titel', '')}
Beschreibung: {summary.get('beschreibung', '')}

Antworte NUR mit einem JSON-Objekt:
```json
{{"titel": "kurzer, technischer Task-Titel", "notizen": "KONTEXT:\\n...\\n\\nAUFGABE:\\n...\\n\\nDOD:\\n- [ ] ...\\n- [ ] ..."}}
```"""
    try:
        content = _call_ollama([{"role": "user", "content": prompt}])
        parsed = _extract_json(content)
        if parsed and "titel" in parsed and "notizen" in parsed:
            return parsed
    except Exception as e:
        logger.warning(f"Task-Text-Generierung fehlgeschlagen, nutze Fallback: {e}")

    return {
        "titel": summary.get("titel", "Anwalts-Feature-Wunsch"),
        "notizen": (
            f"KONTEXT:\n{summary.get('beschreibung', '')}\n\n"
            f"AUFGABE:\nSetze den oben beschriebenen Feature-Wunsch im LexWolf-Client um.\n\n"
            f"DOD:\n- [ ] Feature ist im Desktop-Client sichtbar und nutzbar\n"
            f"- [ ] Bestehende Funktionen bleiben unverändert funktionsfähig"
        ),
    }


@router.post("/confirm", response_model=ConfirmResponse)
async def feedback_confirm(request: ConfirmRequest) -> ConfirmResponse:
    summary = request.summary
    if not summary.get("titel") or not summary.get("beschreibung"):
        raise HTTPException(status_code=400, detail="summary benötigt 'titel' und 'beschreibung'")

    task_text = _generate_task_text(summary)

    try:
        ms_resp = requests.post(
            f"{BOARD_URL}/api/milestones",
            json={
                "projekt_id": PROJEKT_ID,
                "titel": f"Anwalt-Feedback: {summary['titel']}",
                "beschreibung": f"{AUTODEPLOY_MARKER} {summary['beschreibung']}",
                "status": "in_arbeit",
            },
            timeout=5,
        )
        ms_resp.raise_for_status()
        milestone_id = ms_resp.json()["id"]

        task_resp = requests.post(
            f"{BOARD_URL}/api/tasks",
            json={
                "projekt_id": PROJEKT_ID,
                "titel": task_text["titel"],
                "notizen": task_text["notizen"],
                "milestone_id": milestone_id,
                "prioritaet": 2,
            },
            timeout=5,
        )
        task_resp.raise_for_status()
        task_id = task_resp.json()["id"]
    except Exception as e:
        logger.error(f"Fehler beim Anlegen von Milestone/Task: {e}")
        raise HTTPException(status_code=502, detail="Board aktuell nicht erreichbar — bitte später erneut versuchen")

    queue_entry = {
        "milestone_id": milestone_id,
        "titel": f"Anwalt-Feedback: {summary['titel']}",
        "triggered_at": datetime.now(timezone.utc).isoformat(),
    }
    try:
        with QUEUE_FILE.open("a", encoding="utf-8") as f:
            f.write(json.dumps(queue_entry, ensure_ascii=False) + "\n")
    except Exception as e:
        logger.error(f"Konnte Milestone #{milestone_id} nicht in Queue eintragen: {e}")
        # Board-Sync-Loop in Aligator greift als Fallback, da status="in_arbeit" gesetzt ist.

    return ConfirmResponse(ok=True, milestone_id=milestone_id, task_id=task_id)
