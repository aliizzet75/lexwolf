"""
Akten-Funktionen für den Desktop-Client: Dokument-Auswertung (Posteingang,
Fristen, Aktenstamm), Schriftsatz-Entwurf aus der Akte mit Belegprüfung und
Sachstandsmitteilung an den Mandanten.

Alle KI-Aufrufe laufen über die zentrale ask_llm(). Fristen werden hier nur
*erkannt* (Ereignis + Fristart), berechnet werden sie im Client
(FristRechner, fest programmiert) — die KI rechnet keine Daten aus.
"""
import asyncio
import json
import logging
import re
from datetime import date
from typing import List, Optional, Tuple

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel

from services.llm_client import ask_llm, using_temporary_test_backend, TEST_MODE_DISCLAIMER

logger = logging.getLogger(__name__)
router = APIRouter(prefix="/akte", tags=["akte"])

MAX_DOKUMENT_ZEICHEN = 15000
MAX_AKTE_ZEICHEN = 60000

FRISTARTEN = {
    "berufung": "Berufung (§ 517 ZPO, 1 Monat ab Zustellung des Urteils)",
    "berufungsbegruendung": "Berufungsbegründung (§ 520 Abs. 2 ZPO, 2 Monate ab Zustellung des Urteils)",
    "revision": "Revision (§ 548 ZPO)",
    "revisionsbegruendung": "Revisionsbegründung (§ 551 ZPO)",
    "einspruch_vu": "Einspruch gegen Versäumnisurteil (§ 339 ZPO, 2 Wochen)",
    "einspruch_vb": "Einspruch gegen Vollstreckungsbescheid (§§ 700, 339 ZPO)",
    "widerspruch_mb": "Widerspruch gegen Mahnbescheid (§ 692 ZPO)",
    "sofortige_beschwerde": "Sofortige Beschwerde (§ 569 ZPO, 2 Wochen)",
    "beschwerde_famfg": "Beschwerde gegen Beschluss in Familiensachen (§ 63 FamFG, 1 Monat)",
    "beschwerdebegruendung_famfg": "Beschwerdebegründung Familienstreitsache (§ 117 FamFG, 2 Monate)",
    "rechtsbeschwerde_famfg": "Rechtsbeschwerde (§ 71 FamFG)",
    "kschg": "Kündigungsschutzklage (§ 4 KSchG, 3 Wochen ab Zugang der Kündigung)",
    "widerspruch_va": "Widerspruch gegen Verwaltungsakt (§ 70 VwGO)",
    "klage_va": "Anfechtungsklage (§ 74 VwGO)",
    "einspruch_ao": "Einspruch gegen Steuerbescheid (§ 355 AO)",
    "berufung_stpo": "Berufung in Strafsachen (§ 314 StPO, 1 Woche ab Verkündung)",
    "revision_stpo": "Revision in Strafsachen (§ 341 StPO)",
    "revisionsbegruendung_stpo": "Revisionsbegründung in Strafsachen (§ 345 StPO)",
    "einspruch_strafbefehl": "Einspruch gegen Strafbefehl (§ 410 StPO)",
    "beschwerde_famfg_eilverfahren": "Beschwerde gegen Beschluss im Verfahren der einstweiligen Anordnung (§ 63 Abs. 2 Nr. 1 FamFG, 2 Wochen)",
    "nzb": "Nichtzulassungsbeschwerde (§ 544 Abs. 3 ZPO, 1 Monat)",
    "nzb_begruendung": "Begründung der Nichtzulassungsbeschwerde (§ 544 Abs. 4 ZPO, 2 Monate)",
    "tatbestandsberichtigung": "Antrag auf Tatbestandsberichtigung (§ 320 ZPO, 2 Wochen)",
    "wiedereinsetzung": "Antrag auf Wiedereinsetzung in den vorigen Stand (§ 234 ZPO)",
    "berufung_arbg": "Berufung gegen Urteil des Arbeitsgerichts (§ 66 ArbGG, 1 Monat)",
    "berufungsbegruendung_arbg": "Berufungsbegründung Arbeitsgerichtsverfahren (§ 66 ArbGG, 2 Monate)",
    "einspruch_vu_arbg": "Einspruch gegen Versäumnisurteil des Arbeitsgerichts (§ 59 ArbGG, 1 Woche)",
    "widerspruch_sgg": "Widerspruch gegen Bescheid eines Sozialleistungsträgers (§ 84 SGG, 1 Monat)",
    "klage_sgg": "Klage zum Sozialgericht (§ 87 SGG, 1 Monat)",
    "berufung_sgg": "Berufung gegen Urteil des Sozialgerichts (§ 151 SGG, 1 Monat)",
    "zulassung_berufung_vwgo": "Antrag auf Zulassung der Berufung gegen Urteil des Verwaltungsgerichts (§ 124a Abs. 4 VwGO, 1 Monat)",
    "zulassung_begruendung_vwgo": "Begründung des Antrags auf Zulassung der Berufung (§ 124a Abs. 4 VwGO, 2 Monate)",
    "klage_fgo": "Klage zum Finanzgericht (§ 47 FGO, 1 Monat)",
    "gesetzt": "Vom Gericht, der Gegenseite oder einer Behörde gesetzte Frist mit konkretem Datum (frist_datum angeben)",
    "frei": "Sonstige Frist mit Dauer (anzahl + einheit angeben), z. B. 'binnen zwei Wochen'",
}


