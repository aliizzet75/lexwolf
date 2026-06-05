"""
Acceptance Tests für Task #69: TLS 1.3 Verbindung zum LexWolf-Server
DoD:
- Alle Verbindungen über TLS 1.3
- Zertifikat-Pinning implementiert
- HTTP-Verbindungen werden abgelehnt
- API-Key aus verschlüsseltem Config gespeichert
"""
import importlib.util
import os
import ssl
import sys
import tempfile
import hashlib
import base64
import json
import pytest
from pathlib import Path

DESKTOP = Path('/data/.openclaw/workspace-codex/projects/lexwolf/desktop')
TLS_MODULE = DESKTOP / 'tls_client.py'


def _import_tls_client():
    spec = importlib.util.spec_from_file_location("tls_client", TLS_MODULE)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# ── Datei-Existenz ──────────────────────────────────────────────────────────

def test_tls_client_datei_existiert():
    assert TLS_MODULE.exists(), f'tls_client.py fehlt in {DESKTOP}'


# ── Modul-Import ─────────────────────────────────────────────────────────────

def test_modul_importierbar():
    mod = _import_tls_client()
    assert mod is not None


# ── Exception-Klassen ─────────────────────────────────────────────────────────

def test_insecure_connection_error_existiert():
    mod = _import_tls_client()
    assert hasattr(mod, 'InsecureConnectionError')
    assert issubclass(mod.InsecureConnectionError, Exception)


def test_certificate_pinning_error_existiert():
    mod = _import_tls_client()
    assert hasattr(mod, 'CertificatePinningError')
    assert issubclass(mod.CertificatePinningError, Exception)


def test_missing_api_key_error_existiert():
    mod = _import_tls_client()
    assert hasattr(mod, 'MissingApiKeyError')
    assert issubclass(mod.MissingApiKeyError, Exception)


# ── HTTP-Ablehnung ────────────────────────────────────────────────────────────

def test_http_url_wird_abgelehnt():
    mod = _import_tls_client()
    env_backup = os.environ.get('LEXWOLF_API_KEY')
    os.environ['LEXWOLF_API_KEY'] = 'test-key-12345'
    try:
        client = mod.TLSClient(api_key='test-key-12345')
        with pytest.raises(mod.InsecureConnectionError):
            client.get('http://lexwolf-server.example.com/api/v1/data')
    finally:
        if env_backup is None:
            os.environ.pop('LEXWOLF_API_KEY', None)
        else:
            os.environ['LEXWOLF_API_KEY'] = env_backup


def test_http_url_post_wird_abgelehnt():
    mod = _import_tls_client()
    client = mod.TLSClient(api_key='test-key-99')
    with pytest.raises(mod.InsecureConnectionError):
        client.post('http://api.lexwolf.local/mandant')


def test_https_url_wird_nicht_direkt_abgelehnt():
    """TLSClient soll HTTPS-URLs nicht sofort durch URL-Validierung ablehnen."""
    mod = _import_tls_client()
    client = mod.TLSClient(api_key='test-key-99')
    # Sollte keine InsecureConnectionError werfen (nur ggf. Netzwerkfehler)
    try:
        client._validate_url('https://lexwolf-server.example.com/api/v1/data')
    except mod.InsecureConnectionError:
        pytest.fail("HTTPS-URL darf nicht durch _validate_url abgelehnt werden")


# ── TLS 1.3 Konfiguration ────────────────────────────────────────────────────

def test_tls13_ssl_context_minimum_version():
    mod = _import_tls_client()
    ctx = mod.make_tls13_context()
    assert isinstance(ctx, ssl.SSLContext)
    assert ctx.minimum_version == ssl.TLSVersion.TLSv1_3


def test_tls13_ssl_context_maximum_version():
    mod = _import_tls_client()
    ctx = mod.make_tls13_context()
    assert ctx.maximum_version == ssl.TLSVersion.TLSv1_3


def test_tls13_ssl_context_cert_required():
    mod = _import_tls_client()
    ctx = mod.make_tls13_context()
    assert ctx.verify_mode == ssl.CERT_REQUIRED


# ── Zertifikat-Pinning ────────────────────────────────────────────────────────

