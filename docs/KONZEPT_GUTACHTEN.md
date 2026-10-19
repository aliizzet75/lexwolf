# Konzept: Gutachtliche Prüfung von Rechtsfragen und Fällen

Stand: 19.10.2026. Anforderung eines Anwalts, umzusetzen als Phase **G** der Roadmap
(`docs/ROADMAP.md`). Dieses Dokument ist die Grundlage für die Umsetzung durch Claude Code.

## 1. Anforderung (vom Anwalt)

Die KI soll Rechtsfragen und Fälle **gutachtlich prüfen**:

1. **Erfolgsaussichten** einer Klage bzw. eines Antrags beurteilen,
2. prüfen, ob ein **Anspruch besteht oder nicht**,
3. **Einwände und Angriffsmöglichkeiten** gegen eine Klage der Gegenseite finden,
4. alles **anhand der konkreten Rechtsnormen**,
5. im Idealfall **einschlägige Rechtsprechung** finden,
6. im Idealfall die Lösung **in den konkreten Fall einarbeiten** (Schriftsatz, Mandantenschreiben).

## 2. Machbarkeit — kurz

| Teil | Machbar? | Bedingung |
|---|---|---|
| Prüfung im Gutachtenstil anhand von Normen (1–4) | **Ja** | Starkes Sprachmodell; Gesetzestexte liegen vollständig vor |
| Sachverhalt aus der Akte | **Ja** | Baut auf Dokument-Auswertung und Fundstellen [Dn] auf |
| Einschlägige Rechtsprechung (5) | **Nur eingeschränkt** | Bestand muss erheblich ausgebaut werden (siehe 3.1) |
| Einarbeiten in den Fall (6) | **Ja** | Nutzt Schriftsatz-, Antwort- und Sachstand-Funktion |

Eine **Erfolgsaussicht** wird qualitativ angegeben (hoch / offen / gering, mit Gründen), **keine
Prozentzahl**: Die würde eine Genauigkeit vortäuschen, die es nicht gibt.

Das Ergebnis ist immer ein **Entwurf zur Prüfung durch den Anwalt**. Die Verantwortung bleibt nach
§ 43 BRAO bei ihm. Jede Aussage muss deshalb mit Normtext, Fundstelle in der Akte oder Entscheidung
nachprüfbar sein.

## 3. Bestandsaufnahme und Lücken

Am 19.10.2026 in der Datenbank auf dem Server gezählt (nur lesend):

- **Gesetze:** 741.281 Textabschnitte; das Bundesrecht ist breit abgedeckt (BGB, ZPO, FamFG,
  SGB, StGB, StPO, HGB, AO, VwGO, BRAO …). **Gut.** Gültigkeitsangaben (`valid_from`,
  `valid_until`) sind als Spalten angelegt; ob sie befüllt sind, ist zu prüfen.
- **Rechtsprechung:** **843 Entscheidungen**, nur Bundesgerichte (BGH 441, BVerwG 141, BSG 80,
  BFH 74, BVerfG 56, BAG 39, BPatG 12), fast alle aus 2025/2026. Der Crawler holt nur den Feed der
  letzten Tage. **Für die Anforderung völlig unzureichend.**
- **Oberlandesgerichte, Land- und Amtsgerichte:** **keine.** Im Familienrecht entscheiden die OLG
  aber meist abschließend; ihre Rechtsprechung und die **Unterhaltsleitlinien der OLG** sind zentral.
- **Kommentare und Fachliteratur** (Grüneberg, MüKo usw.): **keine.** Sie sind lizenzpflichtig.
- **Landesrecht:** nicht vorhanden.
- **Vorhandene Bausteine:**
  - hybride Suche (Volltext und Vektoren) in `search_service.py`;
  - direkte Paragraphensuche (`_direct_paragraph_search`);
  - Verweisungsgraph zwischen Normen in Neo4j (`VERWEIST_AUF`);
  - Belegprüfung `pruefe_belege` aus dem Schriftsatz;
  - Prompt, der nur mit vorgelegten Quellen antworten lässt (`react_engine.GROUNDED_SYSTEM_PROMPT`);
  - Qualitätstests in `tests/quality`.
- **Schwachstellen:**
  - `/knowledge/stats` liefert nur Platzhalterzahlen.
  - Der SSH-Tunnel der Entwicklungsumgebung reißt wiederholt ab (zuletzt über Nacht).
  - Der KI-Proxy über Claude Code stößt bei vielen Anfragen an Nutzungsgrenzen (im Log zu sehen:
    „session limit“). Ein Gutachten braucht 10–20 KI-Aufrufe.