# ----------------------------------------------------------------- Hilfen

def _json_aus_antwort(text: str) -> dict:
    """Erstes JSON-Objekt aus der KI-Antwort (auch wenn sie es in ```json ... ``` packt)."""
    start = text.find("{")
    ende = text.rfind("}")
    if start < 0 or ende <= start:
        raise ValueError("Keine JSON-Antwort")
    return json.loads(text[start:ende + 1])


def _kuerzen(text: str, n: int) -> str:
    return text if len(text) <= n else text[:n] + "\n[... gekürzt ...]"


# ----------------------------------------------------------------- Dokument-Auswertung

class AnalyseRequest(BaseModel):
    titel: str
    text: str
    eingangsdatum: Optional[str] = None  # Datei-Datum, falls kein Zustelldatum im Text
    mandant: Optional[str] = None


_ANALYSE_PROMPT = """Du bist Kanzleiassistenz in einer deutschen Anwaltskanzlei und wertest ein Dokument aus der Mandantenakte aus.
Heute ist der {heute}. Mandant der Kanzlei: {mandant}.

Antworte AUSSCHLIESSLICH mit einem JSON-Objekt in genau diesem Format (keine Erklärungen davor oder danach):
{{
  "dokumenttyp": "Urteil | Beschluss | Versäumnisurteil | Gerichtliche Verfügung | Ladung | Schriftsatz Gegenseite | Bescheid | Mahnbescheid | Kündigung | Vertrag | E-Mail/Brief Mandant | Sonstiges",
  "absender": "wer das Dokument verfasst hat",
  "datum": "YYYY-MM-DD des Dokuments oder null",
  "zusammenfassung": "2-3 Sätze, sachlich",
  "anliegen": "was wird von der Kanzlei/dem Mandanten verlangt oder erwartet (leer, wenn nichts)",
  "naechster_schritt": "konkreter Vorschlag, was die Kanzlei jetzt tun sollte",
  "eilig": true oder false,
  "fristen": [
    {{"bezeichnung": "z.B. Berufungsfrist", "fristart_id": "eine der IDs unten",
      "ereignis_datum": "YYYY-MM-DD des fristauslösenden Ereignisses (Zustellung/Zugang/Verkündung) NUR wenn im Text genannt, sonst null",
      "frist_datum": "YYYY-MM-DD nur bei gesetzter Frist mit konkretem Datum, sonst null",
      "anzahl": Zahl oder null, "einheit": "Tage | Wochen | Monate" oder null,
      "zitat": "wörtliche Textstelle, aus der sich die Frist ergibt"}}
  ],
  "beteiligte": [{{"rolle": "Mandant | Gegner | Gegnervertreter | Gericht | Richter | Behörde | Zeuge | Sachverständiger | Sonstige", "name": "..."}}],
  "gericht": "Gericht oder Behörde, leer wenn keins",
  "aktenzeichen": "Aktenzeichen des Gerichts/der Behörde, leer wenn keins",
  "ereignisse": [{{"datum": "YYYY-MM-DD", "beschreibung": "kurz, für eine Zeitleiste des Sachverhalts"}}]
}}

Fristart-IDs:
{fristarten}

Regeln:
- Erkenne Fristen nur, wenn sie sich aus dem Dokument ergeben (z.B. Rechtsmittelbelehrung eines Urteils, gesetzte Stellungnahmefrist, Kündigung -> Kündigungsschutzklage). Erfinde keine.
- Berechne KEIN Fristende selbst. Gib nur Ereignis und Fristart an — gerechnet wird in der Software.
- Ein Zustelldatum steht oft nicht im Dokument selbst — dann ereignis_datum null.
- Personen nur mit Namen aufnehmen, die im Dokument stehen.
- Datumsangaben immer als YYYY-MM-DD.

Dokument "{titel}":
---
{text}
---"""


