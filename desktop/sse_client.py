"""
SSE Client für LexWolf Desktop.
Verbindet sich mit dem SSE-Endpunkt /api/search/stream und empfängt
Echtzeit-Updates des Denkprozesses (ReAct-Schritte).

Klasse: SseClient
Methoden:
  - on_event(callback): Event-Handler registrieren
  - on_error(callback): Error-Handler registrieren
  - connect(query, timeout): SSE-Verbindung herstellen
  - verbindung_getrennt: Status-Flag
"""
import json
import urllib.request
import urllib.error
from typing import Callable, Optional, Dict, Any


class SseClient:
    """
    SSE-Client für Live-Streaming von ReAct-Schritten vom Server.
    
    Usage:
        client = SseClient("http://localhost:8000")
        client.on_event(lambda step, text: print(f"{step}: {text}"))
        client.connect("Kündigungsschutz klagen", timeout=30)
    """
    
    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip('/')
        self._on_event: Optional[Callable[[str, str], None]] = None
        self._on_error: Optional[Callable[[Exception], None]] = None
        self.verbindung_getrennt: bool = False
        self._running: bool = False
    
    def on_event(self, callback: Callable[[str, str], None]) -> None:
        """Registriert einen Event-Handler für SSE-Daten."""
        self._on_event = callback
    
    def on_error(self, callback: Callable[[Exception], None]) -> None:
        """Registriert einen Error-Handler für Verbindungsfehler."""
        self._on_error = callback
    
    def connect(self, query: str, timeout: int = 30) -> None:
        """
        Stellt eine SSE-Verbindung zum Server her.
        
        Args:
            query: Der Suchquery
            timeout: Timeout in Sekunden
        """
        self._running = True
        self.verbindung_getrennt = False
        
        url = f"{self.base_url}/api/search/stream?q={query}"
        
        try:
            req = urllib.request.Request(url)
            req.add_header('Accept', 'text/event-stream')
            
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                if resp.status != 200:
                    raise Exception(f"SSE-Verbindung fehlgeschlagen: {resp.status}")
                
                for line in resp:
                    line = line.decode('utf-8').strip()
                    if line.startswith("data: "):
                        data = line[6:]
                        try:
                            event = json.loads(data)
                            step = event.get("step", "")
                            text = event.get("text", "")
                            if self._on_event:
                                self._on_event(step, text)
                        except json.JSONDecodeError:
                            continue
                
                self.verbindung_getrennt = False
                
        except urllib.error.URLError as e:
            self.verbindung_getrennt = True
            if self._on_error:
                self._on_error(e)
        except urllib.error.HTTPError as e:
            self.verbindung_getrennt = True
            if self._on_error:
                self._on_error(e)
        except TimeoutError as e:
            self.verbindung_getrennt = True
            if self._on_error:
                self._on_error(e)
        finally:
            self._running = False
    
    def close(self) -> None:
        """Schließt die Verbindung."""
        self._running = False
        self.verbindung_getrennt = True


def parse_sse_block(block: str) -> Optional[Dict[str, Any]]:
    """
    Parsed einen SSE-Block in ein Dictionary.
    
    Format: "data: {\"step\": \"...\", \"text\": \"...\"}"
    
    Returns:
        Dictionary mit step und text, oder None bei Parse-Fehler
    """
    block = block.strip()
    if not block.startswith("data: "):
        return None
    
    try:
        data = block[6:]  # "data: " entfernen
        return json.loads(data)
    except json.JSONDecodeError:
        return None
