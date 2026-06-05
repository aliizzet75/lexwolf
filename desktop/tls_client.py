"""
TLS 1.3 Client für LexWolf-Server.
Erzwingt TLS 1.3, Zertifikat-Pinning und rejects HTTP-Verbindungen.
API-Key wird aus verschlüsselter Config-Datei oder Umgebungsvariable geladen.
"""
import ssl
import hashlib
import base64
import json
import os
from typing import Optional


class InsecureConnectionError(Exception):
    """HTTP-Verbindungen (kein TLS) sind verboten."""


class CertificatePinningError(Exception):
    """Server-Zertifikat stimmt nicht mit gepinntem Fingerprint überein."""


class MissingApiKeyError(Exception):
    """Kein API-Key in Umgebungsvariable oder Config-Datei gefunden."""


def make_tls13_context() -> ssl.SSLContext:
    """Erstellt einen SSL-Context, der ausschließlich TLS 1.3 erlaubt."""
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    ctx.minimum_version = ssl.TLSVersion.TLSv1_3
    ctx.maximum_version = ssl.TLSVersion.TLSv1_3
    ctx.verify_mode = ssl.CERT_REQUIRED
    ctx.check_hostname = True
    return ctx


def _xor_cipher(data: bytes, key_bytes: bytes) -> bytes:
    """XOR-Cipher mit wiederholtem Schlüssel."""
    key_repeated = (key_bytes * (len(data) // len(key_bytes) + 1))[:len(data)]
    return bytes(b ^ k for b, k in zip(data, key_repeated))


def save_api_key_to_config(api_key: str, config_path: str, master_password: str) -> None:
    """Verschlüsselt API-Key mit PBKDF2+XOR und speichert ihn in config_path."""
    pw_bytes = master_password.encode("utf-8")
    key_bytes = hashlib.pbkdf2_hmac("sha256", pw_bytes, b"lexwolf_salt_v1", 100_000, dklen=32)
    encrypted = _xor_cipher(api_key.encode("utf-8"), key_bytes)
    data = {"api_key": base64.b64encode(encrypted).decode("ascii"), "version": 1}
    with open(config_path, "w", encoding="utf-8") as f:
        json.dump(data, f)


def load_api_key_from_config(
    config_path: Optional[str] = None,
    master_password: Optional[str] = None,
) -> str:
    """
    Lädt API-Key aus Umgebungsvariable LEXWOLF_API_KEY (Vorrang)
    oder aus verschlüsselter Config-Datei.
    """
    env_key = os.environ.get("LEXWOLF_API_KEY")
    if env_key:
        return env_key

    if config_path and os.path.exists(config_path):
        with open(config_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        encrypted = base64.b64decode(data["api_key"])
        if not master_password:
            raise MissingApiKeyError(
                "master_password erforderlich zum Entschlüsseln der Config-Datei."
            )
        pw_bytes = master_password.encode("utf-8")
        key_bytes = hashlib.pbkdf2_hmac("sha256", pw_bytes, b"lexwolf_salt_v1", 100_000, dklen=32)
        decrypted = _xor_cipher(encrypted, key_bytes)
        return decrypted.decode("utf-8")

    raise MissingApiKeyError(
        "Kein API-Key in LEXWOLF_API_KEY Umgebungsvariable oder Config-Datei gefunden."
    )


def get_cert_fingerprint_sha256(der_cert: bytes) -> str:
    """Gibt SHA-256-Fingerprint eines DER-kodierten Zertifikats zurück."""
    return hashlib.sha256(der_cert).hexdigest()


class TLSClient:
    """
    HTTPS-Client der ausschließlich TLS 1.3 verwendet.
    Unterstützt Zertifikat-Pinning und API-Key-Authentifizierung.
    HTTP-Verbindungen werden abgelehnt.
    """

    def __init__(self, api_key: str, pinned_fingerprint: Optional[str] = None):
        if not api_key:
            raise MissingApiKeyError("api_key darf nicht leer sein.")
        self.api_key = api_key
        self.pinned_fingerprint = pinned_fingerprint
        self._ssl_context = make_tls13_context()

    def _validate_url(self, url: str) -> None:
        if not url.startswith("https://"):
            raise InsecureConnectionError(
                f"HTTP-Verbindungen sind nicht erlaubt. Verwende HTTPS: {url}"
            )

    def _default_headers(self) -> dict:
        return {"X-API-Key": self.api_key, "Content-Type": "application/json"}

    def get(self, url: str, **kwargs):
        self._validate_url(url)
        import httpx
        headers = {**self._default_headers(), **kwargs.pop("headers", {})}
        with httpx.Client(verify=self._ssl_context, headers=headers) as client:
            return client.get(url, **kwargs)

    def post(self, url: str, **kwargs):
        self._validate_url(url)
        import httpx
        headers = {**self._default_headers(), **kwargs.pop("headers", {})}
        with httpx.Client(verify=self._ssl_context, headers=headers) as client:
            return client.post(url, **kwargs)

    def verify_pinning(self, der_cert: bytes) -> None:
        """
        Prüft ob das Zertifikat dem gepinnten Fingerprint entspricht.
        Raises CertificatePinningError bei Mismatch.
        """
        if self.pinned_fingerprint is None:
            return
        actual = get_cert_fingerprint_sha256(der_cert)
        if actual != self.pinned_fingerprint.lower():
            raise CertificatePinningError(
                f"Zertifikat-Fingerprint stimmt nicht überein.\n"
                f"Erwartet: {self.pinned_fingerprint}\n"
                f"Erhalten:  {actual}"
            )
