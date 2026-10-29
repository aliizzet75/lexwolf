# LexWolf – Roadmap zum marktreifen Kanzlei-Assistenten

Stand: 18.10.2026. Dieser Plan wird von Claude Code **Aufgabe für Aufgabe** abgearbeitet.
Jede Aufgabe hat eine ID (z. B. `P1-2`). Erledigte Aufgaben werden hier abgehakt (`[x]`) und
mit Datum und Commit-Hash versehen.

---

## So arbeitet Claude Code diesen Plan ab

1. Zuerst `CLAUDE.md` lesen (Build, Tests, unverrückbare Regeln).
2. Die **erste offene Aufgabe** in Reihenfolge nehmen. Nur dann springen, wenn der Nutzer es
   sagt oder die Aufgabe als `BLOCKIERT` markiert ist (Grund dazuschreiben). Die Aufgaben `K-…`
   ganz unten werden übersprungen, bis der Nutzer die Kundentestphase ausruft.
3. Pro Aufgabe: Code lesen → umsetzen → Tests schreiben → `dotnet test` (Desktop) bzw.
   `python -m pytest tests/unit` (Backend) → App bauen (`dotnet build desktop/LexWolf.csproj`).
4. Die Akzeptanzkriterien der Aufgabe einzeln prüfen. Was nicht prüfbar war, ausdrücklich
   melden – nicht als erledigt abhaken.
5. Checkbox hier abhaken, einen Satz zum Ergebnis dazuschreiben. Committen nur, wenn der
   Nutzer das für die Sitzung freigegeben hat.
6. **Nie** auf den Server (IONOS, 212.227.180.66) deployen, ohne ausdrückliche Anweisung.
7. Aufgaben mit 🧑 brauchen eine Entscheidung oder Material vom Nutzer (Konten, Verträge,
   Beispieldateien). Dann die Frage stellen, die Aufgabe als `BLOCKIERT` markieren und mit der
   nächsten weitermachen.

---

## Positionierung (gilt für alle Entscheidungen)

**LexWolf ist die KI-Kanzleikraft für kleine Kanzleien (1–10 Berufsträger), zuerst im
Familienrecht.** Die Akte liest sich selbst: Eingänge werden ausgewertet, Fristen vorgeschlagen,
Antworten, Schriftsätze, Sachstandsmitteilungen und Berechnungen entworfen. Der Anwalt prüft und
entscheidet, die Fleißarbeit macht LexWolf.

Ein Anwalt will LexWolf nicht mehr hergeben, wenn es
1. **jeden Morgen Zeit spart**: Tagesüberblick, Eingänge sind schon gelesen, Antworten liegen im Entwurf;
2. **Haftungsangst nimmt**: keine verpasste Frist, keine erfundenen Zitate, Rechenweg immer sichtbar;
3. **mit seinem Briefkopf, seinem Stil und seinen Ordnern** arbeitet, ohne Umgewöhnung;
4. **berufsrechtlich unbedenklich** ist (§ 203 StGB, § 43e BRAO, DSGVO) und er das seinen Mandanten
   und der Kammer gegenüber belegen kann;
5. **Geld bringt**: Gebühren werden nicht vergessen, Vorschüsse rechtzeitig angefordert.

Was keinem dieser fünf Punkte dient, wird nicht gebaut.

**KI macht:** Lesen, Zusammenfassen, Entwerfen, Extrahieren, Zuordnen, Vorschlagen.
**Feste Programmlogik macht:** Fristen berechnen, Geld rechnen, Tabellenwerte, Zuständigkeiten.
Diese Trennung bleibt.

---

## Übersicht

| Block | Inhalt | Wann |
|---|---|---|
| **Phase 1** | Schriftgröße überall, Einrichtungsassistent, Briefbogen, aufgeräumte Oberfläche, Nutzungsmessung | jetzt |
| **Phase G** | Gutachtliche Prüfung: Erfolgsaussichten, Anspruch, Einwände, Rechtsprechung (Konzept: `docs/KONZEPT_GUTACHTEN.md`) | **als Nächstes** |
| **Phase 2** | Fristen haftungsfest, beA-Eingang, Erwiderung auf Schriftsätze, Fragen an die Akte, VKH-Antrag, Rechnungen, Fremdsprachen, Vermerke, Wochen-Check | danach |
| **Phase 2b** | Premium-Rechtsquellen (ohne kostenpflichtige Verlagslizenzen): Bundesgerichte, OLG-Leitlinien, Begründungen, Kanzlei-Wissen | vor dem Verkauf |
| **Phase 3** | Bezahlung, Preise, mehrere Arbeitsplätze | vor dem Verkauf |
| **Phase 4** | Word-Add-in, Kanzleisoftware, weitere Rechtsgebiete | nach 20 zahlenden Kanzleien |
| **Aufräumen** | Altlasten im Repo, ungenutzte Endpunkte, CI | nebenbei |
| **K (ganz unten)** | Verschlüsselung, Lizenzschlüssel, Pseudonymisierung, KI-Anbieter, Verträge, Signatur | **erst zur Kundentestphase** |

---

## Bereits erledigt (18.10.2026)

- [x] **Q-1 Weitere Fristarten**: NZB (§ 544 ZPO), Tatbestandsberichtigung (§ 320 ZPO),
  Wiedereinsetzung (§ 234 ZPO), ArbGG (Berufung, Begründung, Einspruch VU **1 Woche**), SGG
  (Widerspruch, Klage, Berufung), VwGO-Zulassungsantrag und -begründung, FGO-Klage, FamFG-Beschwerde
  im Eilverfahren (2 Wochen). Client (`FristRechner.cs`) und KI-Liste (`backend/api/akte.py`)
  sind synchron. Ein Test bricht, sobald eine Fristart nur auf einer Seite steht.
- [x] **Q-2 Zitatprüfung im Chat**: Normen und Aktenzeichen in Chat-Antworten werden gegen die
  Datenbank geprüft (wie bisher nur beim Schriftsatz) und unter der Antwort als ✓, ○ oder ⚠
  markiert. Der Prüfvermerk landet nie in Word-Dokumenten oder E-Mail-Entwürfen. Der
  System-Prompt verbietet erfundene Aktenzeichen.
- [x] **Q-3 Fristenliste drucken**: 🖨-Knopf im Fristen-Reiter erzeugt eine druckbare Liste der
  nächsten 14 Tage mit Spalte zum Abzeichnen und Warnung bei noch ungeprüften Fristvorschlägen
  (für die tägliche Fristenkontrolle im Büro).
- [x] **Q-4 Sicherheitslücke**: verwundbare SQLite-Nativbibliothek (GHSA-2m69-gcr7-jv3q) auf
  2.1.14 angehoben. `dotnet list package --vulnerable` meldet für die App nichts mehr.

---

## Phase 1 – In 10 Minuten zum ersten Aha-Erlebnis (Aktivierung)

