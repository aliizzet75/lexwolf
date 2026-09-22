from fastapi import APIRouter

router = APIRouter(prefix="/client", tags=["client-update"])

# Aktuellste veroeffentlichte Desktop-Client-Version.
# Solange wir nicht produktiv sind: 0.x-Versionierung.
# Bei jedem Release manuell erhoehen — download_url bleibt stabil (kein
# Versions-Suffix im Dateinamen), nur die Datei dahinter wird ersetzt.
LATEST_VERSION = "0.15.0"
DOWNLOAD_URL = "http://212.227.180.66:8000/client/download/LexWolf-Setup.exe"
RELEASE_NOTES = "Der LexWolf-Client soll im erweiterten Feature-Wunsch-/Bug-Meldungs-Dialog die Möglichkeit bieten, Screenshots an Bug-Meldungen anzuhängen. Anwälte können dabei entweder ein Bild aus der Zwischenablag"


@router.get("/version")
async def get_latest_version():
    return {
        "version": LATEST_VERSION,
        "download_url": DOWNLOAD_URL,
        "notes": RELEASE_NOTES,
    }
