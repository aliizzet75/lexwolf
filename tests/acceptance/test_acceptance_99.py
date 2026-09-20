import os
from pathlib import Path

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
if os.path.isdir("/data/.openclaw/workspace-codex/projects/lexwolf"):
    _ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"

DOC_FILE = Path(_ROOT) / "docs" / "word_addin_concept.md"
MANIFEST_FILE = Path(_ROOT) / "backend" / "static" / "addin" / "manifest.xml"
INDEX_FILE = Path(_ROOT) / "backend" / "static" / "addin" / "index.html"


def test_konzept_dokument_existiert_und_hat_pflichtabschnitte():
    assert DOC_FILE.exists(), "docs/word_addin_concept.md fehlt"
    content = DOC_FILE.read_text(encoding="utf-8")
    assert "Office-JS" in content and "VSTO" in content, "Technologie-Vergleich fehlt"
    assert "Empfehlung" in content, "Technologie-Entscheidung mit Begründung fehlt"
    assert "Architektur" in content, "Architektur-Skizze fehlt"
    assert "Aufwand" in content, "Aufwand-Schätzung fehlt"


def test_proof_of_concept_dateien_vorhanden():
    assert MANIFEST_FILE.exists(), "backend/static/addin/manifest.xml (PoC-Manifest) fehlt"
    assert INDEX_FILE.exists(), "backend/static/addin/index.html (PoC-Taskpane) fehlt"
    manifest = MANIFEST_FILE.read_text(encoding="utf-8")
    assert "<OfficeApp" in manifest, "manifest.xml ist kein gueltiges Office-Add-In-Manifest"
    index_html = INDEX_FILE.read_text(encoding="utf-8")
    assert "office.js" in index_html.lower(), "index.html bindet Office.js nicht ein"