### [ ] P1-0 Eine Schriftgröße für die ganze App, und kurze Texte
> **Teilweise erledigt (18.10.2026):** Schriftstufen `LwSchriftKlein/Normal/Gross/Titel` in
> `App.xaml`, gesetzt über `App.SetzeSchriftgroesse()` aus der Chat-Schriftgröße (Start, A+/A−,
> Einstellungen). Die gemeinsamen Styles (Knöpfe, Textfelder, Auswahlfelder, Häkchen, Reiter) folgen
> der Größe. Neu: dunkle, mitwachsende Häkchen und Optionsfelder, dunkle Bildlaufleisten, Bausteine
> `LwKarte`, `LwAbschnitt`, `LwSchritt`, `LwHinweis`, `LwFussleiste`, `LwEditor`. **Aktenstamm,
> Schriftsatz, Rechner, Sachstand und Fragebogen** sind komplett neu gestaltet (Kopf, nummerierte
> Schritte, Karten, Fußleiste) und wachsen mit der Schrift (`FensterGroesse.AnSchriftAnpassen`). Die
> Aktenleiste im Hauptfenster hat normale Knöpfe. **Offen:** Fristen- und Eingang-Reiter,
> Hauptfenster (Kopfzeile, Dateibaum), übrige Dialoge (Einstellungen, Antwort, Frist, Postfach,
> Notizen, Feature-Wunsch, Info, Zusammenfassung), eigene Einstellung „Schriftgröße“ mit Regler.
> Vorbild für den Aufbau ist `Dialogs/SachstandDialog.xaml`. Zum Prüfen hat sich ein kleines
> Vorschauprogramm bewährt, das die Dialoge mit Beispieldaten öffnet und per `RenderTargetBitmap`
> als PNG speichert (bei 13, 18 und 23). Es lohnt sich, das als `tools/UiVorschau` ins Repo zu nehmen.

- **Warum:** Heute gilt die eingestellte Schriftgröße (A+/A−, Strg+Plus/Minus, Einstellungen) nur
  im Chat. Fristen, Eingang, Aktenleiste und alle Dialoge haben feste Größen (rund 200 Stellen
  `FontSize="11"` usw. in den XAML-Dateien). Wer größere Schrift braucht, hat davon nichts.
- **Schritte:**
  1. `AppSettings`: neue Einstellung `Schriftgroesse` (Grundgröße, Standard 13, erlaubt 10–24).
     Bestehende Einstellungen beim ersten Start übernehmen (`ChatOutputFontSize` als Startwert).
     Chat-Ein- und -Ausgabe folgen der Grundgröße. Eine eigene Chat-Größe bleibt nur als Option
     „Chat abweichend“ erhalten.
  2. In `App.xaml` vier Größen als Ressourcen: `LwSchriftKlein` (Grundgröße − 2),
     `LwSchriftNormal` (Grundgröße), `LwSchriftGross` (+ 3), `LwSchriftTitel` (+ 6). Eine Methode
     `App.SetzeSchriftgroesse(double)` schreibt sie neu. Durch `DynamicResource` ändert sich alles
     sofort, ohne Neustart.
  3. Alle festen `FontSize`-Werte in `MainWindow.xaml`, `App.xaml` (Styles), `Controls/` und
     `Dialogs/` ersetzen: 9–11 → Klein, 12–14 → Normal, 15–17 → Groß, ab 18 → Titel. Im Code
     erzeugte Elemente (z. B. `FontSize = 11` in `MainWindow.xaml.cs`) über
     `SetResourceReference(FontSizeProperty, "LwSchriftKlein")`. **Ausnahmen** (bleiben fest):
     Logo-Schriftzug, reine Symbol-Knöpfe, wenn sie sonst die Kopfzeile sprengen.
  4. A+/A− und Strg+Plus/Minus ändern die Grundgröße, also die ganze App. Die Einstellungen
     bekommen einen Regler mit Vorschau.
  5. Dialoge mit fester `Width`/`Height` auf `MinWidth`/`MinHeight` umstellen, mit
     `ResizeMode="CanResize"` und `ScrollViewer`, damit bei großer Schrift nichts abgeschnitten wird.
  6. Bei Größe 20 alle Dialoge öffnen und prüfen: kein abgeschnittener Text, keine überlappenden
     Knöpfe. Screenshots bei 13 und 20 in den PR.
- **Textregeln für die Oberfläche** (auch in `CLAUDE.md`, gelten für alle späteren Aufgaben):
  - Beschriftungen, Knöpfe, Hinweise: **kurz**, höchstens eine Zeile. Was etwas tut, steht im
    Knopf („Frist übernehmen“), nicht in einer Erklärung daneben.
  - Erklärungen gehören in den Tooltip oder hinter „Mehr …“, nicht in die Fläche.
  - **Ausführlich bleibt**, was der Anwalt prüfen muss: Rechenwege, Belege, Zitatprüfung, Entwürfe.
  - Fehlermeldungen: ein Satz, was passiert ist, und ein Satz, was zu tun ist.
- **Akzeptanz:** Ein Test in `LexWolf.Tests` liest alle `.xaml`-Dateien und schlägt fehl, wenn noch
  `FontSize="<Zahl>"` vorkommt (bis auf eine kleine Ausnahmeliste im Test). Test für die Grenzen
  10–24 und die Übernahme der alten Einstellung.

### [x] P1-0b Neues Mandat anlegen (18.10.2026)
Knopf „＋ Neues Mandat“ in der Kopfzeile (`Dialogs/NeuesMandatDialog`, Logik in
`Services/MandatAufnahme.cs`). Ablauf:
1. Mandant mit Kontaktdaten erfassen; Hinweis, wenn es den Mandanten schon gibt.
2. Gegenseite erfassen; die Kollisionsprüfung läuft live, „Anlegen“ ist erst nach Bestätigung möglich.
3. Rechtsgebiet und Anliegen.
4. Laufende Frist, sofort berechnet und als offene Frist eingetragen.
5. Ordner, Aktenstamm, Aktenvermerk (mit dokumentierter Kollisionsprüfung) und Vollmacht anlegen.
6. Der KI-Auftrag im Chat liefert Ersteinschätzung, Fragen, Unterlagenliste, Risiken, nächste
   Schritte und eine Mandatsbestätigung als E-Mail-Entwurf.

Die E-Mail-Adresse wird gespeichert und im Sachstand als Empfänger verwendet.
**Später:** mehrere Akten pro Mandant (z. B. Scheidung und Unterhalt getrennt), Fragebogen
direkt beim Anlegen verschicken, Vorschussrechnung (P2-6) als weitere Option.

### [x] P1-0c Dokumente und E-Mails in die Akte (18.10.2026)
Knopf „📎 Dokumente“ in der Aktenleiste, Karte „Unterlagen“ in „Neues Mandat“, Drag-and-drop aufs
ganze Hauptfenster (aus dem Explorer und direkt aus Outlook). Ohne gewählten Mandanten wird
angeboten, ein neues Mandat mit den Dokumenten anzulegen.
- Dateien werden kopiert. E-Mails (.eml, aus Outlook .msg über `MsgKonverter`) landen wie beim
  Postfach unter „E-Mails“, Anhänge als eigene Dateien.
- Noch nicht zugeordnete Postfach-Mails lassen sich anhaken und werden der Akte zugeordnet.
- Danach wird sofort eingelesen (Chat und Schriftsatz kennen die Dokumente) und ausgewertet
  (Fristvorschläge).
- Der WebView-Chat nimmt keine Drops mehr an (vorher hätte eine hineingezogene PDF den Chat ersetzt).

