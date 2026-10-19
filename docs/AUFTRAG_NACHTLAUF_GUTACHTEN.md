# Auftrag für den autonomen Nachtlauf: Phase G „Gutachtliche Prüfung“

> **Wie benutzen:** Diesen ganzen Text dem Coding-Agenten auf dem Server als Auftrag geben
> (z. B. „Lies docs/AUFTRAG_NACHTLAUF_GUTACHTEN.md und arbeite ihn ab.“). Er ist für mehrere
> Nächte ausgelegt: Jede Nacht macht dort weiter, wo die letzte aufgehört hat
> (Fortschritt in `docs/NACHTLAUF_LOG.md`).

---

## 1. Deine Rolle und dein Ziel

Du bist ein erfahrener Softwareentwickler und arbeitest **allein und ohne Rückfragen** an LexWolf,
einem KI-Assistenten für Anwaltskanzleien (zuerst Familienrecht): Windows-Desktop-App (WPF, .NET 10)
in `desktop/` und FastAPI-Backend in `backend/`.

**Ziel:** Phase G aus `docs/KONZEPT_GUTACHTEN.md` umsetzen. LexWolf soll Rechtsfälle gutachtlich
prüfen: Erfolgsaussichten, Anspruch ja/nein, Einwände gegen eine Klage der Gegenseite, anhand der
Normen, mit Rechtsprechung, und das Ergebnis in den Fall einarbeiten (Schriftsatz,
Mandantenschreiben).

Niemand ist erreichbar. Du entscheidest selbst, dokumentierst jede wichtige Entscheidung und
arbeitest so, dass ein Mensch morgens in 10 Minuten versteht, was passiert ist, und alles mit einem
Klick verwerfen kann.

## 2. Vor dem Start (jede Nacht)

1. Lies in dieser Reihenfolge: `CLAUDE.md`, `docs/KONZEPT_GUTACHTEN.md`, `docs/ROADMAP.md`
   (Phase G), `docs/NACHTLAUF_LOG.md` (falls vorhanden).
2. Arbeite **ausschließlich auf dem Branch `feature/gutachten`**. Gibt es ihn nicht, lege ihn von
   `main` an. Ist er hinter `main`, merge `main` hinein; bei Konflikten nichts erzwingen, sondern
   im Log notieren und mit dem nächsten Paket weitermachen, das nicht betroffen ist.
3. Prüfe, dass die Tests vor deiner Arbeit grün sind:
   - `python -m pytest tests/unit -q`
   - `dotnet test desktop/LexWolf.Tests/LexWolf.Tests.csproj`
   - `dotnet build desktop/LexWolf.csproj -p:EnableWindowsTargeting=true`
     (WPF lässt sich unter Linux bauen, aber nicht starten.)

   Sind sie schon vorher rot, notiere das im Log und repariere es nur, wenn es klein und eindeutig ist.

## 3. Feste Regeln (niemals brechen)

1. **Kein Deployment, kein Eingriff in den laufenden Betrieb.** Nicht den Dienst
   `lexwolf-backend` neu starten, keine Umgebungsvariablen des Dienstes ändern, nichts nach `main`
   pushen oder mergen. Nur Commits auf `feature/gutachten` (und Push dieses Branches, falls
   eingerichtet).
2. **Produktionsdatenbank nur additiv.** Neue Tabellen und neue Zeilen sind erlaubt, wenn sie über
   ein Migrationsskript in `backend/scripts/` angelegt werden, das idempotent ist und ein
   Rückbauskript hat. **Nie** bestehende Zeilen löschen oder ändern, nie `DROP`/`TRUNCATE`/`DELETE`
   auf bestehende Tabellen. Neue Rechtsprechung kennzeichnest du mit einer Quelle
   (`source`/`tags`), damit sie gezielt wieder entfernt werden kann.
3. **Alle KI-Aufrufe in LexWolf nur über `ask_llm()`** in `backend/services/llm_client.py`.
   Kein Anbieter-SDK woanders importieren.
4. **Fristen und Geldbeträge rechnet nie die KI** (siehe `CLAUDE.md`).
5. **Keine erfundenen Rechtsquellen.** Normen, Aktenzeichen und Fundstellen nur aus der Datenbank
   bzw. den Dokumenten der Akte. Was nicht belegbar ist, wird als „nicht geprüft“ markiert, nie
   als Tatsache ausgegeben. Kommentare (Grüneberg, MüKo) werden nie zitiert; stattdessen
   „im Kommentar nachschlagen“ mit Suchbegriffen.
6. **KI-Kosten sparsam:** In Tests wird `ask_llm` gemockt. Echte KI-Aufrufe nur für kurze
   Stichproben, **höchstens 30 echte Aufrufe pro Nacht**. Zähle sie im Log mit.
