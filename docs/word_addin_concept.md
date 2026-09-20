# Word Add-In Konzept für LexWolf

## Ziel

Ein Word-Add-In, das LexWolf direkt in Microsoft Word integriert. Der Anwalt
soll im Task Pane (Seitenbereich) mit LexWolf chatten, Textvorschläge erhalten
und diese per Knopfdruck an der aktuellen Cursor-Position in das offene
Word-Dokument einfügen können.

## Technologie-Entscheidung: Office-JS (empfohlen)

| Kriterium | Office-JS (Web-Add-In) | VSTO (.NET) |
|-----------|------------------------|-------------|
| Plattform | Windows, Mac, Web-Word | Nur Windows Desktop |
| Verteilung | Manifest-Datei oder Microsoft Store | MSI/ClickOnce-Installer |
| Updates | Serverseitig (kein Redeployment nötig) | Client-seitiges Update |
| UI | HTML/TypeScript/React (Office-UI-Fabric) | WPF/WinForms im Task Pane |
| Office-API | Office.js (Web-API) | Vollständiges .NET Object Model |
| Offlinefähigkeit | Lokal gehostet möglich (localhost:8000/addin) | Ja, ohne Netzwerk nötig |
| Datenschutz | UI lädt vom lokalen LexWolf-Server; sensible Texte verlassen Word nur mit Zustimmung | Alles lokal im Prozess |
| Pflegeaufwand | Geringer (Web-Stack, ein Codebase) | Höher (Windows-only, .NET Lifecycle) |

**Empfehlung: Office-JS** – passt besser zum bestehenden Tech-Stack
(React-loses, servergehostetes UI, plattformunabhängig, schnelle Updates,
lokale Bereitstellung via LexWolf-Backend).

## Architektur-Skizze

```
┌─────────────────────────────────────────────┐
│              Microsoft Word                 │
│  ┌───────────────────────────────────────┐│
│  │  Task Pane: LexWolf Word Add-In         ││
│  │  - HTML/JS/TS App (Office.js)           ││
│  │  - Lädt von http://localhost:8000/addin ││
│  │  - Chat-UI + Vorschläge + Einfügen      ││
│  └─────────────────┬───────────────────────┘│
└────────────────────┼────────────────────────┘
                     │ Office.js API
                     │ insertText / insertFileFromBase64
                     ▼
┌─────────────────────────────────────────────┐
│         LexWolf Backend (FastAPI)           │
│  - /addin/           → React/HTML/JS        │
│  - /addin/manifest.xml → Word-Add-In-Manifest│
│  - /chat, /ask, /export/docx, /export/pdf   │
└─────────────────────────────────────────────┘
```

## Grundlegende Funktionen (MVP)

1. **Task Pane öffnen**
   - Word-Menüband/Button „LexWolf öffnen“
   - Task Pane lädt UI vom lokalen Server.

2. **Chat-Interface**
   - Multi-Turn-Chat wie im Desktop-Client.
   - Kontext aus aktuellem Dokument kann optional übergeben werden
     (z. B. markierter Text oder Titel).

3. **Direkteinfügung**
   - Button „In Word einfügen“ fügt letzte KI-Antwort an der Cursor-Position
     ein.
   - Alternativ: „Als DOCX einfügen“ für formatierten Entwurf.

4. **Mandanten-Erkennung**
   - Optional: Wenn im Dokument ein bekannter Mandantenname vorkommt, wird
     die Akte automatisch geladen (Phase 2).

## Proof-of-Concept (minimal)

- Ein statisches HTML-Fragment unter `backend/static/addin/index.html`, das
  über `Office.context.document.setSelectedDataAsync` Text einfügt.
- Ein minimales `manifest.xml` unter `backend/static/addin/manifest.xml`, das
  Word die URL und Berechtigungen mitteilt.
- Kein vollständiges UI; nur Nachweis, dass Office.js im Task Pane läuft und
  Text in das Dokument geschrieben werden kann.

## Aufwand-Schätzung Vollimplementierung

| Arbeitspaket | Aufwand |
|--------------|---------|
| Add-In-UI (React/TS) | 3–4 Tage |
| Office.js-Dokumenten-API (Einfügen, Cursor, Formatierung) | 2 Tage |
| Backend: Add-In-Hosting + Manifest-Auslieferung | 0,5 Tage |
| Chat-Integration mit bestehendem /chat Endpunkt | 1 Tag |
| Mandanten-Erkennung aus Dokument | 1–2 Tage |
| Testing & Windows-/Mac-Kompatibilität | 2–3 Tage |
| **Gesamt** | **10–13 Tage** |

## Offene Punkte / Nächste Schritte

- [ ] React-Add-In-Gerüst in `desktop/word-addin/` oder `backend/static/addin/` anlegen.
- [ ] `manifest.xml` mit localhost:8000 URLs erstellen.
- [ ] Office.js-Befehl zum Einfügen von HTML/Text/DOCX testen.
- [ ] Authentifizierung/Session zwischen Word-Add-In und LexWolf-Backend klären.
- [ ] Entscheidung, ob der Anwalt markierten Text als Kontext senden möchte
      (DSGVO/Einwilligung).