**Offen:** Drag aus dem „neuen Outlook“ (Web-Version) und Outlook im Praxistest prüfen. Bilder
(Fotos, Scans als JPG) per OCR lesbar machen; `OcrService` gibt es schon für PDFs.

### [x] P1-0d Formulare füllen sich aus der Akte (18.10.2026)
Fragebogen und Rechner werden aus den Dokumenten der Akte gefüllt (Backend
`/akte/formular-ausfuellen`, mit Fundstelle je Feld). Tabelle `formular_feld` speichert je Feld den
Wert von LexWolf (`ki_wert`, `ki_quelle`) und die Eingabe des Anwalts (`anwalt_wert`; `''` = bewusst
geleert) getrennt. Logik in `Services/Formularwerte.cs`.
- Herkunftsfarben mit Legende: Bernstein = von LexWolf (bitte prüfen), Blau = vom Anwalt. Der
  Tooltip zeigt Dokument und Textstelle.
- Neues Auslesen nur, wenn sich die Dokumente geändert haben (Fingerabdruck in `app_meta`).
  Eingaben des Anwalts werden nie überschrieben.
- „✨ LexWolf ausfüllen“ füllt geleerte Felder wieder, per Rechtsklick auch einzelne Felder.
- Der Rechner übernimmt Werte und Herkunft aus dem Fragebogen; eigene Änderungen im Rechner werden
  als Formular „rechner“ gespeichert.
- Behoben: „4.250“ wurde als 4,25 € gelesen (deutscher Tausenderpunkt), jetzt `Formularwerte.Betrag`.
- Auswahl im Dateibaum wählt den Mandanten oben mit aus.

**Offen:** Kinderzeilen einzeln markieren (heute nur ein Hinweis an der Überschrift), Herkunft auch
für Aktenstamm (Gericht, Aktenzeichen, Gegner) mit derselben Technik.

### [x] P1-0e Mandant löschen, Liste und Dateibaum (18.10.2026)
- „🗑 Mandant löschen …“ im Aktenstamm (unten links) und per Rechtsklick auf den Mandantenordner.
  Die Sicherheitsabfrage (`Dialogs/MandantLoeschenDialog`) zeigt den Umfang, warnt vor offenen
  Fristen, nennt § 50 Abs. 1 BRAO und verlangt den Namen zur Bestätigung.
- Der Aktenordner kommt in den Papierkorb. Auf Netzlaufwerken wird endgültig gelöscht, das sagt der
  Dialog ausdrücklich. Scheitert der Ordner (Datei geöffnet), wird gar nichts gelöscht.
- `LocalDb.DeleteMandantKomplett` löscht in einer Transaktion aus allen Tabellen mit `mandant_id`.
  Ein Test prüft, dass keine Tabelle Reste behält. **Neue Tabellen mit `mandant_id` in
  `MandantTabellen` eintragen.**
- Behoben: Nach Anlegen oder Auswahl eines Mandanten zeigten Liste und Dateibaum nur noch diesen
  Mandanten. Jetzt zeigt die Liste beim Aufklappen alle; der Baum zeigt alle, der aktive ist
  hervorgehoben und aufgeklappt. Ordner laden ihren Inhalt beim Anklicken.

### [ ] P1-1 Einrichtungsassistent beim ersten Start
- Schritte: Akten-Hauptordner wählen (Struktur „ein Ordner pro Mandant“ erkennen und anzeigen),
  Bundesland (Feiertage), Kanzleidaten (Name, Anschrift, Telefon, Bankverbindung, für P1-2),
  optional Postfach (IMAP, bereits vorhanden), optional ein eigener Musterschriftsatz für den Stil.
- Am Ende: „Wollen wir eine Akte auswerten?“ Eine Akte wählen und `AkteAuswerten` starten, mit
  Live-Fortschritt. Das Ergebnis (Fristvorschläge, Beteiligte, Zeitleiste) ist das Aha-Erlebnis.
- Demo-Akte (fiktiver Familienrechtsfall mit Urteil, gegnerischem Schriftsatz und Mandantenmail)
  unter `desktop/Assets/Demo/` mitliefern, für Testkanzleien ohne echte Daten.
- Akzeptanz: Der Assistent erscheint nur, solange `AppSettings.EinrichtungAbgeschlossen` false ist.
  Er lässt sich überspringen und in den Einstellungen erneut öffnen.

### [ ] P1-2 Kanzlei-Briefbogen für alle Word-Exporte
- **Warum:** Ein Schreiben ohne den eigenen Briefkopf benutzt kein Anwalt. Heute erzeugt
  `DokumentExport.cs` die .docx von Grund auf.
- Schritte: Einstellung „Briefvorlage (.dotx/.docx)“. Der Export kopiert die Vorlage und setzt
  den Text an einer Textmarke `LexWolfText` ein (Fallback: Ende des Dokuments). Platzhalter
  `{{Datum}}`, `{{Aktenzeichen}}`, `{{Empfänger}}`, `{{Betreff}}`, `{{Mandant}}` werden ersetzt.
  Ohne Vorlage: eine saubere Standardvorlage aus den Kanzleidaten (P1-1) erzeugen.
- Akzeptanz: Tests mit einer Beispielvorlage in `desktop/test/`. Platzhalter in Kopf- und
  Fußzeile werden ersetzt.

### [ ] P1-3 Oberfläche entrümpeln
- Die Kopfzeile enthält heute 8 Bedienelemente (Feature-Wunsch, Features, Info, Einstellungen,
  Verbinden, Ordner scannen, Stil-Fortschritt, Mandant). Neu: Mandantenauswahl links groß, rechts
  ein Menü „☰“ mit Einstellungen, Ordner scannen, Feedback senden, Hilfe. Der Verbindungsstatus
  wird nur bei Problemen gezeigt.
- Die Aktenleiste (Aktenstamm, Schriftsatz, Rechner, Sachstand, Fragebogen) bleibt, sie ist der
  Kern.
- Leerzustände erklären, was als Nächstes zu tun ist (wie im Fristen-Reiter bereits gelöst).
- Akzeptanz: Screenshot vorher und nachher im PR, keine Funktion entfällt.

### [ ] P1-4 Nutzungsmessung (opt-in, anonym)
- Ereignisse ohne Inhalte: App gestartet, Akte ausgewertet, Fristvorschlag übernommen oder
  verworfen, Schriftsatz erzeugt, Entwurf exportiert, Sachstand versandt. Pro Lizenz aggregiert.
- Daraus die Kennzahlen am Ende dieses Dokuments. Beim Einrichten mit Erklärung abfragen.
- Akzeptanz: Abschalten wirkt sofort. Test, dass kein Ereignis Freitext enthält.

---

## Phase G – Gutachtliche Prüfung (Anforderung eines Anwalts, hohe Priorität)

Erfolgsaussichten, Anspruchsprüfung, Einwände gegen Klagen, anhand der Normen, mit Rechtsprechung
und eingearbeitet in den Fall. **Vollständiges Konzept mit Machbarkeit, Lücken und Arbeitspaketen:
`docs/KONZEPT_GUTACHTEN.md`.** Pakete G-0 bis G-8, Reihenfolge siehe dort.