### 3.1 Was fehlt — und woher es kommen kann

| Lücke | Quelle | Aufwand | Hinweis |
|---|---|---|---|
| Ältere Bundesrechtsprechung (ab ca. 2010) | rechtsprechung-im-internet.de: Gesamtverzeichnis und XML je Entscheidung, inkl. **Normenkette** und Leitsatz | mittel | amtlich, kostenlos; Verzeichnisdatei vor Umsetzung prüfen |
| OLG/LG/AG-Entscheidungen | Rechtsprechungsdatenbanken der Länder (z. B. NRWE), openJur | hoch | Entscheidungen sind gemeinfrei (§ 5 UrhG), die **Datenbanken** können aber nach § 87b UrhG geschützt sein → Nutzungsbedingungen je Quelle prüfen 🧑 |
| Unterhaltsleitlinien der OLG, Düsseldorfer Tabelle | Veröffentlichungen der OLG (PDF) | gering | jährlich aktualisieren |
| Gesetzesbegründungen | DIP des Bundestags (offene Schnittstelle) | mittel | Argumente zum Zweck einer Norm |
| Kommentare | beck-online, juris | — | nur mit Lizenz/Kooperation; vorerst nicht |
| Landesrecht | Landesportale | mittel | erst bei Bedarf (Verwaltungsrecht) |

**Ohne Ausbau der Rechtsprechung** kann LexWolf die Normprüfung liefern. Bei der Rechtsprechung
muss es dann ehrlich „bitte selbst recherchieren“ sagen, mit vorgeschlagenen Suchbegriffen und
Normen für juris oder beck-online.

## 4. So soll es sich für den Anwalt anfühlen (ohne die Oberfläche zu überladen)

**Ein einziger neuer Knopf** in der Aktenleiste: **„⚖ Prüfen“**. Dazu kommt der Chat: Erkennt er
Fragen wie „Hat die Klage Aussicht?“, antwortet er kurz und bietet „Ausführliches Gutachten
erstellen“ an.

Der Dialog hat drei Schritte, im Stil der bestehenden Dialoge:

1. **Was soll geprüft werden?** Vier Kacheln:
   - Erfolgsaussichten unserer Klage bzw. unseres Antrags,
   - Besteht ein Anspruch?,
   - Verteidigung gegen Klage oder Antrag der Gegenseite (Dokument aus der Akte wählen),
   - freie Rechtsfrage.

   Darunter eine Zeile für die Fragestellung (diktierbar) und die einbezogenen Dokumente
   (vorausgewählt).
2. **Sachverhalt bestätigen:** LexWolf stellt den Sachverhalt aus der Akte zusammen:
   unstreitig / streitig / Vortrag jeder Seite, jeweils mit [Dn]. Dazu kommen **Lücken und
   Rückfragen** („Wann ist die Kündigung zugegangen?“). Der Anwalt korrigiert, denn das Gutachten
   steht und fällt mit dem Sachverhalt.
3. **Gutachten:**
   - **Oben:** Ergebnis in zwei Sätzen und die Erfolgsaussicht als Ampel mit Begründung.
   - **Links:** der Prüfungsbaum mit Status je Punkt: ✓ erfüllt, ✗ nicht erfüllt,
     ? offen/Beweisfrage. Zum Beispiel:
     - Zulässigkeit: Zuständigkeit, Frist, Form
     - Anspruchsgrundlage → Tatbestandsmerkmale
     - Einwendungen und Einreden
     - Beweislast
   - **Rechts:** der Text im Gutachtenstil (Obersatz, Definition, Subsumtion, Ergebnis).
     - **Normen** mit Wortlaut beim Darüberfahren;
     - **Rechtsprechung** mit Aktenzeichen und Link zur Quelle;
     - **Aktenbelege [Dn]** zum Anklicken;
     - alles mit der bekannten Belegprüfung (✓ / ⚠).
   - **Fußleiste:** In den Schriftsatz übernehmen · Mandantenschreiben (Risiken in einfacher
     Sprache) · Als Word in die Akte.

Gutachten werden in der Akte gespeichert (Datenbank und Word-Datei) und im Aktenstamm aufgelistet.

## 5. Technische Umsetzung

### 5.1 Ablauf im Backend (mehrstufig, „agentisch“)

Ein Gutachten dauert einige Minuten. Deshalb läuft es als **Auftrag mit Fortschrittsanzeige**: Der
Client startet ihn mit `POST /gutachten`, fragt mit `GET /gutachten/{id}` den Stand ab und zeigt die
Schritte an.

