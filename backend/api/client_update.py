from fastapi import APIRouter, Query

router = APIRouter(prefix="/client", tags=["client-update"])

# Versionshistorie der veroeffentlichten Desktop-Client-Releases, aelteste
# zuerst. Bei jedem Release wird ein neuer Eintrag ANGEHAENGT (nicht
# ueberschrieben) — so koennen Clients, die mehrere Releases verpasst haben,
# beim naechsten Start alle Neuigkeiten seit ihrer eigenen Version sehen,
# statt nur die Notiz des letzten Releases.
# Solange wir nicht produktiv sind: 0.x-Versionierung.
# download_url bleibt stabil (kein Versions-Suffix im Dateinamen), nur die
# Datei dahinter wird ersetzt.
DOWNLOAD_URL = "http://212.227.180.66:8000/client/download/LexWolf-Setup.exe"
VERSION_HISTORY: list[tuple[str, str]] = [
    (
        "0.46.0",
        "Absturz beim Oeffnen von Einstellungen und Feature-Wunsch behoben "
        "(globales ComboBox-Popup-Template war seit dem 22.09. kaputt).",
    ),
    (
        "0.47.0",
        "Absturz beim Oeffnen von Feature-Wunsch behoben (SelectionChanged feuerte waehrend InitializeComponent, bevor BugFieldsPanel verbunden war). Versionsnummer von Client (.exe) und Server synchronisiert, damit das Update-Popup nicht mehr bei jedem Neustart erneut erscheint. Update-Hinweis zeigt jetzt alle verpassten Versionen gesammelt an.",
    ),
    (
        "0.48.0",
        "Fachliche Anforderung des Anwalts: KI soll Rechtsfragen/Rechtsfälle im Gutachtenstil prüfen können — Anspruchsgrundlage(n) ermitteln, Voraussetzungen durchgehen, Einwände/Angriffsmöglichkeiten prüfen,",
    ),
    (
        "0.49.0",
        "Fachliche Anforderung des Anwalts: KI soll Rechtsfragen/Rechtsfälle im Gutachtenstil prüfen können — Anspruchsgrundlage(n) ermitteln, Voraussetzungen durchgehen, Einwände/Angriffsmöglichkeiten prüfen,",
    ),
    (
        "0.50.0",
        "Fachliche Anforderung des Anwalts: KI soll Rechtsfragen/Rechtsfälle im Gutachtenstil prüfen können — Anspruchsgrundlage(n) ermitteln, Voraussetzungen durchgehen, Einwände/Angriffsmöglichkeiten prüfen,",
    ),
    (
        "0.51.0",
        "Fachliche Anforderung des Anwalts: KI soll Rechtsfragen/Rechtsfälle im Gutachtenstil prüfen können — Anspruchsgrundlage(n) ermitteln, Voraussetzungen durchgehen, Einwände/Angriffsmöglichkeiten prüfen,",
    ),
    (
        "0.52.0",
        "Fachliche Anforderung des Anwalts: KI soll Rechtsfragen/Rechtsfälle im Gutachtenstil prüfen können — Anspruchsgrundlage(n) ermitteln, Voraussetzungen durchgehen, Einwände/Angriffsmöglichkeiten prüfen,",
    ),
    (
        "0.53.0",
        "Fachliche Anforderung des Anwalts: KI soll Rechtsfragen/Rechtsfälle im Gutachtenstil prüfen können — Anspruchsgrundlage(n) ermitteln, Voraussetzungen durchgehen, Einwände/Angriffsmöglichkeiten prüfen,",
    ),
    (
        "0.54.0",
        "502 Bad Gateway bei Schriftsatz-Entwurf behoben (Server-Konfiguration: LLM_PROVIDER=ollama, OLLAMA_MODEL=gemma4:31b-cloud in lexwolf-backend.service — bisherige Cloud-Modelle waren zurueckgezogen/ohne",
    ),
    (
        "0.55.0",
        "502 Bad Gateway bei Schriftsatz-Entwurf behoben (Server-Konfiguration: LLM_PROVIDER=ollama, OLLAMA_MODEL=gemma4:31b-cloud in lexwolf-backend.service — bisherige Cloud-Modelle waren zurueckgezogen/ohne",
    ),
    (
        "0.56.0",
        "502 Bad Gateway bei Schriftsatz-Entwurf behoben (Server-Konfiguration: LLM_PROVIDER=ollama, OLLAMA_MODEL=gemma4:31b-cloud in lexwolf-backend.service — bisherige Cloud-Modelle waren zurueckgezogen/ohne",
    ),
    (
        "0.57.0",
        "502 Bad Gateway bei Schriftsatz-Entwurf behoben (Server-Konfiguration: LLM_PROVIDER=ollama, OLLAMA_MODEL=gemma4:31b-cloud in lexwolf-backend.service — bisherige Cloud-Modelle waren zurueckgezogen/ohne",
    ),
    (
        "0.58.0",
        "502 Bad Gateway bei Schriftsatz-Entwurf behoben (Server-Konfiguration: LLM_PROVIDER=ollama, OLLAMA_MODEL=gemma4:31b-cloud in lexwolf-backend.service — bisherige Cloud-Modelle waren zurueckgezogen/ohne",
    ),
    (
        "0.59.0",
        "M9 (Whisper-Transkription, DSGVO-Einwilligung, Gespräch-Strukturierung, Post-Gespräch-Vorschläge) wurde gestern nach main gemerged, aber nie released — der Versionsbump ging auf dem verwaisten M8-Bran",
    ),
    (
        "0.60.0",
        "GespraechsRecorder/Einwilligungsdialog/VorschlaegeService (T#127-131) waren implementiert, aber nie an die UI angeschlossen. Button \"Gespräch aufnehmen\" neben \"Schriftsatz\" ergänzt und kompletten Pfad",
    ),
    (
        "0.61.0",
        "Aufnahme-Dialog mit Timer, Wolf-Animation und großem Stop-Knopf statt Button-Toggle. Whisper-Modell laedt sich jetzt direkt ueber den Gespraech-Button herunter. Kurze Zusammenfassung vor den Vorschlae",
    ),
    (
        "0.62.0",
        "Nutzer-Feedback zur Gespräch-Aufnahme: Dialog verschwand ohne Hinweis waehrend Transkription/Strukturierung (jetzt bitte-warten-Zustand). Gespraechsprotokoll und angenommene Vorschlaege (Fristennotiz/",
    ),
    (
        "0.63.0",
        "Nutzer-Feedback: Vorschlaege-Textbox blieb klein trotz maximiertem Fenster (MaxHeight entfernt). Speicher-Fehler bei Gespraechsprotokoll/E-Mail-Entwurf gingen nur ins Log (jetzt laute Meldung, Abbruch",
    ),
    (
        "0.64.0",
        "BUENDEL-RELEASE (2026-10-26, Retry nach Merge-Konflikt-Fix): Alle 5 Datenqualitaets-Milestones: M19 Rohmaterial-Expansion (Urteile + Gesetzesbegruendungen), M24 Zitier-Graph & Multi-Hop-Suche, M18 Qua",
    ),
    (
        "0.65.0",
        "Nutzer-Feedback: Word-Export-Dateiname sollte Inhalt statt Datum zeigen (jetzt wegen:-Zeile aus dem Entwurf, Fallback Datum). Dokumentenauswahl braucht Oeffnen-Button pro Dokument zur Pruefung vor Ein",
    ),
    (
        "0.66.0",
        "Nutzer-Feedback (echter Entwurf): Text war abgehackt (Stakkato-Kurzsaetze), und [D1]/[D2]-Belegverweise landeten im an die Gegenseite versendeten Dokument. Backend-Prompt verlangt jetzt fluessige verb",
    ),
    (
        "0.67.0",
        "BUENDEL-RELEASE (2026-10-26): M27 Qualitaetsanzeige-Bugfixes (Konfidenz-Fehlberechnung repariert, 404 bei Quellen-Klick gefixt, Schriftsatz-Konfidenz+Quellen-Anbindung) + M28 Vorlagen-Verwaltung (Date",
    ),
    (
        "0.68.0",
        "Nutzer-Feedback: Umbenennen eines Dokuments stuerzte die App ab. Ursache: DeleteDokumentChunks nach File.Move kann SqliteException werfen (Scanner greift evtl. gleichzeitig auf DB zu), nicht von IOExc",
    ),
]

LATEST_VERSION = VERSION_HISTORY[-1][0]


def _parse(version: str) -> tuple[int, ...]:
    parts = [int(p) for p in version.split(".")]
    parts += [0] * (4 - len(parts))
    return tuple(parts[:4])


@router.get("/version")
async def get_latest_version(current_version: str | None = Query(default=None)):
    notes = VERSION_HISTORY[-1][1]

    if current_version:
        try:
            current = _parse(current_version)
        except ValueError:
            current = None
        if current is not None:
            missed = [(v, n) for v, n in VERSION_HISTORY if _parse(v) > current]
            if len(missed) > 1:
                notes = "\n\n".join(f"{v}: {n}" for v, n in missed)
            elif missed:
                notes = missed[0][1]

    return {
        "version": LATEST_VERSION,
        "download_url": DOWNLOAD_URL,
        "notes": notes,
    }
