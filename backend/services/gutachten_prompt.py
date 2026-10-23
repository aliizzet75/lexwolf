"""
Gutachtenstil-Prompt-Schema für die Anspruchsprüfung (Phase G, Konzept in
docs/KONZEPT_GUTACHTEN.md, Paket G-4 "Prüfungs-Engine").

Dieses Modul liefert nur den Prompt-Baustein und das Ergebnis-Schema für die
Anspruchsprüfung — bewusst ohne schwere Imports (keine DB, keine Embeddings,
kein LLM-SDK), damit es isoliert testbar ist. Die KI wird angewiesen, im
klassischen Gutachtenstil zu prüfen, aber **ausschließlich strukturiertes
JSON** zurückzugeben, damit der Ergebnis-Editor (T#276) es Abschnitt für
Abschnitt rendern kann statt nur Fließtext zu zeigen.

Tatsächlich aufgerufen wird die KI erst in der Prüfungs-Engine (T#274/T#275);
dieses Modul baut nur Prompt-Text und validiert/parst die Antwort.
"""
import json
import re
from typing import Optional

# ---------------------------------------------------------------- Vokabular

# Bewusst "hoch/mittel/gering" statt einer Prozentzahl (Konzept Abschnitt 2):
# eine Zahl würde eine Genauigkeit vortäuschen, die es nicht gibt.
AMPEL_WERTE = ("hoch", "mittel", "gering")

# Status je Tatbestandsmerkmal. "offen" ist für Beweisfragen — die KI soll
# ungeklärte Punkte nicht einfach unterstellen.
MERKMAL_STATUS = ("erfüllt", "nicht erfüllt", "offen")

# Status für entstanden / nicht erloschen / durchsetzbar sowie dafür, ob eine
# gefundene Einwendung/Einrede tatsächlich greift.
JA_NEIN_OFFEN = ("ja", "nein", "offen")

# Ergebnis je Anspruchsgrundlage.
ANSPRUCH_ERGEBNIS = ("besteht", "besteht nicht", "offen")


SCHEMA_BESCHREIBUNG = """{
  "anspruchsgrundlagen": [
    {
      "bezeichnung": "kurze Bezeichnung des Anspruchs",
      "norm": "z.B. § 433 Abs. 2 BGB",
      "obersatz": "Obersatz im Gutachtenstil: X könnte gegen Y einen Anspruch auf ... aus ... haben.",
      "voraussetzungen": [
        {
          "merkmal": "Name des Tatbestandsmerkmals",
          "norm": "einschlägige Norm (ggf. mit Absatz/Satz)",
          "quelle": "Gesetzestext | Aktenbeleg [Dn] | Entscheidung",
          "status": "erfüllt" | "nicht erfüllt" | "offen",
          "begruendung": "Definition und Subsumtion in 1-3 Sätzen"
        }
      ],
      "entstanden": {"status": "ja" | "nein" | "offen", "begruendung": "..."},
      "nicht_erloschen": {"status": "ja" | "nein" | "offen", "begruendung": "..."},
      "durchsetzbar": {"status": "ja" | "nein" | "offen", "begruendung": "..."},
      "einwendungen_einreden": [
        {
          "bezeichnung": "z.B. Verjährung",
          "norm": "z.B. § 214 BGB",
          "greift": "ja" | "nein" | "offen",
          "begruendung": "..."
        }
      ],
      "rechtsprechung": [
        {
          "gericht": "z.B. BGH",
          "aktenzeichen": "...",
          "datum": "...",
          "bezug": "wofür die Entscheidung herangezogen wird",
          "status": "geprüft" | "nicht in Datenbank" | "nicht prüfbar"
        }
      ],
      "ergebnis": "besteht" | "besteht nicht" | "offen"
    }
  ],
  "erfolgsprognose": {"ampel": "hoch" | "mittel" | "gering", "begruendung": "..."},
  "gesamtergebnis": "Zusammenfassung in 2-3 Sätzen",
  "offene_punkte": ["Rückfrage oder Beweisfrage, falls vorhanden"]
}"""


