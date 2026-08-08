from fastapi import APIRouter
from fastapi.responses import HTMLResponse

from api.client_update import LATEST_VERSION as CLIENT_VERSION, DOWNLOAD_URL as CLIENT_DOWNLOAD_URL
from api.tool_update import ANONYMISIERER_VERSION, ANONYMISIERER_DOWNLOAD_URL

router = APIRouter(tags=["downloads"])

PAGE_TEMPLATE = """<!DOCTYPE html>
<html lang="de">
<head>
<meta charset="utf-8">
<title>LexWolf – Downloads</title>
<style>
  body {{ font-family: -apple-system, Segoe UI, Arial, sans-serif; background: #0d1117; color: #c9d1d9; max-width: 640px; margin: 60px auto; padding: 0 20px; }}
  h1 {{ color: #f0f6fc; }}
  .card {{ background: #161b22; border: 1px solid #30363d; border-radius: 8px; padding: 24px; margin-bottom: 20px; }}
  .card h2 {{ margin-top: 0; color: #f0f6fc; }}
  .version {{ color: #8b949e; font-size: 0.9em; }}
  a.button {{ display: inline-block; margin-top: 12px; padding: 10px 20px; background: #238636; color: #fff; text-decoration: none; border-radius: 6px; font-weight: 600; }}
  a.button:hover {{ background: #2ea043; }}
</style>
</head>
<body>
<h1>LexWolf – Downloads</h1>
<div class="card">
  <h2>LexWolf Client</h2>
  <p class="version">Version {client_version}</p>
  <a class="button" href="{client_url}">Installer herunterladen</a>
</div>
<div class="card">
  <h2>Anonymisierer</h2>
  <p class="version">Version {anonymisierer_version}</p>
  <a class="button" href="{anonymisierer_url}">Installer herunterladen</a>
</div>
</body>
</html>
"""


@router.get("/downloads", response_class=HTMLResponse)
async def downloads_page():
    return PAGE_TEMPLATE.format(
        client_version=CLIENT_VERSION,
        client_url=CLIENT_DOWNLOAD_URL,
        anonymisierer_version=ANONYMISIERER_VERSION,
        anonymisierer_url=ANONYMISIERER_DOWNLOAD_URL,
    )