- [ ] G-0 Grundlagen (Tunnel-Wächter, echte Bestandszahlen, Modellwahl je Aufgabe, Kostenprotokoll)
- [ ] G-1 Rechtsprechung ausbauen (Bundesgerichte ab 2010 mit Normenkette; OLG-Leitlinien; Länderquellen 🧑)
- [ ] G-2 Norm-Werkzeuge (Normtext per Zitat, Verweisungen, Fassung zum Stichtag)
- [ ] G-3 Sachverhalt aus der Akte, vom Anwalt bestätigt
- [ ] G-4 Prüfungs-Engine (Programm, Recherche, Subsumtion, Gegenprüfung, Ergebnis)
- [ ] G-5 Belegprüfung 2.0 (trägt die Entscheidung die Aussage?)
- [ ] G-6 Oberfläche: ein Knopf „⚖ Prüfen“, Dialog in drei Schritten
- [ ] G-7 Einarbeiten in Schriftsatz, Mandantenschreiben, Erwiderung
- [ ] G-8 Qualität: Musterfälle des Anwalts als Regressionstests 🧑

---

## Phase 2 – Funktionen, die man nicht mehr hergeben will

Reihenfolge nach Nutzen pro Aufwand.

### [ ] P2-1 Fristen haftungsfest machen
Die Rechtsprechung des BGH verlangt eine zuverlässige Fristenkontrolle (Fristenkalender,
Vorfristen, Löschung erst nach Erledigung, Ausgangskontrolle). Ziel: LexWolf ist der Fristenkalender.
1. **Erledigung mit Nachweis:** „Erledigt“ fragt nach der Art (Schriftsatz versandt per beA,
   Fax, Frist verlängert auf …, gegenstandslos) und dem Kürzel. Beides wird gespeichert und in der
   Fristenliste angezeigt. Bei „verlängert“ wird die neue Frist direkt angelegt.
2. **Vier-Augen-Prinzip (optional):** Einstellung „Fristen müssen von einer zweiten Person
   bestätigt werden“. Feld „eingetragen von / geprüft von“.
3. **Höchstfristen:** Bei Berufung und NZB zusätzlich das Datum Verkündung + 5 bzw. 6 Monate
   berechnen, wenn das Verkündungsdatum bekannt ist (`Hinweis` existiert schon, die Rechnung fehlt).
4. **Begründungsfrist automatisch:** Wird eine Berufungsfrist übernommen, die
   Begründungsfrist (und umgekehrt) als verknüpften Vorschlag anlegen.
5. **Erinnerungen:** Windows-Benachrichtigung um 8:00 Uhr für Vorfristen und Fristen von heute.
   Beim Schließen der App warnen, wenn heute eine Notfrist abläuft, die nicht erledigt ist.
6. **Kalender-Abo:** Statt einzelner .ics-Dateien eine ständig aktualisierte .ics-Datei aller
   offenen Fristen in einem wählbaren Ordner (Outlook kann sie als Internetkalender abonnieren).
7. **Verjährungsrechner** (eigene Methode in `FristRechner`, nicht über `FristEinheit`):
   Regelverjährung §§ 195, 199 BGB (3 Jahre ab Jahresende von Entstehung und Kenntnis),
   Höchstfristen § 199 Abs. 2–4 BGB, Hemmung durch Verhandlungen (§ 203 BGB, frühestens 3 Monate
   nach Ende), durch Klage oder Mahnbescheid (§ 204 BGB, 6 Monate nach Verfahrensende),
   familienrechtliche Hemmung § 207 BGB. Wochenend- und Feiertagsregel (§ 193 BGB) wie bei den
   übrigen Fristen. Mit Rechenweg.
- Akzeptanz: Unit-Tests für jede Rechenregel mit konkreten Daten (auch Jahreswechsel, 29.02.).
  Fachliche Regeln vor dem Abhaken vom Nutzer bestätigen lassen 🧑.

### [ ] P2-2 beA-Eingang übernehmen 🧑
- **Warum:** Gerichtspost kommt über das beA. Wenn LexWolf beA-Nachrichten automatisch in die
  richtige Akte legt und daraus Fristen vorschlägt, ist das der größte Zeitgewinn im Alltag.
- **Nutzer:** 3–5 echte beA-Exporte (ZIP aus der beA-Webanwendung bzw. der Kanzleisoftware,
  geschwärzt) als Beispiel bereitstellen. **Das Format nicht raten.**
- Schritte: Überwachter Ordner „beA-Export“. ZIP entpacken, Strukturdaten aus der XJustiz-Nachricht
  lesen (Absender, Gericht, Aktenzeichen, Eingangszeitpunkt), PDFs in den Mandantenordner legen.
  Zuordnung über das Aktenzeichen im Aktenstamm, sonst über die bestehende Zuordnungslogik, sonst
  in den Eingang „ohne Mandant“. Wird ein Empfangsbekenntnis verlangt, das Zustelldatum als
  Ereignisdatum der Frist vorschlagen und deutlich als „Datum des eEB prüfen“ markieren.
- Akzeptanz: Tests mit den Beispieldateien. Keine Datei wird doppelt abgelegt.

### [ ] P2-3 Synopse und Erwiderung auf gegnerische Schriftsätze
- **Warum:** Das Erwidern ist die zeitaufwendigste Schreibarbeit im Zivil- und Familienprozess und
  passt sehr gut zu einem KI-Agenten.
- Schritte: Im Eingang bei Dokumenttyp „Schriftsatz Gegenseite“ den Knopf „Erwiderung vorbereiten“
  anbieten. Das Backend (`/akte/synopse`) zerlegt den Schriftsatz in einzelne Tatsachenbehauptungen
  und Rechtsausführungen (JSON mit Fundstelle). Der Anwalt markiert pro Zeile: zugestehen /
  bestreiten / mit Nichtwissen bestreiten / Gegenvortrag (Freitext oder Diktat). Daraus entsteht
  ein Erwiderungsentwurf über den bestehenden Schriftsatz-Weg mit Belegprüfung. Das Ergebnis
  bleibt in der Akte gespeichert.
- Akzeptanz: Backend-Test mit festem KI-Mock. UI-Zustand übersteht das Schließen des Dialogs.

### [ ] P2-4 Fragen an die ganze Akte mit Seitenangabe
- Heute geht der Akteninhalt als `mandant_context` in den Chat-Prompt. Bei großen Akten passt das
  nicht und die Fundstelle fehlt.
- Schritte: Lokaler Suchindex pro Mandant (SQLite FTS5 über Seiten-Chunks mit `[Seite N]`). Pro
  Frage nur die relevanten Abschnitte senden (nach K-3 pseudonymisiert). Die Antwort zitiert
  `[D3, S. 4]`, und ein Klick öffnet die PDF an dieser Seite.
- Akzeptanz: Test für den Index und das Chunking. Eine Akte mit 300 Seiten antwortet in unter
  30 Sekunden.

### [ ] P2-5 VKH-Antrag aus dem Fragebogen 🧑
- **Warum:** Verfahrenskostenhilfe gibt es in sehr vielen Familiensachen. Das amtliche Formular
  „Erklärung über die persönlichen und wirtschaftlichen Verhältnisse“ auszufüllen, ist reine
  Abschreibarbeit.
