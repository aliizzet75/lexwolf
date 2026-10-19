"""
Prompt-Aufbau und Intent-Erkennung für /chat — bewusst ohne schwere Imports
(keine DB, keine Embeddings, kein LLM-SDK).
"""
import re
from typing import Optional

# Vor "erstelle"/"berechne" geprüft: "Schreib eine Mail an X wegen des Unterhalts"
# ist eine E-Mail, kein Schreiben und keine Unterhaltsberechnung.
_EMAIL_RE = re.compile(r"\b(e-?mail|mail)\b")


def detect_intent(text: str) -> str:
    """Einfache Intent-Erkennung für Chat ohne schwere Embedding-Last."""
    lower = text.lower()
    if _EMAIL_RE.search(lower):
        return "email"
    if any(k in lower for k in ("erstell", "schreib", "formulier", "aufsetzen", "entwurf", "verfass")):
        return "erstelle"
    if any(k in lower for k in ("berechne", "berechnung", "höhe", "betrag", "unterhalt", "wie viel", "wieviel")):
        return "berechne"
    return "frage"


def intent_to_action(intent: str) -> str:
    return {
        "email": "erstelle_email",
        "erstelle": "erstelle_dokument",
        "berechne": "berechne_unterhalt",
        "frage": "frage",
    }.get(intent, "frage")


# Format des Entwurf-Blocks — geparst im Desktop-Client (desktop/Services/ChatEntwurf.cs).
ENTWURF_BEISPIEL = (
    '  <<<ENTWURF art="brief" titel="…" an="…" betreff="…" ueberarbeitung="nein">>>\n'
    "  …nur der Text…\n"
    "  <<<ENDE>>>"
)
_ENTWURF_RE = re.compile(r"<<<\s*ENTWURF\b([^>]*)>>>", re.IGNORECASE)
_ART_RE = re.compile(r'art\s*=\s*"([^"]*)"', re.IGNORECASE)
# Merkmale eines Briefs/einer E-Mail, falls das Modell den Block vergisst.
_SCHREIBEN_RE = re.compile(r"sehr geehrte|mit freundlichen grüßen|^\s*\**\s*(an|betreff)\s*:", re.IGNORECASE | re.MULTILINE)


def action_for_response(content: str, intent: str) -> str:
    """Aktion für den Client: maßgeblich ist der Entwurf-Block der KI. Fehlt er,
    gilt die Stichwort-Erkennung nur, wenn die Antwort auch wie ein Schreiben
    aussieht — sonst würde z.B. "beschreib mir …" zu einem Word-Dokument."""
    m = _ENTWURF_RE.search(content)
    if m:
        art = _ART_RE.search(m.group(1))
        return "erstelle_email" if art and art.group(1).strip().lower() in ("email", "e-mail", "mail") else "erstelle_dokument"
    action = intent_to_action(intent)
    if action in ("erstelle_email", "erstelle_dokument") and not _SCHREIBEN_RE.search(content):
        return "frage"
    return action


