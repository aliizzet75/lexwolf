from fastapi import APIRouter

router = APIRouter(prefix="/client", tags=["client-update"])

# Aktuellste veröffentlichte Desktop-Client-Version.
# Bei jedem Release manuell erhöhen und download_url auf den neuen Installer setzen.
LATEST_VERSION = "1.0.0"
DOWNLOAD_URL = "http://212.227.180.66:8000/client/download/LexWolf-Setup-1.0.0.exe"
RELEASE_NOTES = "Erstveröffentlichung des Update-Checks."


@router.get("/version")
async def get_latest_version():
    return {
        "version": LATEST_VERSION,
        "download_url": DOWNLOAD_URL,
        "notes": RELEASE_NOTES,
    }