- Nutzer: aktuelle amtliche PDF-Fassung (ausfüllbar) bereitstellen.
- Schritte: Den Fragebogen um die fehlenden Felder erweitern (Wohnkosten, Vermögen, Unterhaltspflichten,
  Belege). AcroForm-Felder der PDF zuordnen (Zuordnung als JSON, damit sie bei einer neuen
  Formularfassung austauschbar ist), ausfüllen und in die Akte legen. Fehlende Angaben und fehlende
  Belege als Checkliste anzeigen, auf Wunsch als Mail an den Mandanten.
- Akzeptanz: Test, dass alle zugeordneten Felder befüllt werden. Die PDF bleibt bearbeitbar.

### [ ] P2-6 Geld: Vorschuss, Kostennote, vergessene Gebühren
- Schritte:
  1. Aus dem RVG-Rechner direkt eine **Vorschussrechnung (§ 9 RVG)** und eine **Kostennote
     (§ 10 RVG)** als Word-Dokument im Briefbogen (P1-2) erzeugen, mit fortlaufender
     Rechnungsnummer, USt. und Pflichtangaben.
  2. **Gebührenhinweise:** Aus der Zeitleiste und den Eingängen erkennen, wann Gebühren entstanden
     sind (Terminsgebühr nach einem Termin, Einigungsgebühr nach einem Vergleich), und anbieten,
     sie abzurechnen. Erkennen macht die KI, rechnen macht `KanzleiRechner`.
  3. Offene Vorschüsse je Akte anzeigen.
- Akzeptanz: Tests für Rechnungsnummern und Beträge. Die Rechenwerte kommen aus `Rechenwerte`.

### [ ] P2-7 Rechenwerte ohne App-Update aktualisieren
- Düsseldorfer Tabelle, Selbstbehalte, Kindergeld und RVG-Tabellen ändern sich jährlich (meist
  zum 1. Januar). Das Backend liefert eine signierte `rechenwerte.json` mit `gueltig_ab`. Der Client
  prüft die Signatur (öffentlicher Schlüssel im Client), speichert die Datei und zeigt „Tabellen
  aktualisiert: Düsseldorfer Tabelle 2027“.
- Rechnungen mit Stichtag vor `gueltig_ab` verwenden weiter die alten Werte (Werte versioniert ablegen).
- Akzeptanz: Tests für die Signaturprüfung und die Stichtagsauswahl.

### [ ] P2-8 Mandantenkommunikation in einfacher Sprache und Fremdsprachen
- Sachstand- und Antwortdialog: Auswahl „Sprache“ (Deutsch, einfaches Deutsch, Englisch, Türkisch,
  Arabisch, Russisch, Ukrainisch, Polnisch). Das deutsche Original wird immer mit abgelegt.
- Eingehende fremdsprachige Mandantenmails werden im Eingang mit Übersetzung angezeigt.
- Akzeptanz: Prompt-Test (Sprache wird übergeben). Original und Übersetzung liegen beide in der Akte.

### [ ] P2-9 Aktenvermerk und Telefonnotiz per Diktat
- Knopf „Vermerk“ in der Aktenleiste: diktieren (vorhandener `DiktatService`), die KI gliedert in
  Datum, Gesprächspartner, Inhalt und To-dos, To-dos mit Datum werden zu Wiedervorlagen (Fristenliste,
  Status „Wiedervorlage“, keine Frist). Speichern als .docx in der Akte.

### [ ] P2-10 Wöchentliche Aktenpflege durch den Agenten
- Einmal pro Woche (oder auf Knopfdruck) für jede aktive Akte prüfen: Gibt es seit mehr als 30 Tagen
  keinen Kontakt zum Mandanten? Unbeantwortete Eingänge? Fristen ohne Vorfrist? Dokumente, die
  nicht ausgewertet sind? Ergebnis als Liste „Diese Woche liegt an“ mit Ein-Klick-Aktionen
  (Sachstand entwerfen, Antwort entwerfen, Akte auswerten).

---

## Phase 2b – Premium-Rechtsquellen (ohne kostenpflichtige Verlagslizenzen)

Ziel: LexWolf liefert mehr Rechtssicherheit und Tiefe, ohne auf teure Beck-online/juris-Lizenzen
angewiesen zu sein. Alle Quellen sind urheberrechtlich unbedenklich oder amtlich verfügbar.

### [x] P2b-1 Bundesgerichte systematisch ausbauen
- BVerfG, BGH, BAG, BFH, BSG, BVerwG bereits über RIS-Crawler abgedeckt.
- **Neu:** Dedizierter `crawlers/bgh_backfill_crawler.py`, der bundesgerichtshof.de
  systematisch nach PDF-Entscheidungen durchsucht und Volltext extrahiert. Ersetzt
  den vorherigen, unzuverlässigen OpenJur-Fallback für den BGH-Backfill.
- `main_crawler.py` nutzt `BghBackfillCrawler`; `LEXWOLF_BGH_BACKFILL_DAYS` auf
  **1095 Tage (3 Jahre)** und `LEXWOLF_BGH_BACKFILL_MAX_RESULTS` auf **500**
  angehoben (P2b-1).
- Unit-Tests in `tests/test_bgh_backfill_crawler.py` prüfen Parser, Chunking und
  Chunk-Struktur ohne Netzwerk. Integrationstest-Stub für bekannte BGH-Leitentscheidung
  vorhanden, läuft nur bei `RUN_INTEGRATION_TESTS=1`.
- **Offen:** Regressionstests für BVerfG, BAG, BFH, BSG, BVerwG (automatisierte
  Aktenzeichen-Checks); P2b-1 gilt damit für BGH als umgesetzt, für die übrigen
  Bundesgerichte als Teilumsetzung.

### [ ] P2b-2 OLG-Leitlinien und Länderrecht indexieren
- OLG-Leitlinien (z. B. BGH-orientierte OLG-Richtlinien, Familiensenate) sind oft frei auf den
  Websites der OLG verfügbar.
- Landesgesetzblätter für Bundesländer ergänzen, wo das Mandat landesspezifisch ist.
- Akzeptanz: Suche nach „OLG Dresden Leitlinien Unterhalt“ liefert Treffer mit Quelle.

### [ ] P2b-3 Gesetzesbegründungen massiv ausbauen
- BT-Drucksachen (`crawlers.drucksachen_crawler`) sind bereits angebunden.
- Sicherstellen, dass zu den Top-N-Gesetzen (BGB, ZPO, FamFG, BRAO, RVG, KSchG, BUrlG) für jede
  wichtige Änderung die Begründung im Index liegt.
- Neo4j-Kanten `ERLAEUTERT` bereits vorhanden — prüfen, dass sie für alle neuen Begründungen
  gesetzt werden.
- Akzeptanz: Test, dass zu einer Gesetzesänderung (z. B. BGB 2002) die Begründung über den
  Graph erreichbar ist.

### [ ] P2b-4 Kanzlei-eigenes Wissen als Quelle integrieren
- Musterschriftsätze, interne Checklisten, erledigte Fälle (anonymisiert) und gelernte
  Stilprofile als eigene Quelle `kanzlei_dokumente` indexieren.
- Boost im Retrieval: Kanzlei-eigene Treffer sollen bei Schriftsatz und Antworten priorisiert
  werden, sobald genügend Material vorhanden ist.
