"""
Akzeptanztest für den SSE-Client (Task #76).
Testet den Import und die Grundfunktionalität des SseClient.
"""
import sys
from pathlib import Path

import pytest

# Füge Workspace-Verzeichnis zum Path hinzu
_workspace = Path(__file__).parent.parent
if str(_workspace) not in sys.path:
    sys.path.insert(0, str(_workspace))


def test_sse_client_importierbar():
    """Prüft ob der SseClient importiert werden kann."""
    from desktop.sse_client import SseClient, parse_sse_block
    assert SseClient is not None
    assert parse_sse_block is not None


def test_sse_client_klasse():
    """Prüft ob die SseClient-Klasse die erforderlichen Methoden hat."""
    from desktop.sse_client import SseClient
    client = SseClient("http://localhost:8000")
    
    # Prüfe Methoden
    assert hasattr(client, 'on_event')
    assert hasattr(client, 'on_error')
    assert hasattr(client, 'connect')
    assert hasattr(client, 'close')
    
    # Prüfe Attribute
    assert hasattr(client, 'verbindung_getrennt')
    assert isinstance(client.verbindung_getrennt, bool)


def test_sse_block_parser():
    """Prüft ob der SSE-Block-Parser korrekt funktioniert."""
    from desktop.sse_client import parse_sse_block
    
    # Gültiger SSE-Block
    result = parse_sse_block('data: {"step": "suche_start", "text": "Suche nach: test"}')
    assert result is not None
    assert result["step"] == "suche_start"
    assert result["text"] == "Suche nach: test"
    
    # Ungültiger SSE-Block (kein data: prefix)
    result = parse_sse_block('{"step": "test", "text": "test"}')
    assert result is None
    
    # Ungültiger JSON
    result = parse_sse_block('data: {invalid json}')
    assert result is None


def test_sse_client_on_event():
    """Prüft ob on_event Callback registriert wird."""
    from desktop.sse_client import SseClient
    client = SseClient("http://localhost:8000")
    
    received = []
    def handler(step, text):
        received.append((step, text))
    
    client.on_event(handler)
    assert client._on_event is handler


def test_sse_client_on_error():
    """Prüft ob on_error Callback registriert wird."""
    from desktop.sse_client import SseClient
    client = SseClient("http://localhost:8000")
    
    received = []
    def handler(ex):
        received.append(ex)
    
    client.on_error(handler)
    assert client._on_error is handler


def test_sse_client_verbindung_getrennt():
    """Prüft ob verbindung_getrennt initially False ist."""
    from desktop.sse_client import SseClient
    client = SseClient("http://localhost:8000")
    assert client.verbindung_getrennt is False
