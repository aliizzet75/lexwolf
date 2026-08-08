from fastapi import APIRouter

router = APIRouter(prefix="/tools", tags=["tool-update"])

# Aktuellste veröffentlichte Anonymisierer-Version.
# Bei jedem Release manuell erhöhen und download_url auf den neuen Installer setzen.
ANONYMISIERER_VERSION = "1.0.0"
ANONYMISIERER_DOWNLOAD_URL = "http://212.227.180.66:8000/tools/download/anonymisierer/Anonymisierer-Setup-1.0.0.exe"
ANONYMISIERER_NOTES = "Erstveröffentlichung des Update-Checks."


@router.get("/anonymisierer/version")
async def get_anonymisierer_version():
    return {
        "version": ANONYMISIERER_VERSION,
        "download_url": ANONYMISIERER_DOWNLOAD_URL,
        "notes": ANONYMISIERER_NOTES,
    }