7. **Crawlen höflich und legal:** Nur Quellen, deren Nutzung eindeutig erlaubt ist (amtliche
   Seiten des Bundes: rechtsprechung-im-internet.de, gesetze-im-internet.de, DIP des Bundestags).
   Höchstens 1 Anfrage pro Sekunde, eigener User-Agent „LexWolf-Crawler (Kontakt: …)“, Abbruch
   bei Fehlerhäufung, robots.txt beachten. **Länderdatenbanken, openJur, beck-online, juris:
   nicht anfassen** (Nutzungsbedingungen ungeklärt bzw. verboten).
8. **Keine Mandantendaten** in Logs, Tests oder Beispielen. Beispielfälle sind erfunden und als
   solche gekennzeichnet.
9. **Oberfläche:** Bestehendes Design-System nutzen (Styles in `desktop/App.xaml`: `LwKarte`,
   `LwAbschnitt`, `LwSchritt`, `LwHinweis`, `LwFussleiste`, `LwEditor`, Herkunftsfarben).
   **Keine festen Schriftgrößen**, nur `{DynamicResource LwSchrift…}`. Neue Dialoge rufen
   `FensterGroesse.AnSchriftAnpassen(this)` auf und kommen in den Test
   `SchriftgroessenTests.Ueberarbeitete_Dialoge_haben_keine_festen_Schriftgroessen`.
   Texte kurz und auf Deutsch, verständlich für Anwälte.
10. **Code-Stil wie im Bestand:** deutsche Bezeichner, Logik in testbaren Klassen ohne WPF
    (`desktop/Services/`), neue Dateien bei Bedarf in `LexWolf.Tests.csproj` einbinden.

## 4. Arbeitsweise pro Paket

Für jedes Arbeitspaket:

1. Im Log notieren: Start, Ziel, geplante Dateien.
2. Bestehenden Code lesen, bevor du etwas neu baust. Vorhandenes wiederverwenden: Suche
   (`services/search_service.py`), Paragraphensuche (`api/ask.py: _direct_paragraph_search`),
   Verweisungsgraph (`services/neo4j_service.py`), Belegprüfung (`api/akte.py: pruefe_belege`),
   Auftragsmuster, Dialog-Muster (`desktop/Dialogs/SachstandDialog.xaml`).
3. Umsetzen in kleinen Schritten, Tests dazu schreiben (Backend: `tests/unit/`, Desktop:
   `desktop/LexWolf.Tests/`).
4. Alle drei Prüfbefehle aus Abschnitt 2 müssen grün sein.
5. Commit auf `feature/gutachten`. Nachricht: `G-x: <was>`, am Ende
   `Co-Authored-By: <Agent>`.
6. Im Log: Ergebnis, Abweichungen vom Konzept mit Begründung, offene Punkte. In `docs/ROADMAP.md`
   das Paket abhaken bzw. Teilfortschritt vermerken.

**Wenn du feststeckst** (mehr als ca. 45 Minuten ohne Fortschritt, fehlendes Material, rechtliche
Unklarheit): Problem im Log beschreiben, unter „Fragen an den Nutzer“ eintragen, Zwischenstand
committen, falls er die Tests nicht bricht, und mit dem nächsten Paket weitermachen.

## 5. Die Pakete in dieser Reihenfolge

### G-0 Grundlagen
- **Modellwahl je Aufgabe:** `ask_llm(..., aufgabe="gutachten_subsumtion")`. Zuordnung Aufgabe →
  Modell über Umgebungsvariablen mit Vorgabe (z. B. `LLM_MODELL_GUTACHTEN`, `LLM_MODELL_EINFACH`).
  Ohne Angabe bleibt alles wie bisher. Abwärtskompatibel!
- **Kostenprotokoll:** Je Aufruf Anbieter, Modell, Aufgabe, Ein-/Ausgabe-Tokens (soweit der
  Anbieter sie liefert), Dauer. Neue Tabelle `llm_aufruf` (additiv) oder Logdatei. Keine Inhalte
  speichern.
- **Echte Bestandszahlen:** `/knowledge/stats` liefert heute Platzhalterzahlen. Ersetze sie durch
  echte Zählungen (Gesetze nach `tags`, Entscheidungen nach `court`, Datumsbereich), gecacht.
- Tests für alles.

### G-2 Norm-Werkzeuge (vor G-1, weil G-1 sie nutzt)
- Modul `backend/services/normen.py`:
  - `norm_aus_zitat("§ 1578b Abs. 1 S. 2 BGB")` → Gesetz, Paragraph, Absatz, Satz;
    robust gegen „§§“, „Art.“, „i.V.m.“, „Abs.“/„Absatz“.
  - `normtext(gesetz, paragraph, absatz=None)` → exakter Text aus `legal_chunks`. Beachte die zwei
    Titelformate (siehe `pruefe_belege`) und ggf. `valid_from`/`valid_until`.
  - `verweise(gesetz, paragraph)` → verwiesene Normen (Neo4j, sonst Regex auf „§ … “ im Text).