- Datenschutz: Keine Mandantennamen, keine Aktenzeichen, keine E-Mail-Adressen. Vor dem Indexieren
  mit dem bestehenden Pseudonymisierer/Anonymisierer bereinigen.
- Akzeptanz: Test, dass ein hochgeladener Musterschriftsatz bei passender Anfrage gefunden wird
  und dass keine PII im Index landet.

### [ ] P2b-5 Amtliche Formulare und Mustervordrucke
- Gerichtliche Formulare (z. B. Antrag auf Verfahrenskostenhilfe, Scheidungsantrag,
  Antrag auf Erlass einer einstweiligen Anordnung) indexieren.
- BRAK-Mustervordrucke prüfen, ob lizenzfrei nutzbar; falls nein, als eigene Aufgabe vormerken.
- Akzeptanz: 20 häufige Formulare abrufbar mit aktuellem Datum der letzten Fassung.

### [ ] P2b-6 Zitations- und Aktualitätsqualität sicherstellen
- Automatischer Check: Ist jede im Chat/Schriftsatz zitierte Norm im Index auf dem
  aktuellen Stand (`valid_until IS NULL`)?
- Warnung an den Anwalt, wenn eine zitierte Norm veraltet sein könnte oder keine Quelle gefunden
  wurde.
- Akzeptanz: Test, bei dem eine veraltete Norm bewusst im Index liegt und LexWolf die Warnung
  ausgibt.

### [ ] P2b-7 Verlags-Partnerschaften vorbereiten (optional, ohne Budget 🧑)
- Kontaktdaten und Erstkontakt-Texte für C.H. Beck (Beck-online API) und Wolters Kluwer (juris
  API) in `docs/rechtliches/partnerschaften.md` hinterlegen.
- Nicht selbst verhandeln — Material für den Nutzer bereitstellen.
- Akzeptanz: Dokument vorhanden mit Nutzen-Kosten-Argumentation und Datenverarbeitungs-Fragen.

---

## Phase 3 – Monetarisierung

### [ ] P3-1 Bezahlung und Lizenzverwaltung 🧑 (setzt K-2 voraus)
- Nutzer: Zahlungsanbieter wählen. Empfehlung: Paddle oder Lemon Squeezy (Merchant of Record,
  übernimmt die Umsatzsteuer) oder Stripe Billing.
- Claude Code: Webhook-Endpunkt im Backend setzt `kanzlei_lizenz.status` und `paid_until` (K-2).
  Testphase 30 Tage ohne Zahlungsdaten. Kulanzzeit 7 Tage bei fehlgeschlagener Zahlung. Lizenz
  pro Arbeitsplatz mit Geräteliste (abmelden möglich).
- Akzeptanz: Webhook-Tests mit Beispiel-Payloads des Anbieters, Signaturprüfung.

### [ ] P3-2 Preismodell umsetzen 🧑
Vorschlag (vom Nutzer zu entscheiden):
| Paket | Preis (netto) | Inhalt |
|---|---|---|
| Solo | 129 € / Monat | 1 Anwalt, alle Funktionen, faire Nutzung |
| Kanzlei | 99 € / Anwalt / Monat ab 3 | + Vier-Augen-Fristen, gemeinsame Fristenliste |
| Gründerpreis | −40 % dauerhaft | die ersten 50 Kanzleien, gegen Feedbackgespräche |
Jahreszahlung mit 2 Monaten Rabatt. In Tests wurde ein Preis um 200 € als akzeptabel genannt
(`docs/Trailer/02_lexwolf_official.md`). Mit dem Gründerpreis Zahlungsbereitschaft testen, bevor
der Preis festgelegt wird. Die KI-Kosten pro Kanzlei (K-4) müssen unter 15 % des Preises liegen.

### [ ] P3-3 Gemeinsame Daten für Kanzleien mit mehreren Arbeitsplätzen
- Fristen, Aktenstamm und Eingänge liegen heute in einer lokalen SQLite-Datei pro PC. In einer
  Kanzlei mit Sekretariat müssen alle dieselben Fristen sehen.
- Variante A (einfach): SQLite-Datei auf dem Kanzlei-Netzlaufwerk mit Sperrlogik. Variante B:
  Synchronisation über das Backend (Ende-zu-Ende-verschlüsselt). Zuerst Variante A, B erst bei
  Nachfrage. **Vor dem Bau mit dem Nutzer entscheiden** 🧑.

---

## Phase 4 – Ausbau (nach den ersten 20 zahlenden Kanzleien)

- [ ] **P4-1 Word-Add-in** nach `docs/word_addin_concept.md` (Office-JS): Chat und Belegprüfung
  direkt in Word, Text an der Cursorposition einfügen.
- [ ] **P4-2 Schnittstellen zu Kanzleisoftware**: zuerst j-lawyer.org (offene REST-API), dann
  Import und Export für RA-MICRO, Advoware, DATEV Anwalt (CSV/Ordnerstruktur). Ziel: LexWolf als
  KI-Schicht neben der bestehenden Kanzleisoftware, kein Ersatz.
- [ ] **P4-3 Weitere Rechtsgebiete** in dieser Reihenfolge: Arbeitsrecht (Kündigungsschutz:
  Abfindungsrechner, 3-Wochen-Frist ist schon da), Mietrecht, Verkehrsrecht. Jeweils eigener
  Fragebogen, Rechner und Schriftsatzarten.
- [ ] **P4-4 Schriftsatz-Qualitätsprüfung**: eigenen Entwurf hochladen und prüfen lassen (fehlende
  Anträge, Beweisangebote, Fristwahrung, Zitate).
- [ ] **P4-5 Gerichtsakten-Import** (elektronische Akteneinsicht, PDF mit Hunderten Seiten) mit
  Inhaltsverzeichnis und Zeitleiste.

---

## Weglassen und aufräumen

Jede Zeile ist eine eigene kleine Aufgabe. Vor dem Löschen prüfen (`grep`), dass nichts mehr darauf
verweist. Gelöschtes bleibt in der Git-Historie.

- [ ] **W-1** Repo-Wurzel: `verify_*.py`, `test_*.py`, `comprehensive_verify_task_100.py`,
  `migrate_*.py`, `migration_*.sql` aus früheren Agent-Aufgaben. Noch Nötiges nach `backend/scripts/`
  verschieben, den Rest löschen.
- [ ] **W-2** `desktop/*.py` (`confidence_coloring.py`, `drive_scanner.py`, `email_integration.py`,
  `ereignis_erkennung.py`, `sse_client.py`, `tls_client.py`, `vorschlag_engine.py`) sowie
  `MainWindow.xaml.backup` und `MainWindow.xaml.cs.backup`: Prototypen bzw. Kopien, die nicht zur
  WPF-App gehören.
- [ ] **W-3** `MainWindow.xaml.cs` (über 2200 Zeilen): Chat, Dateibaum, Scan und Update in eigene
  Partial-Dateien bzw. Dienste aufteilen, so wie `MainWindow.Akte.cs`. Auch den simulierten
  `AnonymizeText` (Zeile um 2197) durch den Pseudonymisierer (K-3) ersetzen oder entfernen.