@router.post("/analyse")
async def analyse(req: AnalyseRequest):
    prompt = _ANALYSE_PROMPT.format(
        heute=date.today().isoformat(),
        mandant=req.mandant or "unbekannt",
        fristarten="\n".join(f"- {k}: {v}" for k, v in FRISTARTEN.items()),
        titel=req.titel,
        text=_kuerzen(req.text, MAX_DOKUMENT_ZEICHEN),
    )
    try:
        antwort = await asyncio.to_thread(ask_llm, prompt, max_tokens=3000)
        daten = _json_aus_antwort(antwort)
    except Exception as e:
        logger.error(f"Dokument-Auswertung fehlgeschlagen: {e}")
        raise HTTPException(status_code=502, detail="KI-Auswertung fehlgeschlagen")
    # Unbekannte Fristarten nicht durchreichen — der Client rechnet nur bekannte.
    for f in daten.get("fristen") or []:
        if f.get("fristart_id") not in FRISTARTEN:
            f["fristart_id"] = "frei" if f.get("anzahl") else ("gesetzt" if f.get("frist_datum") else None)
    return daten


# ----------------------------------------------------------------- Belegprüfung

_PARAGRAPH_ZITAT = re.compile(
    r"(§§?|Art\.)\s*(\d+[a-z]?)(?:\s*(?:Abs\.|Absatz)\s*\d+[a-z]?)?(?:\s*(?:S\.|Satz)\s*\d+)?(?:\s*(?:Nr\.|Nummer)\s*\d+)?"
    r"\s+((?:[A-ZÄÖÜ][A-Za-zäöü]*){1,3}(?:\s+[IVX]+)?)\b")
# "XII ZB 50/25", "2 StR 768/25", "9 A 6.24" (BVerwG schreibt Punkt statt Schrägstrich)
_AZ_ZITAT = re.compile(r"\b(?:[IVX]+|\d{1,2})\s?[A-Z][A-Za-z]{0,3}\s?\d{1,5}[/.]\d{2}\b")


def _entscheidung_in_db(az: str) -> Tuple[str, Optional[int]]:
    """('gefunden: BGH, 10.06.2026', chunk_id) | ('nicht in Datenbank', None) | ('nicht prüfbar', None).
    Die Datenbank enthält nur die vom Crawler geladenen Entscheidungen der
    Bundesgerichte — nicht gefunden heißt nicht falsch. chunk_id (T#296) ist die
    id des gefundenen legal_chunks-Eintrags, für die exakte Positionsangabe der Quelle."""
    try:
        from sqlalchemy import text as sqltxt
        from api.chat import _get_search
        db = _get_search().database_service.SessionLocal()
        try:
            row = db.execute(sqltxt(
                "SELECT id, court, date FROM legal_chunks WHERE regexp_replace(case_number, '\\s', '', 'g') ILIKE :az LIMIT 1"),
                {"az": re.sub(r"\s", "", az)}).first()
            if not row:
                return "nicht in Datenbank", None
            datum = f", {row.date:%d.%m.%Y}" if row.date else ""
            return f"gefunden: {row.court}{datum}", row.id
        finally:
            db.close()
    except Exception as e:
        logger.warning(f"Rechtsprechungsprüfung nicht möglich: {e}")
        return "nicht prüfbar", None
_DOK_VERWEIS = re.compile(r"\[D(\d+)(?:[^\]]*)\]")


def _paragraph_in_db(nr: str, gesetz: str) -> Tuple[str, Optional[int]]:
    """('geprüft', chunk_id) | ('nicht gefunden', None) | ('nicht in Datenbank', None).
    chunk_id (T#296) ist die id des gefundenen legal_chunks-Eintrags, für die
    exakte Positionsangabe der Quelle in der Response."""
    try:
        from sqlalchemy import text as sqltxt
        from api.chat import _get_search
        db = _get_search().database_service.SessionLocal()
        try:
            tag = gesetz.lower().replace(" ", "")
            gesetz_da = db.execute(sqltxt(
                "SELECT 1 FROM legal_chunks WHERE tags ILIKE :t LIMIT 1"), {"t": f"{tag}%"}).first()
            if not gesetz_da:
                return "nicht in Datenbank", None
            # Titel kommen in zwei Formaten vor: "§ 115 Kosten …" und "FAMFG - § 115 Kosten …"
            treffer = db.execute(sqltxt(
                "SELECT id FROM legal_chunks WHERE tags ILIKE :t AND (title ILIKE :p1 OR title ILIKE :p2) LIMIT 1"),
                {"t": f"{tag}%", "p1": f"§ {nr} %", "p2": f"% § {nr} %"}).first()
            return ("geprüft", treffer.id) if treffer else ("nicht gefunden", None)
        finally:
            db.close()
    except Exception as e:
        logger.warning(f"Belegprüfung nicht möglich: {e}")
        return "nicht prüfbar", None