- Endpunkt `GET /normen/text?zitat=...`.
- **Test mit mindestens 50 echten Zitaten** aus Familien-, Zivil- und Prozessrecht
  (Liste in `tests/unit/daten/zitate.txt`). Ziel ≥ 95 % gefunden.

### G-1 Rechtsprechung ausbauen (läuft über mehrere Nächte, nebenher)
- Prüfe zuerst, ob rechtsprechung-im-internet.de ein Gesamtverzeichnis (z. B. `rii-toc.xml`) mit
  Download je Entscheidung bietet. Dokumentiere das Ergebnis im Log. **Nicht raten.**
- Crawler `backend/crawlers/rii_backfill.py`: Bundesgerichte, rückwärts ab heute bis 2010,
  **fortsetzbar** (Checkpoint-Datei), **Dubletten vermeiden** (über `chunk_hash` bzw.
  Aktenzeichen und Gericht), pro Nacht höchstens ca. 3.000 Entscheidungen.
- **Normenkette speichern:** neue Tabelle `entscheidung_norm (case_number, court, date, gesetz,
  paragraph)`; Endpunkt `GET /rechtsprechung/zu-norm?zitat=§ 1578b BGB` → neueste zuerst, mit
  Leitsatz und Link zur Quelle.
- Leitsatz getrennt speichern (eigenes Feld bzw. eigener Chunk), weil er für die Belegprüfung
  wichtig ist.
- Embeddings über den bestehenden `embedding_service` (Kosten im Log).
- Unterhaltsleitlinien der OLG und Düsseldorfer Tabelle: nur Download-Liste vorbereiten
  (Quellen-URLs im Log). Einlesen erst nach Freigabe durch den Nutzer.

### G-3 Sachverhalt aus der Akte
- `POST /gutachten/sachverhalt`. Eingabe: Fragestellung, Art (`erfolgsaussicht` | `anspruch` |
  `verteidigung` | `frage`), Dokumente (Titel, Text), Aktenstamm.
- Ausgabe (JSON):
  `{"unstreitig":[{"text","belege":["D2"]}], "streitig":[{"text","vortrag_mandant","vortrag_gegner","belege"}], "luecken":["…"], "rueckfragen":["…"]}`.
- Belege [Dn] werden gegen die Dokumentenanzahl geprüft (vorhandene Logik nutzen).
- Tests mit gemocktem `ask_llm`.

### G-4 Prüfungs-Engine (Herzstück)
- Als **Auftrag**: `POST /gutachten` → `{id}`; `GET /gutachten/{id}` → Status, aktueller Schritt,
  Fortschritt 0–100, Ergebnis. Speicherung in neuer Tabelle `gutachten_auftrag`. Lange Läufe in
  einem Hintergrund-Task (asyncio), blockierende Aufrufe per `asyncio.to_thread` (siehe
  Kommentar in `api/chat.py`).
- Schritte:
  1. **Prüfungsprogramm** (Liste der Prüfungspunkte: Zulässigkeit; Anspruchsgrundlagen →
     Tatbestandsmerkmale; Einwendungen und Einreden; Beweislast). **Jede Norm sofort über G-2
     prüfen**, nicht existierende verwerfen und im Ergebnis vermerken.
  2. **Recherche je Punkt:** Normtext, Verweise, Rechtsprechung (G-1-Endpunkt und hybride Suche).
  3. **Subsumtion je Punkt** nur mit den vorgelegten Quellen
     (vgl. `react_engine.GROUNDED_SYSTEM_PROMPT`).
     Ergebnis: `{"punkt","norm","obersatz","definition","subsumtion","ergebnis":"erfuellt|nicht_erfuellt|offen","beweisfrage","quellen":[{"art":"norm|urteil|akte","zitat"}]}`.
  4. **Gegenprüfung aus Sicht der Gegenseite:** übersehene Einwendungen, Beweisprobleme,
     Verjährung, prozessuale Hürden.
  5. **Synthese:** `{"ergebnis_kurz","erfolgsaussicht":"hoch|offen|gering","begruendung","risiken":[],"empfehlung","fehlende_beweise":[],"kommentar_nachschlagen":[{"norm","suchbegriffe"}]}`.
     **Keine Prozentzahlen.**
- Prompts in eigene Datei `backend/api/gutachten_prompts.py` (testbar ohne schwere Imports, wie
  `chat_prompt.py`). Deutsch, Gutachtenstil.