def build_anspruchspruefung_system_prompt() -> str:
    """System-Prompt für die Anspruchsprüfung im Gutachtenstil. Die KI prüft
    wie in einem klassischen Gutachten (Obersatz, Definition, Subsumtion,
    Ergebnis), gibt das Ergebnis aber ausschließlich als ein einziges
    JSON-Objekt nach SCHEMA_BESCHREIBUNG zurück."""
    return (
        "Du bist ein juristischer Gutachter für deutschsprachige Anwälte. Prüfe den "
        "vorgelegten Fall im klassischen Gutachtenstil (Obersatz, Definition, "
        "Subsumtion, Ergebnis) und antworte NUR mit einem einzigen JSON-Objekt nach "
        "dem unten stehenden Schema — kein Text davor oder danach, keine "
        "Markdown-Codeblöcke, keine Kommentare.\n\n"
        "Arbeitsschritte, die sich im JSON wiederfinden müssen:\n"
        "1. Anspruchsgrundlage(n) identifizieren — ggf. mehrere, in der in der Praxis "
        "üblichen Prüfungsreihenfolge.\n"
        "2. Jede Voraussetzung/jedes Tatbestandsmerkmal EINZELN prüfen, mit Norm, "
        "Quelle (Gesetzestext, Aktenbeleg [Dn] oder Entscheidung) und Status "
        "erfüllt/nicht erfüllt/offen. Zusätzlich je Anspruchsgrundlage gesondert "
        "beurteilen, ob der Anspruch entstanden, nicht erloschen und durchsetzbar ist.\n"
        "3. Einwendungen und Einreden AKTIV GEGENSUCHEN — nicht nur die vom Mandanten "
        "genannten, sondern alle, die nach der Normlage in Betracht kommen (z.B. "
        "Verjährung, Anfechtung, Rücktritt, Zurückbehaltungsrecht, Mitverschulden), "
        "auch wenn sie am Ende nicht greifen. Eine leere Liste ist nur zulässig, wenn "
        "du begründet keine ernsthaft in Betracht kommende Einwendung findest.\n"
        "4. Einschlägige Rechtsprechung benennen, wenn vorhanden — mit Gericht, "
        "Aktenzeichen und Datum. Erfinde NIEMALS ein Aktenzeichen oder eine Fundstelle; "
        "ist dir keine Entscheidung sicher bekannt, lass die Liste leer statt zu raten.\n"
        "5. Erfolgsprognose als Ampel (hoch/mittel/gering) mit Begründung — keine "
        "Prozentangabe, das würde eine Genauigkeit vortäuschen, die es nicht gibt.\n"
        "6. Gesamtergebnis in 2-3 Sätzen.\n\n"
        "Jede Aussage muss mit Normtext, Aktenbeleg [Dn] oder Entscheidung nachprüfbar "
        "sein. Ist eine Tatsache ungeklärt, setze status/greift auf \"offen\" und "
        "formuliere die nötige Beweisfrage in 'begruendung' bzw. in 'offene_punkte', "
        "statt sie zu unterstellen.\n\n"
        f"JSON-Schema (Platzhalter sind beispielhaft, Struktur ist verbindlich):\n{SCHEMA_BESCHREIBUNG}"
    )


def build_anspruchspruefung_user_prompt(
    sachverhalt: str,
    fragestellung: str = "",
    normen_kontext: str = "",
) -> str:
    """Nutzer-Prompt aus dem bestätigten Sachverhalt (siehe T#G-3), der
    Fragestellung des Anwalts und optionalem Normen-/Rechtsprechungskontext
    aus der Datenbank (z.B. über search_service oder die direkte
    Normsuche)."""
    teile = []
    if fragestellung.strip():
        teile.append(f"Fragestellung: {fragestellung.strip()}")
    teile.append(f"Sachverhalt:\n{sachverhalt.strip()}")
    if normen_kontext.strip():
        teile.append(f"Relevante Normen/Rechtsprechung aus der Datenbank:\n{normen_kontext.strip()}")
    teile.append("Prüfe den Fall wie oben beschrieben und gib ausschließlich das JSON-Objekt zurück.")
    return "\n\n".join(teile)


def parse_anspruchspruefung_antwort(text: str) -> dict:
    """Erstes JSON-Objekt aus der KI-Antwort (auch wenn sie es versehentlich
    in ```json ... ``` packt oder Text davor/danach ergänzt)."""
    start = text.find("{")
    ende = text.rfind("}")
    if start < 0 or ende <= start:
        raise ValueError("Keine JSON-Antwort der KI gefunden")
    return json.loads(text[start:ende + 1])


