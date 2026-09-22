from fastapi import APIRouter

router = APIRouter(prefix="/client", tags=["client-update"])

# Aktuellste veroeffentlichte Desktop-Client-Version.
# Solange wir nicht produktiv sind: 0.x-Versionierung.
# Bei jedem Release manuell erhoehen — download_url bleibt stabil (kein
# Versions-Suffix im Dateinamen), nur die Datei dahinter wird ersetzt.
LATEST_VERSION = "0.13.0"
DOWNLOAD_URL = "http://212.227.180.66:8000/client/download/LexWolf-Setup.exe"
RELEASE_NOTES = "Zwei zusammenhängende Verbesserungen für den LexWolf-Client: (A) Die Mandantenliste (linke Liste und Auswahldropdown im Hauptfenster) soll bei vielen Mandanten performant geladen werden. Dazu ist die "


@router.get("/version")
async def get_latest_version():
    return {
        "version": LATEST_VERSION,
        "download_url": DOWNLOAD_URL,
        "notes": RELEASE_NOTES,
    }