- Jeder Schritt einzeln testbar; End-to-End-Test mit gemocktem `ask_llm`.
- **Eine** echte Stichprobe mit einem erfundenen, einfachen Fall (z. B. Kaufpreisanspruch
  § 433 Abs. 2 BGB mit Einrede der Verjährung). Ergebnis und Kosten ins Log.

### G-5 Belegprüfung 2.0
- Normen: Existenz und Wortlaut gegen die Datenbank.
- Aktenzeichen: Existenz in der Datenbank.
- **„Trägt die Entscheidung die Aussage?“**: kurzer KI-Aufruf (günstiges Modell) mit Leitsatz und
  relevanter Passage gegen die Aussage im Gutachten → `traegt | traegt_teilweise | traegt_nicht |
  nicht_pruefbar`.
- Ergebnis je Zitat im Gutachten-JSON. Was nicht `traegt` ist, wird in der Oberfläche markiert.

### G-6 Oberfläche (Desktop)
- **Ein** neuer Knopf **„⚖ Prüfen“** in der Aktenleiste (`MainWindow.xaml`), sonst nichts in der
  Hauptoberfläche.
- Dialog `desktop/Dialogs/GutachtenDialog.xaml` in drei Schritten (Konzept Abschnitt 4):
  1. Art (vier Kacheln), Fragestellung, Dokumente (vorausgewählt; bei „Verteidigung“ das Dokument
     der Gegenseite wählen).
  2. Sachverhalt bestätigen (bearbeitbare Liste: unstreitig/streitig, Lücken, Rückfragen).
  3. Gutachten: oben Ergebnis und Ampel, links Prüfungsbaum mit ✓/✗/?, rechts Text mit
     klickbaren Belegen und Belegprüfung; Fußleiste: „In den Schriftsatz“, „Mandantenschreiben“,
     „Als Word in die Akte“.
- Fortschrittsanzeige während des Auftrags (Abfrage alle 2–3 s), abbrechbar.
- Lokale Speicherung: Tabelle `gutachten` in `LocalDb` (Mandant, Art, Frage, JSON, Datum) **und**
  Aufnahme in `MandantTabellen` (Löschen eines Mandanten!) plus Test.
- Liste der Gutachten im Aktenstamm.
- Logik (JSON lesen, Baum aufbauen, Ampel bestimmen) in `desktop/Services/Gutachten.cs` mit Tests.
- Da du die App nicht starten kannst: **Checkliste für die Sichtprüfung** in den Morgenbericht.

### G-7 Einarbeiten
- „In den Schriftsatz“: Gutachten als Vorgabe an den bestehenden Schriftsatz-Weg
  (`/akte/schriftsatz`): Anträge, Begründung im Urteilsstil, Beweisangebote aus `fehlende_beweise`.
- „Mandantenschreiben“: Risiken und Empfehlung in einfacher Sprache über den bestehenden
  Sachstand-Weg.
- Bei „Verteidigung“: Verknüpfung mit Roadmap P2-3 (Erwiderung Punkt für Punkt) vorbereiten.

### G-8 Qualität
- Testgerüst `tests/quality/gutachten/` mit Fällen als JSON (Sachverhalt, Frage, erwartete
  Anspruchsgrundlagen, erwartetes Ergebnis) und Bewertungsraster: richtige Normen gefunden,
  Ergebnis stimmt, keine erfundenen Zitate, Kosten.
- **3 erfundene Beispielfälle** anlegen, klar als Platzhalter markiert. Die echten Musterfälle
  liefert der Anwalt später (im Log unter „Fragen an den Nutzer“ erinnern).
- Skript, das alle Fälle gegen die Engine laufen lässt und eine Tabelle ausgibt (nur bei
  ausdrücklichem Aufruf echte KI, sonst Mock).

## 6. Morgenbericht (am Ende jeder Nacht)

Oben in `docs/NACHTLAUF_LOG.md` einen Abschnitt mit Datum anlegen:

1. **In einem Satz:** Was ist heute Nacht fertig geworden?
2. Erledigte Pakete mit Commit-Hashes.
3. Teststand: Anzahl grün/rot für alle drei Prüfbefehle.
4. Echte KI-Aufrufe: Anzahl, geschätzte Kosten.
5. Datenbank: neue Tabellen, neue Zeilen (Anzahl je Quelle), Rückbauskript.
6. **Fragen an den Nutzer** (nummeriert, je eine Zeile, was du von ihm brauchst).
7. **Sichtprüfung auf Windows** (für UI-Pakete): konkrete Klickfolge zum Ausprobieren.
8. Nächster geplanter Schritt.

Kurz und ehrlich. Was nicht getestet werden konnte, steht ausdrücklich da.