def pruefe_belege(text: str, anzahl_dokumente: int) -> list:
    belege = []
    gesehen = set()
    for m in _PARAGRAPH_ZITAT.finditer(text):
        nr, gesetz = m.group(2), m.group(3).strip()
        if gesetz in ("Abs", "Satz", "Nr") or (nr, gesetz) in gesehen:
            continue
        gesehen.add((nr, gesetz))
        if m.group(1) == "Art.":
            # Artikel (GG, EGBGB, DSGVO …) stehen nicht unter "§" in der Datenbank
            belege.append({"zitat": f"Art. {nr} {gesetz}", "art": "Norm", "status": "bitte prüfen", "chunk_id": None})
        else:
            status, chunk_id = _paragraph_in_db(nr, gesetz)
            belege.append({"zitat": f"§ {nr} {gesetz}", "art": "Norm", "status": status, "chunk_id": chunk_id})
    for m in _AZ_ZITAT.finditer(text):
        if m.group(0) in gesehen:
            continue
        gesehen.add(m.group(0))
        status, chunk_id = _entscheidung_in_db(m.group(0))
        belege.append({"zitat": m.group(0), "art": "Rechtsprechung",
                       "status": "geprüft" if status.startswith("gefunden") else "bitte prüfen",
                       "hinweis": status, "chunk_id": chunk_id})
    for m in _DOK_VERWEIS.finditer(text):
        nr = int(m.group(1))
        key = f"D{nr}"
        if key in gesehen:
            continue
        gesehen.add(key)
        belege.append({"zitat": m.group(0), "art": "Akte",
                       "status": "geprüft" if 1 <= nr <= anzahl_dokumente else "Dokument existiert nicht",
                       "chunk_id": None})
    return belege


# ----------------------------------------------------------------- Schriftsatz

class AktenDokument(BaseModel):
    titel: str
    text: str


class SchriftsatzRequest(BaseModel):
    art: str
    anweisung: str = ""
    mandant: str = ""
    stammdaten: str = ""
    dokumente: List[AktenDokument] = []
    stil_vorlage: str = ""  # eigener Musterschriftsatz der Kanzlei (nur Stil, keine Inhalte)
    # T#310: Ergebnisse der Agentic-RAG-Suche (GET /api/search/stream), die der Client
    # vor diesem Request zu Art+Anweisung durchgeführt hat (siehe desktop/Dialogs/
    # SchriftsatzDialog.RunLiveSearchAsync + Services/AgenticSearchContext). Normen/
    # Rechtsprechung sollen daraus zitiert werden statt aus dem Trainingswissen der KI.
    recherche_kontext: str = ""
    # T#312: Kanzlei-Stammdaten aus den Einstellungen (Briefkopf/Briefende/Kontodaten) und der
    # Kanzleiort — damit Datum/Ort und Briefkopf/-ende im Entwurf selbst stehen, nicht als
    # Platzhalter für den Anwalt zum Nachtragen.
    kanzlei_stammdaten: str = ""
    ort: str = ""


def _kanzleistil(art: str) -> str:
    """Stilvorgaben aus dem gelernten Profil (services/style_learner.py); leer, wenn noch keins existiert."""
    try:
        from services.style_learner import StyleLearner
        a = art.lower()
        typ = "klage" if ("klage" in a or "antrag" in a or "begründung" in a) else ("brief" if "schreiben" in a else None)
        profil = StyleLearner(schriftsatz_typ=typ).verwende_typ_spezifisches_profil(fallback=True)
        if not profil:
            return ""
        teile = []
        if profil.durchschnittliche_satzlaenge:
            teile.append(f"Sätze im Schnitt etwa {round(profil.durchschnittliche_satzlaenge)} Wörter lang")
        try:
            bigramme = list(json.loads(profil.formulierungs_praeferenzen or "{}").get("top_bigramme", {}).keys())[:12]
            if bigramme:
                teile.append("bevorzugte Wendungen: " + ", ".join(f"„{b}“" for b in bigramme))
        except (ValueError, AttributeError):
            pass
        return "; ".join(teile)
    except Exception as e:
        logger.info(f"Kein Stilprofil verfügbar: {e}")
        return ""