def build_system_prompt(chunks_text: str, mandant_context: Optional[str]) -> str:
    system_parts = ["Du bist ein juristischer Rechtsassistent für deutschsprachige Anwälte."]
    if chunks_text:
        system_parts.append(f"\nRelevante Rechtsgrundlagen aus der Datenbank:\n{chunks_text}")
    if mandant_context:
        system_parts.append(f"\nMandanteninformation: {mandant_context}")
    system_parts.append(
        "\nBeantworte Fragen präzise auf Basis der Rechtsgrundlagen, auf Deutsch. "
        "Stelle Rückfragen wenn wichtige Informationen fehlen. "
        "Zitiere Normen genau (z.B. § 1361 Abs. 1 BGB). Rechtsprechung nur mit "
        "Aktenzeichen zitieren, wenn du dir sicher bist, dass es die Entscheidung "
        "gibt und sie das Gesagte trägt — sonst ohne Fundstelle formulieren und "
        "auf Recherchebedarf hinweisen. Erfinde nie Aktenzeichen oder Fundstellen. "
        "Gib ausschließlich die fertige Antwort aus — keine Zwischengedanken, "
        "keine Meta-Kommentare zur Aufgabenstellung, kein Denkprotokoll.\n"
        "\nWichtig zu Mandanten-Dokumenten:\n"
        "- Ein Dokument gilt nur dann als NICHT lesbar, wenn sein Text explizit eine "
        "Fehlermeldung in eckigen Klammern enthält (z.B. '[Datei: PDF konnte nicht "
        "gelesen werden ...]' oder '[... OCR ...]'). In jedem anderen Fall wurde es "
        "erfolgreich gelesen — behaupte dann NIEMALS, es sei 'nicht auswertbar' oder "
        "'nicht lesbar'. Wenn die gesuchte Information/Person darin schlicht nicht "
        "vorkommt, sag das direkt (z.B. 'Dieses Dokument betrifft eine andere Person/"
        "einen anderen Sachverhalt: ...'), statt einen Lesefehler zu unterstellen.\n"
        "- Mehrere Dokumente desselben Mandanten gehören oft zusammen. Rollen- oder "
        "Kürzel-Bezeichnungen in einem Dokument (z.B. 'Mann'/'Frau', 'Hauptverdiener'/"
        "'Zweitverdiener', 'Antragsteller'/'Antragsgegnerin') können sich auf konkret "
        "benannte Personen aus einem anderen Dokument desselben Mandanten beziehen — "
        "prüfe das und verknüpfe die Angaben, statt jedes Dokument isoliert zu lesen.\n"
        "\nWichtig zu Schreiben und E-Mails (der Client legt sie als Word-Dokument bzw. "
        "E-Mail-Entwurf in der Akte ab):\n"
        "- Sobald deine Antwort einen versandfertigen Text enthält — Schreiben, Brief, "
        "Schriftsatz oder E-Mail —, setze genau diesen Text in einen Block:\n"
        f"{ENTWURF_BEISPIEL}\n"
        "  Das gilt auch, wenn der Anwalt nur kurz zustimmt (z.B. 'ja', 'mach das') "
        "oder eine Antwort an jemanden erbittet.\n"
        "- art=\"email\" für E-Mails, sonst art=\"brief\". titel: kurze Beschreibung "
        "für die Übersicht (z.B. 'Stellungnahme an RA Dr. Klein'). an: Empfänger "
        "(bei E-Mails die Adresse, falls bekannt). betreff: Betreff. "
        "ueberarbeitung=\"ja\" nur, wenn du einen vorherigen Entwurf auf Wunsch des "
        "Anwalts änderst, sonst \"nein\".\n"
        "- Im Block steht nur der Text selbst: keine Einleitung wie 'Hier ist der "
        "Entwurf', keine Anmerkungen. E-Mails als reiner Text ohne Markdown. "
        "Platzhalter für Unbekanntes in eckigen Klammern, z.B. [Aktenzeichen].\n"
        "- Außerhalb des Blocks höchstens ein bis zwei kurze Sätze (z.B. was noch zu "
        "ergänzen ist). Pro Antwort höchstens ein Block. Fragen, Erklärungen und "
        "Prüfungen ohne versandfertigen Text bekommen keinen Block."
    )
    return "\n".join(system_parts)


def format_zitatpruefung(belege: list) -> str:
    """Hinweis unter einer Chat-Antwort, welche zitierten Normen und Entscheidungen
    in der LexWolf-Datenbank gefunden wurden. Leer, wenn nichts zitiert wurde
    oder die Datenbank für keines der Zitate erreichbar war."""
    zeilen = []
    for b in belege:
        if b.get("art") not in ("Norm", "Rechtsprechung") or b.get("status") == "nicht prüfbar":
            continue
        if b["status"] == "geprüft":
            zeilen.append(f"✓ {b['zitat']} — in der Datenbank gefunden")
        elif b["status"] == "nicht in Datenbank":
            zeilen.append(f"○ {b['zitat']} — Gesetz nicht in der Datenbank, bitte selbst prüfen")
        else:
            zusatz = f" ({b['hinweis']})" if b.get("hinweis") and b["hinweis"] != "nicht prüfbar" else ""
            zeilen.append(f"⚠ {b['zitat']} — nicht bestätigt{zusatz}, bitte vor Verwendung prüfen")
    if not zeilen:
        return ""
    return "\n\n---\n🔎 Zitatprüfung:\n" + "\n".join(f"- {z}" for z in zeilen)


def format_chunks(results: list) -> str:
    """Kompakter Kontext-Text aus Such-Treffern (max. 5, je 300 Zeichen)."""
    parts = []
    for r in results[:5]:
        title = r.get("title", "")
        text = (r.get("text") or "")[:300].strip()
        if title or text:
            parts.append(f"[{title}] {text}")
    return "\n\n".join(parts)
