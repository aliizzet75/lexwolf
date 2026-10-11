import os

_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
_DESKTOP = os.path.join(_ROOT, "desktop")


def _desktop_files():
    exts = (".xaml", ".cs")
    for dirpath, dirnames, filenames in os.walk(_DESKTOP):
        dirnames[:] = [d for d in dirnames if d not in ("obj", "bin")]
        for name in filenames:
            if name.endswith(exts):
                yield os.path.join(dirpath, name)


def test_testbereich_ui_im_desktop_client_vorhanden():
    """DoD: Bug ist im Desktop-Client sichtbar/nutzbar (T#243, Testbereich)."""
    treffer = [f for f in _desktop_files() if "testbereich" in open(f, encoding="utf-8", errors="ignore").read().lower()]
    assert treffer, "Kein 'Testbereich'-Bezug in desktop/ gefunden - Bugfix/Feature noch nicht umgesetzt"
