import pytest, sys
from pathlib import Path

DESKTOP = Path("/data/.openclaw/workspace-codex/projects/lexwolf/desktop")
sys.path.insert(0, str(DESKTOP))

def ollama_ok():
    import socket; s = socket.socket(); r = s.connect_ex(("localhost", 11434)); s.close(); return r == 0

def test_modul_existiert():
    assert (DESKTOP / "vorschlag_engine.py").exists(), "vorschlag_engine.py fehlt im desktop-Ordner"

def test_regelbasiert_gehaltsaenderung_scheidung():
    from vorschlag_engine import VorschlagEngine
    engine = VorschlagEngine()
    ereignisse = [{"typ": "Gehaltsaenderung", "mandant": "Müller"}, {"typ": "Scheidung", "mandant": "Müller"}]
    vs = engine.generiere_vorschlaege(ereignisse)
    assert len(vs) >= 1
    assert vs[0]["aktion"] == "Unterhaltsanpassung"
    assert vs[0]["mandant"] == "Müller"
    assert vs[0]["dringlichkeit"] == "hoch"

def test_dringlichkeit_und_struktur():
    from vorschlag_engine import VorschlagEngine
    vs = VorschlagEngine().generiere_vorschlaege([{"typ": "Fristablauf", "mandant": "Schmidt"}])
    assert vs and vs[0]["dringlichkeit"] in ("hoch", "mittel", "niedrig")
    assert all(k in vs[0] for k in ("aktion", "mandant", "dringlichkeit"))

@pytest.mark.skipif(not ollama_ok(), reason="Ollama nicht erreichbar auf port 11434")
def test_ki_vorschlag_unbekannte_kombination():
    from vorschlag_engine import VorschlagEngine
    vs = VorschlagEngine().generiere_vorschlaege([{"typ": "Unbekannt", "mandant": "Bauer", "details": "Neue Situation"}])
    assert vs and vs[0]["aktion"] != ""
