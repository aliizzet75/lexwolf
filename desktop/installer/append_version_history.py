#!/usr/bin/env python3
# Haengt einen (version, notes)-Eintrag an VERSION_HISTORY in client_update.py an.
# Wird ausschliesslich von release.sh aufgerufen, damit Backend-Version und
# Build-Version nie wieder auseinanderlaufen (siehe Git-Historie vor diesem Fix:
# client_update.py LATEST_VERSION wurde ~30x erhoeht, LexWolf.csproj <Version> nur 3x).
import sys
from pathlib import Path

version, notes, repo_root = sys.argv[1], sys.argv[2], sys.argv[3]
path = Path(repo_root) / "backend" / "api" / "client_update.py"
text = path.read_text()

anchor = "]\n\nLATEST_VERSION = VERSION_HISTORY[-1][0]"
if anchor not in text:
    raise SystemExit(f"Anker nicht gefunden in {path} - Datei manuell pruefen.")

notes_escaped = notes.replace('"', '\\"')
entry = f'    (\n        "{version}",\n        "{notes_escaped}",\n    ),\n'
path.write_text(text.replace(anchor, entry + anchor, 1))
print(f"VERSION_HISTORY: {version} angehaengt.")
