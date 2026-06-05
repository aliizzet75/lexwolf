import pytest
import sys
import os
import platform
import json
import tempfile
from pathlib import Path

DESKTOP = Path("/data/.openclaw/workspace-codex/projects/lexwolf/desktop")
MODULE_PATH = DESKTOP / "email_integration.py"

sys.path.insert(0, str(DESKTOP))


def test_modul_existiert():
    assert MODULE_PATH.exists(), f"email_integration.py fehlt in {DESKTOP}"


def test_mapi_connector_vorhanden():
    src = MODULE_PATH.read_text()
    assert "OutlookMAPIConnector" in src, "OutlookMAPIConnector Klasse fehlt"
    assert "win32com.client" in src, "win32com.client Import fehlt"


def test_imap_fallback_vorhanden():
    src = MODULE_PATH.read_text()
    assert "IMAPConnector" in src, "IMAPConnector Fallback-Klasse fehlt"
    assert "imaplib" in src, "imaplib Import fehlt"


def test_nur_metadaten_gespeichert():
    src = MODULE_PATH.read_text()
    assert "EmailMetadata" in src, "EmailMetadata-Klasse fehlt"
    import ast
    tree = ast.parse(src)
    for node in ast.walk(tree):
        if isinstance(node, ast.ClassDef) and node.name == "EmailMetadata":
            for item in ast.walk(node):
                if isinstance(item, ast.AnnAssign) and isinstance(item.target, ast.Name):
                    fname = item.target.id
                    assert "body" not in fname.lower(), (
                        f"EmailMetadata enthält body-Feld '{fname}' — kein Volltext erlaubt!"
                    )


def test_mandanten_zuordnung_vorhanden():
    src = MODULE_PATH.read_text()
    assert "ClientMatcher" in src, "ClientMatcher Klasse fehlt"
    assert "subject" in src.lower(), "Betreff-Matching fehlt"
    assert "sender" in src.lower(), "Absender-Matching fehlt"


def test_konfigurierbare_ordner():
    src = MODULE_PATH.read_text()
    assert "folders" in src, "Konfigurierbare Ordner fehlen"


def test_email_indexer_importierbar():
    from email_integration import EmailIndexer, EmailConfig, EmailMetadata
    assert EmailIndexer is not None
    assert EmailConfig is not None
    assert EmailMetadata is not None


def test_email_config_instanziierbar():
    from email_integration import EmailConfig
    config = EmailConfig(folders=["INBOX"], storage_path="/tmp/test_72_cfg.json")
    assert config.folders == ["INBOX"]
    assert config.storage_path == "/tmp/test_72_cfg.json"


def test_metadata_kein_volltext():
    from email_integration import EmailMetadata
    meta = EmailMetadata(
        message_id="test-id-1",
        sender="mandant@example.com",
        subject="AW: Klage Müller ./. Schmidt",
        date="2026-01-01",
        folder="INBOX",
    )
    assert not hasattr(meta, "body"), "EmailMetadata hat body-Attribut — Volltext nicht erlaubt!"
    assert not hasattr(meta, "text"), "EmailMetadata hat text-Attribut — Volltext nicht erlaubt!"
    assert not hasattr(meta, "html_body"), "EmailMetadata hat html_body — Volltext nicht erlaubt!"


def test_mandanten_zuordnung_aus_betreff():
    from email_integration import ClientMatcher
    rules = [
        {"client_id": "M001", "client_name": "Müller GmbH",
         "patterns": ["Müller", "Mueller"], "senders": []}
    ]
    matcher = ClientMatcher(rules)
    result = matcher.match("AW: Klage Müller ./. Schmidt", "anwalt@kanzlei.de")
    assert result is not None, "Mandant aus Betreff nicht erkannt"
    assert result["client_id"] == "M001"


def test_mandanten_zuordnung_aus_absender():
    from email_integration import ClientMatcher
    rules = [
        {"client_id": "M002", "client_name": "Schmidt AG",
         "patterns": [], "senders": [".*@schmidt-ag\.de"]}
    ]
    matcher = ClientMatcher(rules)
    result = matcher.match("Rechnungsprüfung", "kontakt@schmidt-ag.de")
    assert result is not None, "Mandant aus Absender nicht erkannt"
    assert result["client_id"] == "M002"


def test_metadata_store_speichert_nur_metadaten():
    from email_integration import MetadataStore, EmailMetadata
    with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as f:
        path = f.name
    try:
        store = MetadataStore(path)
        meta = EmailMetadata(
            message_id="<test-123@example.com>",
            sender="mandant@example.com",
            subject="Besprechung Akte 2026/42",
            date="2026-05-01",
            folder="INBOX",
            client_id="M001",
            client_name="Testmandant",
        )
        store.save(meta)
        with open(path) as f:
            stored = json.load(f)
        entry = stored["<test-123@example.com>"]
        assert "body" not in entry, "body wurde gespeichert — Volltext nicht erlaubt!"
        assert entry["sender"] == "mandant@example.com"
        assert entry["subject"] == "Besprechung Akte 2026/42"
        assert entry["client_id"] == "M001"
    finally:
        os.unlink(path)


def test_imap_connector_auf_linux():
    from email_integration import IMAPConnector, EmailConfig
    config = EmailConfig(
        imap_server="imap.example.com",
        imap_port=993,
        username="test@example.com",
        password="password",
        folders=["INBOX"],
    )
    connector = IMAPConnector(config)
    result = connector.connect()
    assert result is False, "IMAPConnector.connect() muss False zurückgeben wenn Server nicht erreichbar"


def test_outlook_mapi_graceful_fallback():
    from email_integration import OutlookMAPIConnector, EmailConfig
    config = EmailConfig(folders=["INBOX"])
    connector = OutlookMAPIConnector(config)
    if platform.system() != "Windows":
        result = connector.connect()
        assert result is False, "OutlookMAPIConnector.connect() muss False auf Nicht-Windows zurückgeben"