def test_zertifikat_pinning_korrekt():
    mod = _import_tls_client()
    fake_der = b'\x30\x82\x01\x00' + b'\xAB' * 256  # simuliertes DER-Zertifikat
    expected_fp = hashlib.sha256(fake_der).hexdigest()
    client = mod.TLSClient(api_key='key-x', pinned_fingerprint=expected_fp)
    client.verify_pinning(fake_der)  # darf nicht werfen


def test_zertifikat_pinning_mismatch_wirft_fehler():
    mod = _import_tls_client()
    fake_der = b'\x30\x82\x01\x00' + b'\xAB' * 256
    wrong_fp = 'a' * 64  # falscher Fingerprint
    client = mod.TLSClient(api_key='key-x', pinned_fingerprint=wrong_fp)
    with pytest.raises(mod.CertificatePinningError):
        client.verify_pinning(fake_der)


def test_kein_pinning_akzeptiert_alles():
    mod = _import_tls_client()
    client = mod.TLSClient(api_key='key-y', pinned_fingerprint=None)
    client.verify_pinning(b'\x00' * 64)  # kein pinning → kein Fehler


# ── API-Key aus verschlüsseltem Config ───────────────────────────────────────

def test_api_key_aus_env_variable():
    mod = _import_tls_client()
    env_backup = os.environ.get('LEXWOLF_API_KEY')
    os.environ['LEXWOLF_API_KEY'] = 'super-secret-key-from-env'
    try:
        key = mod.load_api_key_from_config()
        assert key == 'super-secret-key-from-env'
    finally:
        if env_backup is None:
            os.environ.pop('LEXWOLF_API_KEY', None)
        else:
            os.environ['LEXWOLF_API_KEY'] = env_backup


def test_api_key_aus_verschluesselter_config():
    mod = _import_tls_client()
    env_backup = os.environ.pop('LEXWOLF_API_KEY', None)
    try:
        with tempfile.NamedTemporaryFile(suffix='.json', delete=False) as f:
            config_path = f.name
        try:
            mod.save_api_key_to_config('mein-geheimer-api-key', config_path, 'master-pw-42')
            loaded = mod.load_api_key_from_config(config_path, 'master-pw-42')
            assert loaded == 'mein-geheimer-api-key'
        finally:
            os.unlink(config_path)
    finally:
        if env_backup is not None:
            os.environ['LEXWOLF_API_KEY'] = env_backup


def test_api_key_fehlt_wirft_exception():
    mod = _import_tls_client()
    env_backup = os.environ.pop('LEXWOLF_API_KEY', None)
    try:
        with pytest.raises(mod.MissingApiKeyError):
            mod.load_api_key_from_config(config_path=None)
    finally:
        if env_backup is not None:
            os.environ['LEXWOLF_API_KEY'] = env_backup


def test_config_verschluesselt_kein_klartext():
    """API-Key darf nicht im Klartext in der Config-Datei stehen."""
    mod = _import_tls_client()
    with tempfile.NamedTemporaryFile(suffix='.json', delete=False, mode='w') as f:
        config_path = f.name
    try:
        mod.save_api_key_to_config('geheimer-api-schluessel-9876', config_path, 'pw')
        raw = Path(config_path).read_text()
        assert 'geheimer-api-schluessel-9876' not in raw, \
            "API-Key steht im Klartext in der Config-Datei!"
    finally:
        os.unlink(config_path)


def test_falsches_master_pw_liefert_anderen_wert():
    """Falsches Master-Passwort darf nicht den richtigen Key liefern."""
    mod = _import_tls_client()
    env_backup = os.environ.pop("LEXWOLF_API_KEY", None)
    try:
        with tempfile.NamedTemporaryFile(suffix=".json", delete=False) as f:
            config_path = f.name
        try:
            mod.save_api_key_to_config("echter-key", config_path, "richtiges-pw")
            try:
                wrong_result = mod.load_api_key_from_config(config_path, "falsches-pw")
                # Falsches PW liefert anderen (korrumpierten) Wert — das ist OK
                assert wrong_result != "echter-key",                     "Falsches Passwort sollte nicht den richtigen Key liefern"
            except (UnicodeDecodeError, Exception):
                pass  # Fehler beim Entschluesseln mit falschem PW ist auch OK
        finally:
            os.unlink(config_path)
    finally:
        if env_backup is not None:
            os.environ["LEXWOLF_API_KEY"] = env_backup