def validate_anspruchspruefung(data: dict) -> list:
    """Prüft die strukturierte Antwort gegen SCHEMA_BESCHREIBUNG und gibt eine
    Liste verständlicher Fehlertexte zurück (leer = gültig). Wird von der
    Prüfungs-Engine genutzt, um eine fehlerhafte KI-Antwort zu erkennen,
    bevor sie dem Anwalt gezeigt wird — und vom Ergebnis-Editor (T#276), um
    pro Abschnitt zu wissen, welche Felder sicher vorhanden sind."""
    fehler = []

    if not isinstance(data, dict):
        return ["Antwort ist kein JSON-Objekt"]

    agls = data.get("anspruchsgrundlagen")
    if not isinstance(agls, list) or not agls:
        fehler.append("anspruchsgrundlagen: fehlt oder ist leer")
        agls = []

    for i, agl in enumerate(agls):
        pfad = f"anspruchsgrundlagen[{i}]"
        if not isinstance(agl, dict):
            fehler.append(f"{pfad}: kein Objekt")
            continue
        for feld in ("bezeichnung", "norm"):
            if not str(agl.get(feld) or "").strip():
                fehler.append(f"{pfad}.{feld}: fehlt oder leer")

        voraussetzungen = agl.get("voraussetzungen")
        if not isinstance(voraussetzungen, list) or not voraussetzungen:
            fehler.append(f"{pfad}.voraussetzungen: fehlt oder ist leer")
            voraussetzungen = []
        for j, v in enumerate(voraussetzungen):
            vpfad = f"{pfad}.voraussetzungen[{j}]"
            if not isinstance(v, dict):
                fehler.append(f"{vpfad}: kein Objekt")
                continue
            for feld in ("merkmal", "norm"):
                if not str(v.get(feld) or "").strip():
                    fehler.append(f"{vpfad}.{feld}: fehlt oder leer")
            if v.get("status") not in MERKMAL_STATUS:
                fehler.append(f"{vpfad}.status: ungültiger Wert {v.get('status')!r} (erlaubt: {MERKMAL_STATUS})")

        for feld in ("entstanden", "nicht_erloschen", "durchsetzbar"):
            block = agl.get(feld)
            if not isinstance(block, dict) or block.get("status") not in JA_NEIN_OFFEN:
                fehler.append(f"{pfad}.{feld}.status: fehlt oder ungültig (erlaubt: {JA_NEIN_OFFEN})")

        # Aktive Gegensuche nach Einwendungen/Einreden ist Kernanforderung (T#273) —
        # das Feld muss vorhanden sein (eine *begründet* leere Liste ist erlaubt,
        # ein fehlendes Feld zeigt aber, dass gar nicht gesucht wurde.
        if not isinstance(agl.get("einwendungen_einreden"), list):
            fehler.append(f"{pfad}.einwendungen_einreden: fehlt (muss Liste sein, ggf. leer)")
        else:
            for k, e in enumerate(agl["einwendungen_einreden"]):
                epfad = f"{pfad}.einwendungen_einreden[{k}]"
                if not isinstance(e, dict):
                    fehler.append(f"{epfad}: kein Objekt")
                    continue
                if not str(e.get("bezeichnung") or "").strip():
                    fehler.append(f"{epfad}.bezeichnung: fehlt oder leer")
                if e.get("greift") not in JA_NEIN_OFFEN:
                    fehler.append(f"{epfad}.greift: ungültiger Wert {e.get('greift')!r} (erlaubt: {JA_NEIN_OFFEN})")

        if not isinstance(agl.get("rechtsprechung"), list):
            fehler.append(f"{pfad}.rechtsprechung: fehlt (muss Liste sein, ggf. leer)")

        if agl.get("ergebnis") not in ANSPRUCH_ERGEBNIS:
            fehler.append(f"{pfad}.ergebnis: ungültiger Wert {agl.get('ergebnis')!r} (erlaubt: {ANSPRUCH_ERGEBNIS})")

    prognose = data.get("erfolgsprognose")
    if not isinstance(prognose, dict) or prognose.get("ampel") not in AMPEL_WERTE:
        fehler.append(f"erfolgsprognose.ampel: fehlt oder ungültig (erlaubt: {AMPEL_WERTE})")
    elif not str(prognose.get("begruendung") or "").strip():
        fehler.append("erfolgsprognose.begruendung: fehlt oder leer")

    if not str(data.get("gesamtergebnis") or "").strip():
        fehler.append("gesamtergebnis: fehlt oder leer")

    if "offene_punkte" in data and not isinstance(data["offene_punkte"], list):
        fehler.append("offene_punkte: muss eine Liste sein, falls vorhanden")

    return fehler