_SCHRIFTSATZ_PROMPT = """Du bist erfahrene Rechtsanwältin/erfahrener Rechtsanwalt in Deutschland und entwirfst für die Kanzlei: {art}.
Mandant: {mandant}
{stammdaten}
{kanzlei}
Heutiges Datum: {heute}{ort_hinweis}

WICHTIG: "{mandant}" ist der tatsächliche, bereits bekannte Name — verwende diesen Namen direkt im Rubrum und im Fließtext. Schreibe NICHT das Platzhalterwort "Mandant" oder "[Ergänzung erforderlich: Name]" an Stellen, an denen der Name bereits oben angegeben ist.

Anweisung der Anwältin/des Anwalts: {anweisung}
{recherche}
Die Akte besteht aus folgenden Dokumenten, nummeriert als [D1], [D2], ...:
{dokumente}

Anforderungen an den Entwurf:
- Vollständig ausformuliert, {aufbau}, Deutsch, in flüssigem professionellem Anwaltsdeutsch mit vollständigen, verbundenen Sätzen (Haupt- und Nebensätze, Konjunktionen wie "da", "wodurch", "sodass") — KEINE Aneinanderreihung kurzer Einzelsätze im Stakkato-Stil ("Ein Vorfall ereignete sich. Datum: X. Ort: Y."). Mehrere zusammengehörige Tatsachen aus derselben Fundstelle gehören in EINEN zusammenhängenden Satz, nicht in mehrere Kurzsätze mit je eigener Klammer.
- JEDE Tatsachenbehauptung aus der Akte mit Fundstelle belegen, z.B. [D2] oder [D2, S. 3]. Seitenzahlen nur angeben, wenn das Dokument [Seite N]-Marken enthält — sonst nur [D2]. Die Klammer steht am Satzende, nicht nach jedem Teilsatz. Was nicht in der Akte steht, als [Ergänzung erforderlich: ...] markieren — nichts erfinden.
- Anlagen als K1/B1 usw. bezeichnen und am Ende ein Anlagenverzeichnis mit Zuordnung zu [Dn] anfügen.
- Normen genau zitieren (z.B. § 1361 Abs. 1 BGB). Normen und Rechtsprechung bevorzugt aus den oben genannten Rechercheergebnissen entnehmen (falls vorhanden) statt aus deinem Trainingswissen — das sind echte Treffer aus der Rechtsdatenbank der Kanzlei. Rechtsprechung nur zitieren, wenn du dir des Aktenzeichens sicher bist (aus der Recherche oder Akte), sonst weglassen.
- Datum/Ort-Zeile (falls im Aufbau üblich) und Briefkopf/Briefende (falls oben angegeben) direkt mit diesen echten Angaben ausfüllen — kein Platzhalter wie "[Ort], den [Datum]" oder "[Kanzleiname]".
- Kontodaten nur einfügen, wenn der Entwurf tatsächlich eine Zahlung verlangt (z.B. Kostennote, Rechnung) — sonst weglassen.
- Keine Vorrede, keine Meta-Kommentare — nur der Schriftsatz.{stil}"""

_GUTACHTEN_AUFBAU = (
    "Gutachtenstil statt gerichtsüblichem Schriftsatz (Sachverhalt, rechtliche Würdigung je Anspruch/"
    "Streitpunkt im Gutachtenstil — Obersatz, Definition, Subsumtion, Zwischenergebnis —, abschließende "
    "Einschätzung der Erfolgsaussichten als hoch/mittel/gering mit Begründung)"
)
_SCHRIFTSATZ_AUFBAU = "gerichtsüblicher Aufbau (Rubrum soweit bekannt, Anträge, Sachverhalt, rechtliche Würdigung)"


def _aufbau_fuer_art(art: str) -> str:
    """Gutachten/Erfolgsaussichten-Anfragen brauchen Gutachtenstil statt Klage-Rubrum+Anträge."""
    a = art.lower()
    if "gutachten" in a or "erfolgsaussicht" in a:
        return _GUTACHTEN_AUFBAU
    return _SCHRIFTSATZ_AUFBAU


