from dataclasses import dataclass, asdict
from typing import List, Dict, Any


@dataclass
class Vorschlag:
    aktion: str
    mandant: str
    dringlichkeit: str


class VorschlagEngine:
    REGELN = [
        ({"Gehaltsaenderung", "Scheidung"}, lambda m: Vorschlag("Unterhaltsanpassung", m, "hoch")),
        ({"Scheidung"}, lambda m: Vorschlag("Scheidungsfolgenvereinbarung", m, "hoch")),
        ({"Gehaltsaenderung"}, lambda m: Vorschlag("Unterhaltsueberprüfung", m, "mittel")),
        ({"Fristablauf"}, lambda m: Vorschlag("Fristenverlaengerung pruefen", m, "hoch")),
    ]

    def generiere_vorschlaege(self, ereignisse: List[Dict[str, Any]]) -> List[Dict]:
        if not ereignisse:
            return []
        by_mandant: Dict[str, set] = {}
        for e in ereignisse:
            m = e.get("mandant", "Unbekannt")
            by_mandant.setdefault(m, set()).add(e.get("typ", ""))
        vorschlaege: List[Dict] = []
        for mandant, typen in by_mandant.items():
            matched = False
            for regel_typen, factory in self.REGELN:
                if regel_typen.issubset(typen):
                    v = factory(mandant)
                    vorschlaege.append(asdict(v))
                    matched = True
                    break
            if not matched:
                v = self._ki_vorschlag(mandant, ereignisse)
                if v:
                    vorschlaege.append(asdict(v))
        return vorschlaege

    def _ki_vorschlag(self, mandant: str, ereignisse: List[Dict[str, Any]]):
        try:
            import socket
            s = socket.socket()
            r = s.connect_ex(("localhost", 11434))
            s.close()
            if r != 0:
                raise ConnectionRefusedError()
            import urllib.request, json as _json
            typen = [e.get("typ", "") for e in ereignisse if e.get("mandant") == mandant]
            prompt = (f"Assistent. Ereignisse: {typen}. Aktionsname: ")
            payload = _json.dumps({"model": "llama3", "prompt": prompt, "stream": False}).encode()
            req = urllib.request.Request("http://localhost:11434/api/generate", data=payload, headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=10) as resp:
                data = _json.loads(resp.read())
                aktion = data.get("response", "").strip().splitlines()[0]
                if aktion:
                    return Vorschlag(aktion, mandant, "mittel")
        except Exception:
            pass
        return Vorschlag("Rechtliche Pruefung erforderlich", mandant, "niedrig")
