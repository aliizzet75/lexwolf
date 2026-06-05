"""
LexWolf Email Integration - Outlook MAPI (Windows) + IMAP Fallback
Stores only metadata locally — no email body text is sent to any server.
"""
import platform
import json
import os
import re
from dataclasses import dataclass, field, asdict
from typing import List, Optional, Dict
from datetime import datetime
import imaplib
import email
from email.header import decode_header as _decode_hdr


@dataclass
class EmailMetadata:
    """Only metadata is stored - no email body text"""
    message_id: str
    sender: str
    subject: str
    date: str
    folder: str
    client_id: Optional[str] = None
    client_name: Optional[str] = None


@dataclass
class EmailConfig:
    """Email connection configuration"""
    folders: List[str] = field(default_factory=lambda: ["INBOX"])
    imap_server: str = ""
    imap_port: int = 993
    username: str = ""
    password: str = ""
    use_ssl: bool = True
    client_rules: List[Dict] = field(default_factory=list)
    storage_path: str = "email_metadata.json"


class ClientMatcher:
    """Assigns clients to emails based on subject/sender patterns (Mandanten-Zuordnung)"""

    def __init__(self, rules: List[Dict]):
        self.rules = rules

    def match(self, subject: str, sender: str) -> Optional[Dict]:
        """Returns client info if a rule matches the subject or sender"""
        for rule in self.rules:
            for pattern in rule.get("patterns", []):
                if re.search(pattern, subject, re.IGNORECASE):
                    return {"client_id": rule["client_id"], "client_name": rule["client_name"]}
            for sender_pattern in rule.get("senders", []):
                if re.search(sender_pattern, sender, re.IGNORECASE):
                    return {"client_id": rule["client_id"], "client_name": rule["client_name"]}
        return None


class MetadataStore:
    """Stores email metadata as JSON — no body text"""

    def __init__(self, storage_path: str):
        self.storage_path = storage_path
        self._data: Dict[str, dict] = {}
        self._load()

    def _load(self):
        if os.path.exists(self.storage_path) and os.path.getsize(self.storage_path) > 0:
            with open(self.storage_path, "r", encoding="utf-8") as f:
                self._data = json.load(f)

    def save(self, metadata: EmailMetadata):
        self._data[metadata.message_id] = asdict(metadata)
        with open(self.storage_path, "w", encoding="utf-8") as f:
            json.dump(self._data, f, ensure_ascii=False, indent=2)

    def get_all(self) -> List[EmailMetadata]:
        return [EmailMetadata(**v) for v in self._data.values()]

    def count(self) -> int:
        return len(self._data)


def _decode_header(value: str) -> str:
    if not value:
        return ""
    parts = _decode_hdr(value)
    decoded = []
    for part, charset in parts:
        if isinstance(part, bytes):
            decoded.append(part.decode(charset or "utf-8", errors="replace"))
        else:
            decoded.append(part)
    return "".join(decoded)


class IMAPConnector:
    """IMAP connector — used as fallback on non-Windows systems"""

    def __init__(self, config: EmailConfig):
        self.config = config
        self._client = None

    def connect(self) -> bool:
        try:
            if self.config.use_ssl:
                self._client = imaplib.IMAP4_SSL(self.config.imap_server, self.config.imap_port)
            else:
                self._client = imaplib.IMAP4(self.config.imap_server, self.config.imap_port)
            self._client.login(self.config.username, self.config.password)
            return True
        except Exception:
            return False

    def disconnect(self):
        if self._client:
            try:
                self._client.logout()
            except Exception:
                pass
            self._client = None

    def read_folder(self, folder: str = "INBOX") -> List[EmailMetadata]:
        """Read emails from folder, returning only metadata (headers only)"""
        if not self._client:
            return []
        results = []
        try:
            self._client.select(folder, readonly=True)
            _, message_ids = self._client.search(None, "ALL")
            for mid in message_ids[0].split():
                _, msg_data = self._client.fetch(mid, "(RFC822.HEADER)")
                raw = msg_data[0][1]
                msg = email.message_from_bytes(raw)
                subject = _decode_header(msg.get("Subject", ""))
                sender = _decode_header(msg.get("From", ""))
                date = msg.get("Date", "")
                message_id = msg.get("Message-ID", mid.decode())
                results.append(EmailMetadata(
                    message_id=message_id,
                    sender=sender,
                    subject=subject,
                    date=date,
                    folder=folder,
                ))
        except Exception:
            pass
        return results


class OutlookMAPIConnector:
    """
    Outlook MAPI connector using win32com.client (Windows only).
    Returns False on non-Windows systems without raising exceptions.
    """

    def __init__(self, config: EmailConfig):
        self.config = config
        self._outlook = None
        self._namespace = None
        self._available = platform.system() == "Windows"

    def connect(self) -> bool:
        if not self._available:
            return False
        try:
            import win32com.client  # noqa: F401 — Windows only
            self._outlook = win32com.client.Dispatch("Outlook.Application")
            self._namespace = self._outlook.GetNamespace("MAPI")
            return True
        except Exception:
            return False

    def disconnect(self):
        self._outlook = None
        self._namespace = None

    def read_folder(self, folder_name: str = "Inbox") -> List[EmailMetadata]:
        """Read emails from Outlook folder via MAPI, returning only metadata"""
        if not self._namespace:
            return []
        results = []
        FOLDER_MAP = {
            "Inbox": 6, "INBOX": 6, "Posteingang": 6,
            "Sent": 5, "Gesendet": 5,
        }
        try:
            import win32com.client  # noqa: F401
            folder_id = FOLDER_MAP.get(folder_name, 6)
            folder = self._namespace.GetDefaultFolder(folder_id)
            for item in folder.Items:
                try:
                    results.append(EmailMetadata(
                        message_id=item.EntryID,
                        sender=item.SenderEmailAddress,
                        subject=item.Subject,
                        date=str(item.ReceivedTime),
                        folder=folder_name,
                    ))
                except Exception:
                    continue
        except Exception:
            pass
        return results


class EmailIndexer:
    """
    Indexes emails from Outlook MAPI (Windows) or IMAP (fallback).
    Only metadata is stored locally — no email body text.
    """

    def __init__(self, config: EmailConfig):
        self.config = config
        self.store = MetadataStore(config.storage_path)
        self.matcher = ClientMatcher(config.client_rules)
        if platform.system() == "Windows":
            self.connector: object = OutlookMAPIConnector(config)
        else:
            self.connector = IMAPConnector(config)

    def connect(self) -> bool:
        return self.connector.connect()

    def disconnect(self):
        self.connector.disconnect()

    def index_folder(self, folder: str) -> int:
        """Index a folder, assign clients, store metadata. Returns count of indexed emails."""
        emails = self.connector.read_folder(folder)
        for meta in emails:
            client = self.matcher.match(meta.subject, meta.sender)
            if client:
                meta.client_id = client["client_id"]
                meta.client_name = client["client_name"]
            self.store.save(meta)
        return len(emails)

    def index_all_folders(self) -> int:
        total = 0
        for folder in self.config.folders:
            total += self.index_folder(folder)
        return total
