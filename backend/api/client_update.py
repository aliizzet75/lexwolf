from fastapi import APIRouter

router = APIRouter(prefix="/client", tags=["client-update"])

# Aktuellste veroeffentlichte Desktop-Client-Version.
# Solange wir nicht produktiv sind: 0.x-Versionierung.
# Bei jedem Release manuell erhoehen — download_url bleibt stabil (kein
# Versions-Suffix im Dateinamen), nur die Datei dahinter wird ersetzt.
LATEST_VERSION = "0.20.0"
DOWNLOAD_URL = "http://212.227.180.66:8000/client/download/LexWolf-Setup.exe"
RELEASE_NOTES = "Manueller Verifikationstest  Reproduktionsschritte: 1. x 2. y Client-Version: 9.9.9 UI-Stelle: Testbereich  Screenshot: /attachments/90e8793fc03e4a48b12e432d0cb64ebd.png"


@router.get("/version")
async def get_latest_version():
    return {
        "version": LATEST_VERSION,
        "download_url": DOWNLOAD_URL,
        "notes": RELEASE_NOTES,
    }
