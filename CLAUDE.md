# LexWolf

KI-Assistent für Anwaltskanzleien (zuerst Familienrecht): Windows-Desktop-App (WPF, .NET 10) plus
FastAPI-Backend. Arbeitsplan: **`docs/ROADMAP.md`**, Aufgaben dort der Reihe nach abarbeiten.

## Aufbau
- `desktop/`: WPF-Client. `MainWindow.xaml.cs` (Chat, Dateibaum), `MainWindow.Akte.cs` (Fristen,
  Eingang, Aktenleiste), `Services/` (Logik, möglichst ohne WPF, damit testbar), `Dialogs/`,
  `Controls/`, `Database/LocalDb*.cs` (lokale SQLite: Fristen, Analysen, Aktenstamm).
- `desktop/LexWolf.Tests/`: xUnit, plattformunabhängig (`net10.0`). Bindet einzelne Dateien aus
  `Services/` per `<Compile Include>` ein. Neue testbare Dienste dort eintragen.
- `backend/`: FastAPI (`main.py`, Router in `api/`). Akten-Endpunkte in `api/akte.py`, Chat in
  `api/chat.py` und `api/chat_prompt.py` (ohne schwere Imports, testbar).
- `tests/unit/`: schnelle Backend-Tests ohne Datenbank. `tests/acceptance/` braucht Server und
  Postgres.

## Befehle
- Desktop-Tests: `dotnet test desktop/LexWolf.Tests/LexWolf.Tests.csproj`
- App bauen: `dotnet build desktop/LexWolf.csproj`
- Backend-Unit-Tests: `python -m pytest tests/unit`
- Syntaxprüfung Backend: `backend/venv/Scripts/python.exe -m py_compile <datei>`
- Release (Version, Installer): `desktop/installer/release.sh`

## Feste Regeln
- **Alle KI-Aufrufe nur über `ask_llm()`** in `backend/services/llm_client.py`. Kein Provider-SDK
  woanders importieren.
- **Fristen und Geldbeträge berechnet nie die KI.** Die KI erkennt nur Ereignis und Fristart, gerechnet
  wird in `FristRechner.cs` bzw. `KanzleiRechner.cs`, immer mit Rechenweg.
- Neue Fristart = Eintrag in `FristRechner.Fristarten` **und** in `FRISTARTEN` in
  `backend/api/akte.py` (ein Test prüft das).
- KI-Vorschläge (Fristen, Aktenstamm) sind Vorschläge: Der Anwalt bestätigt sie. Manuell
  eingetragene Werte nie überschreiben.
- Oberfläche und Texte auf Deutsch, verständlich für Anwälte und Kanzleipersonal, keine Entwicklersprache.
- Texte in der Oberfläche **kurz** (eine Zeile, Erklärungen in Tooltip oder „Mehr …“). Ausführlich
  nur, was der Anwalt prüfen muss: Rechenwege, Belege, Entwürfe.
- Keine festen Schriftgrößen in neuer Oberfläche: `{DynamicResource LwSchriftNormal}` usw.
  verwenden (siehe ROADMAP P1-0), damit die eingestellte Schriftgröße überall gilt.
- Die Aufgaben `K-…` in der Roadmap erst bearbeiten, wenn der Nutzer die Kundentestphase ausruft.
- Keine Mandanteninhalte in Logs, Telemetrie oder Fehlermeldungen an den Server.
- **Kein Deployment auf den Server (IONOS) ohne ausdrückliche Anweisung des Nutzers.**
- Committen nur, wenn der Nutzer es verlangt.