1. **Sachverhalt** (`/gutachten/sachverhalt`): Aus den Dokumenten und der Vorgabe des Anwalts
   entsteht ein strukturierter Sachverhalt (JSON: unstreitig/streitig/Vortrag je Partei mit [Dn],
   Lücken, Rückfragen). Der Anwalt bestätigt oder korrigiert ihn.
2. **Prüfungsprogramm:** Die KI bestimmt die Prüfungsreihenfolge (prozessual: Zulässigkeit;
   materiell: Anspruchsgrundlagen in der üblichen Reihenfolge, je Grundlage Tatbestandsmerkmale,
   Einwendungen, Einreden). **Jede genannte Norm wird sofort gegen die Datenbank geprüft.** Was
   nicht existiert, fliegt heraus.
3. **Recherche je Prüfungspunkt:**
   - exakter Normtext, auch einzelne Absätze;
   - verwiesene Normen über den Neo4j-Graphen;
   - Rechtsprechung über die Normenkette (siehe G-1) und die hybride Suche.
4. **Subsumtion je Punkt:** Die KI schreibt nur mit den vorgelegten Quellen (Normtext,
   Entscheidungen, Aktenstellen) und liefert ein strukturiertes Ergebnis. Unsichere Punkte werden
   als „offen“ mit Beweisfrage ausgewiesen, statt sie zu entscheiden.
5. **Gegenprüfung aus Sicht der Gegenseite:** Ein zweiter Durchgang sucht Schwachstellen,
   übersehene Einwendungen, Beweisprobleme und Verjährung. Das ist entscheidend für Teil 3 der
   Anforderung, die Angriffsmöglichkeiten.
6. **Belegprüfung:**
   - Jede Norm wird im Wortlaut verglichen.
   - Jedes Aktenzeichen wird auf Existenz geprüft.
   - Zusätzlich prüft ein Durchgang, **ob die Entscheidung die Aussage überhaupt trägt** (gegen
     Leitsatz und Gründe). Das ist der wichtigste Schutz gegen erfundene oder falsch zitierte
     Rechtsprechung.
7. **Ergebnis:** Erfolgsaussicht mit Begründung, Risiken, Empfehlung und Liste der nötigen Beweise
   bzw. Unterlagen.

Alle KI-Aufrufe laufen über `ask_llm()`. Nötig wird die **Modellwahl je Aufgabe** (Roadmap K-4):
ein starkes Modell für Subsumtion und Gegenprüfung, ein günstigeres für Recherche-Hilfen.

### 5.2 Daten

- Tabelle `gutachten` (lokal und/oder Backend): Mandant, Art, Fragestellung, bestätigter
  Sachverhalt, Ergebnis-JSON, Text, Belege, Zeitpunkt, Modell, Kosten.
- Rechtsprechung in `legal_chunks` mit zusätzlichen Feldern bzw. einer Tabelle
  `entscheidung_norm` (Entscheidung ↔ zitierte Normen). So wird „alle Entscheidungen zu
  § 1578b BGB“ eine schnelle Abfrage.

### 5.3 Datenschutz

Für ein Gutachten geht die **ganze Akte** an die KI. Damit werden Roadmap **K-3**
(Pseudonymisierung) und **K-4** (Anbieter mit AVV, EU) **vor dem Einsatz mit echten Mandaten
zwingend**.

## 6. Arbeitspakete (Reihenfolge)