- [ ] **W-4** Backend-Endpunkte, die der Client nicht benutzt, finden (Router-Liste in `main.py`
  gegen Aufrufe in `desktop/`). Kandidaten: `email`, `conversation`, `documents`, `knowledge`,
  `ner`, `subscription` (alt), `export`. Ungenutzte entfernen oder hinter Admin-Auth legen. Jeder
  offene Endpunkt ist Angriffsfläche.
- [ ] **W-5** Neo4j (`neo4j_service.py`, `docker-compose.yml`): prüfen, ob die Graph-Suche messbar
  bessere Antworten liefert (`tests/quality`). Wenn nicht, entfernen. Ein Dienst weniger im
  Betrieb.
- [ ] **W-6** Comic-Aliasse (Donald Duck usw. in `ComicAliasPool.cs`) nicht in der Oberfläche oder
  in Dokumenten zeigen. Für die Pseudonymisierung neutrale Platzhalter verwenden (K-3).
- [ ] **W-7** `README.md` beschreibt nur die Docker-Umgebung. Neu schreiben: Was ist LexWolf,
  Aufbau (Desktop, Backend, KI), Build, Test, Release.
- [ ] **W-8** CI (`.github/workflows`): Läuft heute nur `tests/quality` mit `|| true`. Ergänzen um
  `dotnet test desktop/LexWolf.Tests` (läuft unter Linux) und `pytest tests/unit`. Fehler müssen
  den Build rot machen.
- [ ] **W-9** Test-Projekt: verwundbare transitive Pakete (`System.IO.Packaging` über
  `DocumentFormat.OpenXml` 3.1.0, `System.Security.Cryptography.Pkcs`) anheben; App und Tests
  dieselben Versionen.

---

## Vertrieb und Markt (Aufgaben für den Nutzer, nicht für Claude Code)

1. **10 Pilotkanzleien im Familienrecht** über persönliche Kontakte, Fachanwaltslehrgänge, die
   Arbeitsgemeinschaft Familienrecht im DAV und örtliche Anwaltvereine. Wöchentliches
   15-Minuten-Gespräch und Bildschirmfreigabe beim ersten Einrichten.
2. **Eine Kennzahl verkaufen:** „Spart X Stunden pro Woche“. Mit P1-4 bei den Piloten messen
   (ausgewertete Eingänge × 6 Minuten + Entwürfe × 20 Minuten) und als Zahl auf die Website.
3. **Vertrauen vor Funktionen:** Website mit Datenschutzseite (Pseudonymisierung, EU-Hosting, AVV,
   § 43e BRAO) vor der Funktionsliste. Das ist die erste Frage jeder Kanzlei.
4. **Demo-Video** (Material in `docs/Trailer/`): eine Urteils-PDF landet im Ordner, 30 Sekunden
   später stehen Berufungs- und Begründungsfrist mit Rechenweg da, Klick, Mandantenbrief fertig.
5. **Empfehlungsprogramm:** 1 Monat gratis für beide Seiten. Anwälte empfehlen untereinander.
6. **Messen und Kammern:** Anwaltstag, regionale Fortbildungen. Schulung zur KI-Kompetenz
   (Art. 4 KI-VO) als kostenloses Webinar, mit LexWolf als Beispiel.

---

## Kennzahlen (ab P1-4 messbar)

| Kennzahl | Ziel |
|---|---|
| Aktivierung: erste Fristübernahme innerhalb von 7 Tagen nach Installation | > 60 % |
| Wöchentlich aktive Kanzleien / zahlende Kanzleien | > 80 % |
| Fristvorschläge übernommen / verworfen | > 70 % übernommen |
| Kündigungen pro Monat | < 3 % |
| KI-Kosten pro Kanzlei / Umsatz pro Kanzlei | < 15 % |
| Umwandlung Testphase → zahlend | > 25 % |

---

## Vor der Kundentestphase (K) – erst auf Ansage des Nutzers

Während der internen Testphase zurückgestellt. Diese Aufgaben werden erst bearbeitet, wenn der Nutzer
sagt, dass die Kundentestphase beginnt. **Vor dem ersten echten Mandantendokument müssen alle erledigt
sein.** Mandantenakten gehen heute
**unverschlüsselt über HTTP** an einen Server **ohne Anmeldung**.

### [ ] K-1 HTTPS für alle Verbindungen zum Backend 🧑
- **Warum:** `MainWindow.xaml.cs` verwendet `http://212.227.180.66:8000`. Akteninhalte gehen im
  Klartext durchs Internet. Das verstößt gegen Art. 32 DSGVO und gegen die Verschwiegenheitspflicht.
