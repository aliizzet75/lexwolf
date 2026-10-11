#!/usr/bin/env bash
# Einziger Weg, einen LexWolf-Desktop-Release zu bauen. Haelt LexWolf.csproj
# <Version> (eingebettet in die .exe, das vergleicht UpdateChecker.CurrentVersion),
# backend/api/client_update.py VERSION_HISTORY und den tatsaechlich ausgelieferten
# Installer synchron — vorher wurden diese drei Stellen separat manuell gepflegt
# und liefen auseinander (.exe blieb bei 0.6.3 haengen, Backend meldete 0.46.0 als
# "neueste Version" -> Update-Dialog kam bei jedem Neustart erneut).
set -euo pipefail

VERSION="${1:?Usage: release.sh <version z.B. 0.47.0> <notes>}"
NOTES="${2:?Usage: release.sh <version z.B. 0.47.0> <notes>}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PUBLISH_DIR="$ROOT/desktop/bin/publish-release"
OUT_DIR="$ROOT/backend/static/client-releases"

sed -i "s#<Version>.*</Version>#<Version>${VERSION}</Version>#" "$ROOT/desktop/LexWolf.csproj"
python3 "$ROOT/desktop/installer/append_version_history.py" "$VERSION" "$NOTES" "$ROOT"

rm -rf "$PUBLISH_DIR"
dotnet publish "$ROOT/desktop/LexWolf.csproj" -c Release -r win-x64 --self-contained true \
  -p:EnableWindowsTargeting=true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o "$PUBLISH_DIR"

mkdir -p "$OUT_DIR"
docker run --rm \
  -v "$PUBLISH_DIR:/build/lexwolf-desktop:ro" \
  -v "$ROOT/desktop/installer:/nsis:ro" \
  -v "$OUT_DIR:/out" \
  debian:bookworm-slim \
  bash -c "apt-get update -qq && apt-get install -y -qq nsis && makensis -DVERSION=${VERSION} /nsis/lexwolf-setup.nsi"

echo "Release ${VERSION} gebaut: $OUT_DIR/LexWolf-Setup.exe"