@router.post("/schriftsatz")
async def schriftsatz(req: SchriftsatzRequest):
    teile, gesamt = [], 0
    for i, d in enumerate(req.dokumente, 1):
        stueck = _kuerzen(d.text, max(2000, (MAX_AKTE_ZEICHEN - gesamt) // max(1, len(req.dokumente) - i + 1)))
        gesamt += len(stueck)
        teile.append(f"[D{i}] {d.titel}\n{stueck}")
    stil = ""
    profil = await asyncio.to_thread(_kanzleistil, req.art)
    if profil:
        stil += f"\n- Stil der Kanzlei (aus Korrekturen gelernt): {profil}."
    if req.stil_vorlage.strip():
        stil += ("\n- Schreibe im Stil dieses Musterschriftsatzes der Kanzlei: gleicher Aufbau, Ton, Gliederung und typische "
                 "Formulierungen. Übernimm daraus KEINE Inhalte, Namen oder Tatsachen.\n"
                 f"Musterschriftsatz:\n---\n{_kuerzen(req.stil_vorlage, 8000)}\n---")
    recherche = ""
    if req.recherche_kontext.strip():
        recherche = ("\nErgebnisse der Rechtsrecherche (Agentic-RAG-Suche in der echten Rechtsdatenbank, "
                     "vor diesem Entwurf zu Art und Anweisung durchgeführt):\n"
                     f"{_kuerzen(req.recherche_kontext, 8000)}\n")
    prompt = _SCHRIFTSATZ_PROMPT.format(
        art=req.art, mandant=req.mandant or "—",
        stammdaten=f"Aktenstamm: {req.stammdaten}" if req.stammdaten else "",
        kanzlei=f"Kanzlei-Stammdaten:\n{req.kanzlei_stammdaten}" if req.kanzlei_stammdaten.strip() else "",
        heute=date.today().strftime("%d.%m.%Y"),
        ort_hinweis=f", Ort der Kanzlei (für die Datum/Ort-Zeile): {req.ort}" if req.ort.strip() else "",
        anweisung=req.anweisung or "keine besonderen Vorgaben",
        recherche=recherche,
        dokumente="\n\n".join(teile) or "(keine Dokumente)",
        aufbau=_aufbau_fuer_art(req.art),
        stil=stil)
    try:
        text = await asyncio.to_thread(ask_llm, prompt, max_tokens=8000, rolle="schriftsatz")
    except Exception as e:
        logger.error(f"Schriftsatz fehlgeschlagen: {e}")
        raise HTTPException(status_code=502, detail="KI aktuell nicht erreichbar")
    belege = await asyncio.to_thread(pruefe_belege, text, len(req.dokumente))
    if using_temporary_test_backend():
        text += TEST_MODE_DISCLAIMER
    return {"text": text, "belege": belege}


# ----------------------------------------------------------------- Fragebogen einlesen

class FragebogenRequest(BaseModel):
    text: str
    mandant: str = ""


_FRAGEBOGEN_PROMPT = """Der Mandant {mandant} hat einen Fragebogen einer Familienrechtskanzlei ausgefüllt zurückgeschickt.
Lies die Angaben aus und antworte AUSSCHLIESSLICH mit einem JSON-Objekt. Felder ohne Angabe weglassen oder null — nichts schätzen.
Beträge als Zahl in Euro (monatlich, wo nach Einkommen gefragt), Datumsangaben als YYYY-MM-DD.
{{
  "netto_mandant": Zahl, "netto_gegenseite": Zahl,
  "mandant_erwerbstaetig": true/false, "gegenseite_erwerbstaetig": true/false,
  "abzuege_mandant": Zahl, "abzuege_gegenseite": Zahl,
  "kinder": [{{"name": "...", "geburtsdatum": "YYYY-MM-DD", "lebt_bei_mandant": true/false}}],
  "heiratsdatum": "YYYY-MM-DD", "trennungsdatum": "YYYY-MM-DD",
  "anfangsvermoegen_mandant": Zahl, "privilegiert_mandant": Zahl, "endvermoegen_mandant": Zahl,
  "anfangsvermoegen_gegenseite": Zahl, "privilegiert_gegenseite": Zahl, "endvermoegen_gegenseite": Zahl,
  "anzahl_rentenanrechte": Zahl,
  "wohnsituation": "...", "bemerkungen": "weitere wichtige Angaben des Mandanten, kurz"
}}

Fragebogen:
---
{text}
---"""


@router.post("/fragebogen-lesen")
async def fragebogen_lesen(req: FragebogenRequest):
    prompt = _FRAGEBOGEN_PROMPT.format(mandant=req.mandant or "", text=_kuerzen(req.text, MAX_DOKUMENT_ZEICHEN))
    try:
        antwort = await asyncio.to_thread(ask_llm, prompt, max_tokens=2000)
        return _json_aus_antwort(antwort)
    except Exception as e:
        logger.error(f"Fragebogen einlesen fehlgeschlagen: {e}")
        raise HTTPException(status_code=502, detail="KI-Auswertung fehlgeschlagen")


# ----------------------------------------------------------------- Formular aus der Akte ausfüllen

# Felder, die der Client kennt (desktop/Services/Formularwerte.cs) — nur diese werden zurückgegeben.
FORMULAR_FELDER = {
    "mandant_ist_unterhaltspflichtig": "true, wenn der Mandant Unterhalt zahlen soll; false, wenn er Unterhalt verlangt",
    "netto_mandant": "monatliches Nettoeinkommen des Mandanten in Euro (bei mehreren Abrechnungen Durchschnitt)",
    "netto_gegenseite": "monatliches Nettoeinkommen der Gegenseite in Euro",
    "mandant_erwerbstaetig": "true/false",
    "gegenseite_erwerbstaetig": "true/false",
    "abzuege_mandant": "monatliche Abzüge des Mandanten (Kredite, Altersvorsorge) in Euro",
    "abzuege_gegenseite": "monatliche Abzüge der Gegenseite in Euro",
    "kinder": "Liste [{\"name\": \"...\", \"geburtsdatum\": \"YYYY-MM-DD\", \"lebt_bei_mandant\": true/false}]",
    "heiratsdatum": "YYYY-MM-DD",
    "trennungsdatum": "YYYY-MM-DD",
    "zustellung_scheidungsantrag": "YYYY-MM-DD",
    "anfangsvermoegen_mandant": "Euro",
    "privilegiert_mandant": "Erbschaften/Schenkungen des Mandanten während der Ehe in Euro",
    "endvermoegen_mandant": "Euro",
    "anfangsvermoegen_gegenseite": "Euro",
    "privilegiert_gegenseite": "Euro",
    "endvermoegen_gegenseite": "Euro",
    "anzahl_rentenanrechte": "Anzahl Rentenanrechte beider Ehegatten zusammen (Zahl)",
    "wohnsituation": "kurzer Text",
}


class FormularRequest(BaseModel):
    mandant: str = ""
    gegner: str = ""
    dokumente: List[AktenDokument] = []


_FORMULAR_PROMPT = """Du bist Kanzleiassistenz einer Familienrechtskanzlei und füllst den Mandanten-Fragebogen aus den Dokumenten der Akte aus.
Mandant der Kanzlei: {mandant}
Gegenseite: {gegner}

Felder:
{felder}

Antworte AUSSCHLIESSLICH mit einem JSON-Objekt in diesem Format:
{{"felder": {{"feldname": {{"wert": ..., "quelle": "D2", "zitat": "wörtliche, kurze Textstelle"}}}}}}

Regeln:
- Nur Felder aufnehmen, die sich eindeutig aus den Dokumenten ergeben. Nichts schätzen, nichts erfinden — fehlende Felder weglassen.
- "quelle" ist die Nummer des Dokuments ([D1], [D2] …), aus dem der Wert stammt; "zitat" die Stelle, die ihn belegt.
- Achte darauf, wer Mandant und wer Gegenseite ist. Ist das unklar, Feld weglassen.
- Beträge als Zahl in Euro ohne Tausenderpunkte, Datumsangaben als YYYY-MM-DD, Ja/Nein als true/false.

Dokumente:
{dokumente}"""


@router.post("/formular-ausfuellen")
async def formular_ausfuellen(req: FormularRequest):
    teile, gesamt = [], 0
    for i, d in enumerate(req.dokumente, 1):
        stueck = _kuerzen(d.text, max(2000, (MAX_AKTE_ZEICHEN - gesamt) // max(1, len(req.dokumente) - i + 1)))
        gesamt += len(stueck)
        teile.append(f"[D{i}] {d.titel}\n{stueck}")
    if not teile:
        return {"felder": {}}
    prompt = _FORMULAR_PROMPT.format(
        mandant=req.mandant or "unbekannt", gegner=req.gegner or "unbekannt",
        felder="\n".join(f"- {k}: {v}" for k, v in FORMULAR_FELDER.items()),
        dokumente="\n\n".join(teile))
    try:
        antwort = await asyncio.to_thread(ask_llm, prompt, max_tokens=3000)
        daten = _json_aus_antwort(antwort)
    except Exception as e:
        logger.error(f"Formular ausfüllen fehlgeschlagen: {e}")
        raise HTTPException(status_code=502, detail="KI-Auswertung fehlgeschlagen")
    return {"felder": bereinige_formular(daten.get("felder") or {}, [d.titel for d in req.dokumente])}


def bereinige_formular(felder: dict, titel: List[str]) -> dict:
    """Nur bekannte Felder mit Wert; "D3" wird zum Dokumenttitel aufgelöst."""
    ergebnis = {}
    for name, eintrag in felder.items():
        if name not in FORMULAR_FELDER or not isinstance(eintrag, dict):
            continue
        wert = eintrag.get("wert")
        if wert is None or wert == "" or wert == []:
            continue
        quelle = str(eintrag.get("quelle") or "")
        m = re.search(r"D?(\d+)", quelle)
        if m and 1 <= int(m.group(1)) <= len(titel):
            quelle = titel[int(m.group(1)) - 1]
        ergebnis[name] = {"wert": wert, "quelle": quelle, "zitat": str(eintrag.get("zitat") or "")[:300]}
    return ergebnis


# ----------------------------------------------------------------- Antwort auf einen Eingang

class AntwortRequest(BaseModel):
    titel: str
    text: str
    empfaenger: str = "Absender des Dokuments"
    mandant: str = ""
    stammdaten: str = ""
    hinweis: str = ""


_ANTWORT_PROMPT = """Du bist Rechtsanwältin/Rechtsanwalt einer deutschen Kanzlei und entwirfst die Antwort auf ein eingegangenes Dokument.
Mandant der Kanzlei: {mandant}
{stammdaten}
Empfänger der Antwort: {empfaenger}
Vorgabe der Anwältin/des Anwalts: {hinweis}

Eingegangenes Dokument "{titel}":
---
{text}
---

Anforderungen:
- Tonfall passend zum Empfänger: gegenüber Gericht und Gegenseite sachlich-förmlich (Aktenzeichen nennen), gegenüber dem Mandanten verständlich und freundlich.
- Gehe konkret auf das ein, was im Dokument verlangt oder mitgeteilt wird. Was dafür noch fehlt, als [Ergänzung erforderlich: ...] markieren — nichts erfinden, keine Zusagen ohne Grundlage.
- Fristen, die sich aus dem Dokument ergeben, im Blick behalten, aber kein Fristende selbst ausrechnen.
- Format: erste Zeile "Betreff: ...", dann Leerzeile, dann der Text mit Anrede und Grußformel. Reiner Text ohne Markdown, keine Meta-Kommentare."""


@router.post("/antwort")
async def antwort(req: AntwortRequest):
    prompt = _ANTWORT_PROMPT.format(
        mandant=req.mandant or "—",
        stammdaten=f"Aktenstamm: {req.stammdaten}" if req.stammdaten else "",
        empfaenger=req.empfaenger, hinweis=req.hinweis or "keine",
        titel=req.titel, text=_kuerzen(req.text, MAX_DOKUMENT_ZEICHEN))
    try:
        text = await asyncio.to_thread(ask_llm, prompt, max_tokens=3000)
    except Exception as e:
        logger.error(f"Antwortentwurf fehlgeschlagen: {e}")
        raise HTTPException(status_code=502, detail="KI aktuell nicht erreichbar")
    if using_temporary_test_backend():
        text += TEST_MODE_DISCLAIMER
    return {"text": text}


# ----------------------------------------------------------------- Sachstand

class SachstandRequest(BaseModel):
    mandant: str
    anrede: str = ""
    stammdaten: str = ""
    entwicklungen: List[str] = []
    fristen: List[str] = []
    notizen: List[str] = []
    hinweis: str = ""


_SACHSTAND_PROMPT = """Schreibe für eine deutsche Anwaltskanzlei eine Sachstandsmitteilung an den Mandanten {mandant}.
Anrede: {anrede}
{stammdaten}

Was sich in der Akte getan hat (neueste zuerst):
{entwicklungen}

Laufende Fristen und Termine:
{fristen}

Interne Notizen der Kanzlei (nur verwenden, was für den Mandanten bestimmt ist):
{notizen}

Zusätzlicher Hinweis der Anwältin/des Anwalts: {hinweis}

Anforderungen:
- Verständlich für Laien, freundlich, sachlich; Fachbegriffe kurz erklären.
- Aufbau: Was ist passiert — was bedeutet das — wie geht es weiter — was wird vom Mandanten gebraucht (konkret, mit Datum, falls bekannt).
- Keine internen Einschätzungen zu Erfolgsaussichten, die nicht ausdrücklich im Hinweis stehen.
- Als E-Mail: erste Zeile "Betreff: ...", dann Leerzeile, dann Text mit Grußformel. Keine Meta-Kommentare.
- Reiner Text ohne Markdown (keine Sternchen, keine Rauten)."""


@router.post("/sachstand")
async def sachstand(req: SachstandRequest):
    def liste(xs):
        return "\n".join(f"- {x}" for x in xs) if xs else "- (keine)"
    prompt = _SACHSTAND_PROMPT.format(
        mandant=req.mandant, anrede=req.anrede or "passend wählen",
        stammdaten=f"Akte: {req.stammdaten}" if req.stammdaten else "",
        entwicklungen=liste(req.entwicklungen), fristen=liste(req.fristen),
        notizen=liste(req.notizen), hinweis=req.hinweis or "—")
    try:
        text = await asyncio.to_thread(ask_llm, prompt, max_tokens=3000)
    except Exception as e:
        logger.error(f"Sachstand fehlgeschlagen: {e}")
        raise HTTPException(status_code=502, detail="KI aktuell nicht erreichbar")
    if using_temporary_test_backend():
        text += TEST_MODE_DISCLAIMER
    return {"text": text}