- **Nutzer:** Domain (z. B. `api.lexwolf.de`) auf den Server zeigen lassen.
- **Schritte:**
  1. Auf dem Server einen Reverse Proxy (Caddy, automatisches Let's-Encrypt-Zertifikat) vor uvicorn
     setzen; uvicorn nur noch auf `127.0.0.1` lauschen lassen. Als Skript und Doku in
     `deploy/` ablegen, **nicht selbst ausführen** (siehe Regel 6).
  2. `BackendUrl` und `BoardUrl` im Client auf `https://…` umstellen. `http://` nur noch zulassen, wenn
     `LEXWOLF_BACKEND_URL` ausdrücklich gesetzt ist (Entwicklung).
  3. Den Client bei `http://`-URLs auf andere Hosts als localhost die Verbindung verweigern lassen,
     mit verständlicher Meldung.
- **Akzeptanz:** Ohne Umgebungsvariable spricht der Client nur HTTPS. Ein Test für die
  URL-Prüfung (reine Funktion, z. B. `BackendAdresse.IstErlaubt(string)`).

### [ ] K-2 Anmeldung und Lizenzschlüssel für jede Backend-Anfrage
- **Warum:** Jeder kann heute `/akte/schriftsatz` aufrufen. Das ist ein Datenleck, und jeder Aufruf
  kostet KI-Geld. Außerdem ist es die Grundlage für die Abrechnung (P3).
- **Schritte:**
  1. Backend: Tabelle `kanzlei_lizenz` (id, kanzlei_name, lizenz_schluessel_hash, status, paid_until,
     max_plaetze). Auf `services/subscription_service.py` aufbauen und es erweitern statt
     duplizieren.
  2. FastAPI-Dependency `require_license` prüft den Header `Authorization: Bearer <schluessel>`
     (Hash-Vergleich, konstante Laufzeit). Auf **alle** Router anwenden. Ausnahmen nur `/health`
     und `/client-update` (Version).
  3. Rate-Limit pro Lizenz (z. B. 60 KI-Anfragen pro Minute) mit verständlicher Fehlermeldung.
  4. Client: Lizenzschlüssel in den Einstellungen, gespeichert mit DPAPI
     (`ProtectedData`, CurrentUser) statt im Klartext in `settings.json`. Den Header in jedem
     `HttpClient` setzen (zentral, nicht an jeder Aufrufstelle).
  5. Bei fehlender oder abgelaufener Lizenz: freundlicher Hinweis mit Link, lokale Funktionen
     (Fristen, Rechner, Akten lesen) bleiben nutzbar. **Fristen dürfen nie gesperrt werden.**
- **Akzeptanz:** Backend-Tests ohne und mit falschem Schlüssel liefern 401, mit gültigem Schlüssel 200.
  Client-Test für die Header-Erzeugung. Admin-Skript `backend/scripts/lizenz_anlegen.py`.

### [ ] K-3 Pseudonymisierung vor jedem KI-Aufruf
- **Warum:** Das ist das stärkste Verkaufsargument gegenüber Anwälten („Ihre Mandantennamen
  verlassen nie Ihren Rechner“). Es senkt auch das Risiko bei jedem KI-Anbieter.
  `AnonymizerEngine.cs` existiert, wird aber für Akte, Chat und Schriftsatz **nicht** verwendet.
- **Schritte:**
  1. Neuer Dienst `desktop/Services/Pseudonymisierer.cs`: Ersetzt vor dem Senden bekannte Namen
     (Mandant, Gegner, Gegnervertreter, Kinder aus dem Fragebogen, Beteiligte aus den
     Dokument-Auswertungen) sowie per Regex IBAN, Telefonnummern, E-Mail-Adressen, Geburtsdaten,
     Straßenadressen und Steuer-IDs durch stabile Platzhalter (`[PERSON_1]`, `[IBAN_1]` …). Die
     Zuordnungstabelle bleibt nur im Speicher und lokal.
  2. Rückübersetzung der KI-Antwort im Client. Platzhalter, die die KI selbst erfunden hat, bleiben
     sichtbar stehen.
  3. An allen Stellen einbauen, die Akteninhalte senden: Chat (`mandant_context`, Nachrichten),
     `/akte/analyse`, `/akte/schriftsatz`, `/akte/antwort`, `/akte/sachstand`,
     `/akte/fragebogen-lesen`. Eine zentrale Methode, kein Copy-and-paste.
  4. Einstellung „Namen vor dem Senden ersetzen“, standardmäßig **an**.
  5. Lokales Protokoll „Was wurde gesendet“ (Einstellungen → Datenschutz): die letzten 50 Anfragen
     in pseudonymisierter Form zum Nachsehen. Das schafft Vertrauen und hilft bei Kammeranfragen.
  6. Die Dokument-Auswertung braucht echte Namen im Ergebnis (Beteiligte, Kollisionsprüfung): Die
     Platzhalter im JSON werden vor dem Speichern zurückübersetzt.
- **Akzeptanz:** Unit-Tests: Hin- und Rückweg mit Umlauten, Doppelnamen, Genitiv („Schmidts“),
  Namen in Großbuchstaben, IBAN mit Leerzeichen. Test, dass ein Schriftsatz-Request keinen der
  bekannten Namen mehr enthält.

### [ ] K-4 KI-Anbieter für den Produktivbetrieb festlegen 🧑
- **Warum:** `llm_client.py` hängt im Testmodus an jede Antwort einen Hinweis. Der vorgesehene
  Anbieter `kimi-k2.7-code:cloud` über Ollama Cloud hat einen ungeklärten Serverstandort und
  keinen AVV.
- **Nutzer entscheidet:** Anbieter mit AVV, EU-Datenverarbeitung und ohne Training auf
  Kundendaten (z. B. Claude über AWS Bedrock oder Google Vertex in einer EU-Region, oder ein
  EU-Hoster). Kriterien: AVV, Serverstandort, kein Training, Qualität bei deutschen
  Rechtstexten, Preis pro Akte.
- **Claude Code danach:** Provider in `llm_client.py` ergänzen (nur dort), Testmodus-Hinweis für
  diesen Provider abschalten. Modell pro Aufgabe konfigurierbar (günstiges Modell für
  Dokument-Auswertung und Zuordnung, starkes Modell für Schriftsätze), über Parameter
  `aufgabe=` an `ask_llm`.
- **Akzeptanz:** Bei `LLM_PROVIDER=<produktiv>` kein Testhinweis. Kostenprotokoll pro Lizenz
  (Tokens pro Tag) in einer Tabelle, als Grundlage für die Preisgestaltung.

### [ ] K-5 Berufsrechts- und Datenschutzpaket 🧑
- **Warum:** Kanzleien müssen nach § 43e BRAO Dienstleister zur Verschwiegenheit verpflichten
  (Textform) und brauchen einen AVV nach Art. 28 DSGVO. Wer das fertig mitliefert, verkauft schneller.
- **Schritte (Texte in `docs/rechtliches/`, vom Nutzer anwaltlich prüfen lassen):**
  AVV-Vorlage, Verschwiegenheitsvereinbarung nach § 43e BRAO, TOM-Beschreibung (aus K-1 bis K-3
  und K-6 ableiten), Liste der Unterauftragsverarbeiter, Muster für den Mandantenhinweis zum
  KI-Einsatz, Kurzschulung „KI-Kompetenz“ (Art. 4 KI-VO verpflichtet Kanzleien als Betreiber).
- **Akzeptanz:** Dokumente vorhanden, in der App unter Einstellungen → Datenschutz verlinkt.

### [ ] K-6 Lokale Daten schützen
- **Schritte:**
  1. `settings.json`: Postfach-Passwort und Lizenzschlüssel mit DPAPI verschlüsseln (Migration:
     Klartext beim ersten Start einlesen, verschlüsselt speichern, Klartext löschen).
  2. SQLite-Datenbank (`LocalDb`) mit Fristen und Analysen: täglich automatisch sichern
     (rotierend, 14 Kopien, im Benutzerprofil oder einem wählbaren Ordner) und in den
     Einstellungen „Sicherung wiederherstellen“ anbieten. **Ein Verlust der Fristen-Datenbank ist
     ein Haftungsfall.**
  3. Prüfen, ob SQLCipher (`SQLitePCLRaw.bundle_e_sqlcipher`) ohne großen Umbau möglich ist; wenn ja,
     umsetzen, sonst als eigene Aufgabe vormerken.
- **Akzeptanz:** Tests für die Rotation der Sicherungen und für die Migration der Passwörter.

### [ ] K-7 Installer signieren 🧑
- **Warum:** Ein unsignierter Installer löst die Windows-SmartScreen-Warnung aus. Für eine
  Anwaltskanzlei sieht das wie Schadsoftware aus.
- **Nutzer:** Code-Signing-Zertifikat besorgen (OV reicht; EV oder Azure Trusted Signing
  vermeiden die Warnung schneller).
- **Claude Code:** Signaturschritt in `desktop/installer/release.sh` für `LexWolf.exe` und den
  Installer einbauen, Zertifikat nur über Umgebungsvariable oder Zertifikatsspeicher, nie im Repo.

### [ ] K-8 Release-Disziplin
- **Warum:** Die Git-Historie zeigt etwa 20 Releases v0.15 bis v0.46 für denselben Testfehler
  („Dropdown leer“). Jedes Anwalt-Feedback erzeugt automatisch ein Release. Echte Kunden bekommen
  so ständig Updates, die nichts bringen oder etwas kaputt machen.
- **Schritte:** Update-Kanäle `stable` und `beta` in `client_update.py` und `UpdateChecker.cs`.
  Automatische Releases aus Feedback gehen nur noch in `beta`. `stable` nur auf ausdrückliche
  Freigabe. Ein Changelog in Anwaltssprache statt Ticket-Titeln.
- **Akzeptanz:** Tests für die Kanalauswahl. Einstellung „Vorabversionen erhalten“ (Standard: aus).
