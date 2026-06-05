import re
from typing import List, Dict, Any


class EreignisErkenner:
    REGELN = [
        (r"gehalt|lohn|erhoeh|erhoht|gehaltsaen", "Gehaltsaenderung"),
        (r"frist|ablauf|deadline|laeuft ab|laeuft aus|fristende", "Fristablauf"),
        (r"scheidung|scheiden|trennung|getrennt", "Scheidung"),
        (r"neuer fall|neue akte|mandat erteilt|neues mandat|mandant neu", "Neuer_Fall"),
    ]

    def erkenne(self, text: str, mandant: str) -> List[Dict[str, Any]]:
        if not text or not text.strip():
            return []

        gefunden: List[Dict[str, Any]] = []
        text_lower = text.lower()

        for pattern, typ in self.REGELN:
            if re.search(pattern, text_lower):
                gefunden.append({"typ": typ, "mandant": mandant, "details": text.strip()})

        if not gefunden:
            gefunden.append({"typ": "Unbekannt", "mandant": mandant, "details": text.strip()})

        return gefunden
