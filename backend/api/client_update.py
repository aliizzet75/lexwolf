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
