from fastapi import APIRouter

router = APIRouter(prefix="/client", tags=["client-update"])

# Aktuellste veroeffentlichte Desktop-Client-Version.
# Solange wir nicht produktiv sind: 0.x-Versionierung.
# Bei jedem Release manuell erhoehen — download_url bleibt stabil (kein
# Versions-Suffix im Dateinamen), nur die Datei dahinter wird ersetzt.
LATEST_VERSION = "0.44.0"
DOWNLOAD_URL = "http://212.227.180.66:8000/client/download/LexWolf-Setup.exe"
RELEASE_NOTES = "Task #270 Fix: Mandanten-Dropdown Bugfix — weiteres Duplikat. Produktionscode-Pfad ApplyMandantFilter delegiert an testbaren MandantFilter-Service; xUnit-Regressionstests in LexWolf.Tests/MandantFilterTests.cs greifen direkt auf den echten Produktionscode. Dialog oeffnen + Dropdown klicken zeigt jetzt alle geladenen Mandanten. Client-Version: 1.2.3 UI-Stelle: Mandanten-Liste."


@router.get("/version")
async def get_latest_version():
    return {
        "version": LATEST_VERSION,
        "download_url": DOWNLOAD_URL,
        "notes": RELEASE_NOTES,
    }
