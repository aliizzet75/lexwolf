from fastapi import APIRouter

router = APIRouter(prefix="/client", tags=["client-update"])

# Aktuellste veroeffentlichte Desktop-Client-Version.
# Solange wir nicht produktiv sind: 0.x-Versionierung.
# Bei jedem Release manuell erhoehen — download_url bleibt stabil (kein
# Versions-Suffix im Dateinamen), nur die Datei dahinter wird ersetzt.
LATEST_VERSION = "0.14.0"
DOWNLOAD_URL = "http://212.227.180.66:8000/client/download/LexWolf-Setup.exe"
RELEASE_NOTES = "Der bestehende „Feature-Wunsch“-Dialog im LexWolf-Client (desktop/Dialogs/FeatureWunschDialog.xaml) soll um eine Bug-Meldung erweitert werden. Anwälte sollen dort neben Verbesserungsvorschlägen auch F"


@router.get("/version")
async def get_latest_version():
    return {
        "version": LATEST_VERSION,
        "download_url": DOWNLOAD_URL,
        "notes": RELEASE_NOTES,
    }
