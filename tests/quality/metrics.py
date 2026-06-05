from typing import List, Dict, Any


def halluzinations_rate(ergebnisse: List[Dict[str, Any]]) -> float:
    """Anteil unverified Aussagen (verifiziert=False) an allen Ergebnissen."""
    if not ergebnisse:
        return 0.0
    unverified = sum(1 for e in ergebnisse if not e.get("verifiziert", True))
    return float(unverified / len(ergebnisse))


def quellen_genauigkeit(ergebnisse: List[Dict[str, Any]]) -> float:
    """Anteil korrekt zitierter §§ relativ zu den erwarteten §§."""
    if not ergebnisse:
        return 0.0
    total_erwartet = 0
    total_korrekt = 0
    for e in ergebnisse:
        erwartet = set(e.get("erwartete_paragraphen", []))
        korrekt = set(e.get("korrekte_quellen", []))
        total_erwartet += len(erwartet)
        total_korrekt += len(korrekt & erwartet)
    if total_erwartet == 0:
        return 0.0
    return float(total_korrekt / total_erwartet)


def vollstaendigkeits_score(ergebnisse: List[Dict[str, Any]]) -> float:
    """Durchschnittlicher Anteil gefundener erwarteter §§ je Ergebnis."""
    if not ergebnisse:
        return 0.0
    scores = []
    for e in ergebnisse:
        erwartet = set(e.get("erwartete_paragraphen", []))
        gefunden = set(e.get("korrekte_quellen", []))
        if not erwartet:
            scores.append(1.0)
        else:
            scores.append(len(erwartet & gefunden) / len(erwartet))
    return float(sum(scores) / len(scores))


def antwort_verweigerungsrate(ergebnisse: List[Dict[str, Any]]) -> float:
    """Anteil der Ergebnisse mit verweigerter Antwort."""
    if not ergebnisse:
        return 0.0
    verweigert = sum(1 for e in ergebnisse if e.get("verweigert", False))
    return float(verweigert / len(ergebnisse))