| ID | Paket | Inhalt | Ergebnis / Abnahme |
|---|---|---|---|
| G-0 | Grundlagen | Tunnel-Wächter für die Entwicklung; ehrliche `/knowledge/stats`; Modellwahl je Aufgabe in `ask_llm`; Kosten je Aufruf protokollieren | Backend bleibt verbunden; echte Bestandszahlen |
| G-1 | Rechtsprechung ausbauen | Bundesgerichte ab 2010 nachladen (rechtsprechung-im-internet.de), **Normenkette** und Leitsatz speichern, Dubletten vermeiden; OLG-Unterhaltsleitlinien; dann Länderquellen nach Prüfung der Nutzungsbedingungen 🧑 | ≥ 30.000 Bundesentscheidungen mit Normenkette; Suche „Entscheidungen zu § X“ |
| G-2 | Norm-Werkzeuge | Normtext per Zitat (inkl. Absatz/Satz), Verweisungen, Fassung zu einem Stichtag | Tests mit 50 Zitaten aus Schriftsätzen |
| G-3 | Sachverhalt | Endpunkt und Dialogschritt 2 | Anwalt kann Sachverhalt bestätigen/korrigieren |
| G-4 | Prüfungs-Engine | Programm, Recherche, Subsumtion, Gegenprüfung, Ergebnis; als Auftrag mit Fortschritt | Strukturiertes Gutachten-JSON; Teilschritte einzeln testbar |
| G-5 | Belegprüfung 2.0 | Norm-Wortlaut, Aktenzeichen, „trägt die Entscheidung die Aussage?“ | Kein ungeprüftes Zitat ohne Warnung |
| G-6 | Oberfläche | Knopf „⚖ Prüfen“, Dialog in drei Schritten, Chat-Angebot, Ablage in der Akte | Bildschirmfotos bei Schrift 13 und 23 |
| G-7 | Einarbeiten | Gutachten → Schriftsatz (Anträge, Begründung, Beweisangebote), → Mandantenschreiben, → Erwiderung (verbunden mit Roadmap P2-3) | Ein Klick vom Gutachten zum Entwurf |
| G-8 | Qualität | **10–20 Musterfälle vom Anwalt** mit seiner Lösung; Bewertungsraster (richtige Anspruchsgrundlagen, Ergebnis, keine erfundenen Zitate); automatische Regressionstests | Messbare Trefferquote vor jeder Änderung an Prompts |

Empfohlene Reihenfolge: **G-0 → G-2 → G-3 → G-4 → G-5 → G-6 → G-7**, parallel **G-1** (läuft
lange) und **G-8** (Musterfälle sammeln, sobald möglich). Ein erster nutzbarer Stand (Normprüfung
ohne breite Rechtsprechung) ist nach G-6 erreicht.

## 6a. KI-Kosten (Schätzung, 19.10.2026)

Listenpreise der Anthropic-API (USD je 1 Mio. Tokens, Eingabe / Ausgabe / Eingabe aus Cache):
Claude Opus 5.5 4 / 20 / 0,20 · Claude Fable 5.1 10 / 50 / 0,25 · Claude Sonnet 5.5 2 / 10 / 0,20 ·
Claude Haiku 5.5 0,10 / 0,50. Das Denken der Modelle zählt als Ausgabe und macht den Großteil
der Kosten aus.

Annahme für ein typisches Gutachten:
- **Aufrufe:** Sachverhalt, Prüfungsprogramm, ca. 8 Prüfungspunkte, Gegenprüfung, Synthese und
  ca. 10 kurze Belegprüfungen.
- **Tokens:** rund 375.000 Eingabe-Tokens (davon ca. 60 % aus dem Cache, weil Akte und Sachverhalt
  wiederverwendet werden) und rund 115.000 Ausgabe-Tokens einschließlich Denken.

| Variante | ca. Kosten je Gutachten |
|---|---|
| Sonnet 5.5 durchgehend | 1,50 $ |
| **Opus 5.5 (Empfehlung für sehr gute Qualität), Belegprüfung mit Sonnet/Haiku** | **3–6 $** |
| Fable 5.1 für Subsumtion und Gegenprüfung, Rest Opus 5.5 | 5–10 $ |
| Fable 5.1 durchgehend | 8–15 $ |

Spannweite je nach Aktenumfang und Denktiefe. Die echten Werte misst G-0 (Kostenprotokoll).
Welche Modellstufe sich lohnt, entscheiden die Musterfälle (G-8). Läuft die KI über einen
Cloud-Partner in der EU (K-4), gelten dessen Preise.

## 7. Offene Fragen an den Anwalt 🧑

1. **Rechtsgebiete zuerst:** nur Familienrecht oder auch Arbeits-, Miet-, allgemeines Zivilrecht?
2. **Musterfälle:** Kann er 10–20 abgeschlossene, anonymisierte Fälle mit seiner Einschätzung
   bereitstellen? Ohne sie ist die Qualität nicht messbar.
3. **Form:** Gutachtenstil intern und Urteilsstil im Schriftsatz, oder beides wählbar?
4. **Erfolgsaussicht:** Ist die Ampel (hoch / offen / gering) mit Begründung so recht?
5. **Rechtsprechungsquellen:** Hat er Zugang zu juris oder beck-online? (Eine Anbindung wäre nur
   über eine Kooperation möglich; vorerst liefert LexWolf dafür Suchbegriffe.)
6. **Kosten:** Ein Gutachten braucht 10–20 Aufrufe eines starken Modells. Welcher Preis je
   Gutachten ist für die Kanzlei akzeptabel?
