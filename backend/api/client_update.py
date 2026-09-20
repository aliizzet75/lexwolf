from fastapi import APIRouter

router = APIRouter(prefix="/client", tags=["client-update"])

# Aktuellste veroeffentlichte Desktop-Client-Version.
# Solange wir nicht produktiv sind: 0.x-Versionierung.
# Bei jedem Release manuell erhoehen — download_url bleibt stabil (kein
# Versions-Suffix im Dateinamen), nur die Datei dahinter wird ersetzt.
LATEST_VERSION = "0.10.0"
DOWNLOAD_URL = "http://212.227.180.66:8000/client/download/LexWolf-Setup.exe"
RELEASE_NOTES = "KI-Antworten und Nutzereingaben im LexWolf-Chat sollen mit der Maus markier- und in die Zwischenablage kopierbar sein (Strg+C / Kontextmenü). Technische Voraussetzung ist die geplante Umstellung der C"


@router.get("/version")
async def get_latest_version():
    return {
        "version": LATEST_VERSION,
        "download_url": DOWNLOAD_URL,
        "notes": RELEASE_NOTES,
    }
