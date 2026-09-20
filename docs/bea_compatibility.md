# beA-Kompatibilitätsprüfung für LexWolf-PDF-Export

## Zusammenfassung

Dieses Dokument beschreibt die Anforderungen des besonderen elektronischen
Anwaltspostfachs (beA) an PDFs, die über das elektronische Verfahren bei
Gerichten eingereicht werden. Es dient als Recherche- und Entscheidungsgrundlage
für die weitere Ausgestaltung des LexWolf-PDF-Exports.

## Rechtlicher und technischer Kontext

- **beA** ist die geschlossene Kommunikationsplattform der deutschen
  Rechtsanwaltschaft für den elektronischen Rechtsverkehr (ERV) mit Gerichten
  und Behörden.
- Schriftsätze, die über beA an Gerichte gesendet werden, müssen die technischen
  Vorgaben des jeweiligen Gerichts bzw. der XJustiz-/beA-Spezifikationen
  erfüllen.
- Aktuell gelten für viele Gerichte die Vorgaben der
  **Elektronischer-Rechtsverkehr-Standards (ERV-Standards)** und der
  **XJustiz-Nachrichtenstruktur**.

## beA-Formatanforderungen an PDFs

### 1. Dateiformat

- PDF-Dateien sind grundsätzlich zulässig, wenn das Empfangsgericht PDF als
  Format akzeptiert.
- Empfohlen und vielfach vorausgesetzt ist ein **PDF im PDF/A-Format**,
  vorzugsweise **PDF/A-1a** oder **PDF/A-1b**, da diese Formate Langzeit-
  archivierung und eingebettete Schriften garantieren.
- Einige Gerichte akzeptieren auch reine PDF-Dateien ohne PDF/A-Angabe;
  Langzeitarchivfähigkeit ist jedoch Pflichtbestandteil der ERV-Standards.

### 2. Eingebettete Schriftarten (Fonts)

- Alle verwendeten Schriftarten müssen **vollständig im PDF eingebettet** sein.
- Nicht eingebettete oder nur referenzierte Fonts führen beim Empfänger zu
  Darstellungsfehlern oder Ablehnung.
- LexWolf verwendet aktuell **Times New Roman**. Diese Schriftart ist in
  Windows-Systemen vorhanden; LibreOffice muss sie beim PDF-Export korrekt
  einbetten.

### 3. PDF/A-Konformität

- **PDF/A-1b** (ISO 19005-1) stellt die Mindestanforderung: eingebettete Fonts,
  Farbrauminformationen, keine Verschlüsselung, keine multimediale Inhalte.
- **PDF/A-1a** erfordert zusätzlich Tagged PDF / logische Struktur (barriere-
  frei); wird von LibreOffice-Headless-Exporten in der Regel nicht automatisch
  erzeugt.
- LibreOffice erzeugt im Headless-Modus mit `--convert-to pdf` kein PDF/A,
  sondern ein gewöhnliches PDF. Für PDF/A ist die Option
  `--convert-to pdf:writer_pdf_Export:SelectPdfVersion=1` nötig.

### 4. Dateigröße und Seitenzahlen

- Einzelne PDF-Dateien dürfen die vom Gericht vorgegebene Maximalgröße nicht
  überschreiten (häufig 10 MB, 20 MB oder 50 MB je nach Gericht).
- Seitenränder und Layout sollten druckfertig sein; LexWolf nutzt derzeit
  **2,5 cm Ränder**.

### 5. Zulässige Inhalte

- Keine Passwörter, keine Verschlüsselung, keine Formularfelder, keine
  JavaScript-Aktionen.
- Keine Multimedia-Inhalte (Audio/Video).
- Keine externen Verweise oder nicht eingebettete Objekte.

## Aktueller Stand des LexWolf-PDF-Exports

- Der PDF-Export in `backend/api/routes/export.py` baut ein DOCX mit
  identischer Formatierung zum DOCX-Export auf und konvertiert es anschließend
  mit LibreOffice headless nach PDF.
- LibreOffice-Befehl aktuell:

  ```text
  soffice --headless --convert-to pdf --outdir <tmpdir> <docx>
  ```

- Dieser Aufruf erzeugt ein **Standard-PDF**, nicht notwendigerweise PDF/A.
- Times New Roman wird aus dem DOCX übernommen; die Einbettung hängt von der
  LibreOffice-Installation und der verfügbaren Schriftart ab.

## Gaps / Offene Punkte

1. **PDF/A-Konvertierung**
   - LibreOffice-Parameter muss auf PDF/A umgestellt werden, z. B.:
     ```text
     soffice --headless --convert-to "pdf:writer_pdf_Export:SelectPdfVersion=1" ...
     ```
   - Alternativ: Nachkonvertierung mit `pikepdf`, `ghostscript` oder `qpdf`.

2. **Font-Einbettung verifizieren**
   - Mit `pdffonts` (Poppler) oder `pdfinfo` prüfen, ob alle Fonts embedded
     sind.
   - Falls nicht, Schriftart in LibreOffice-Profil persistent einbetten oder
     Ghostscript-Nachbearbeitung (`-dPDFSETTINGS=/prepress`) verwenden.

3. **PDF/A-Level**
   - Festlegen, ob PDF/A-1b ausreicht oder PDF/A-1a/2a/2b gefordert wird.
   - PDF/A-1a erfordert Tagged PDF, was mit DOCX → LibreOffice → PDF nicht
     trivial ist.

4. **Validierung automatisieren**
   - Ein zukünftiger Acceptance-Test sollte erzeugte PDFs auf PDF/A-Kennung und
     eingebettete Fonts prüfen (z. B. mit `pypdf`, `pikepdf` oder
     `veraPDF`-CLI).

5. **Gerichtsspezifische Sonderregeln**
   - Verschiedene Gerichte/Gerichtsbarkeiten haben unterschiedliche
     Anforderungen. Eine zentrale Konfiguration oder hinterlegte Whitelist
     wäre sinnvoll.

## Empfohlene nächste Schritte

- Umstellen des LibreOffice-Aufrufs auf PDF/A-Export.
- Tool-Abhängigkeiten auf dem Deployment-Host prüfen:
  `libreoffice`, `libreoffice-common`, ggf. `fonts-liberation`,
  `fonts-dejavu`, `ttf-mscorefonts-installer`.
- Test-PDF erzeugen und mit `pdffonts` / `pdfinfo` auf eingebettete Fonts und
  PDF/A-Version prüfen.
- Sobald beA-spezifische Validierung möglich ist, in den Acceptance-Test
  integrieren.

## Quellen

- Bundesrechtsanwaltskammer: beA-Hilfe und ERV-FAQs
- XJustiz-Spezifikationen des Bundesministeriums der Justiz
- ISO 19005 (PDF/A)
- LibreOffice Writer PDF-Export-Parameter
