# LexWolf Anonymisierer

WPF .NET 10 Client-Application für die Anonymisierung von juristischen Dokumenten.

## Features

- **Dokumenten-Anonymisierung**: Persönliche Daten, Beträge, Adressen und mehr werden anonymisiert
- **De-Anonymisierung**: Nur lokal auf dem PC - keine Daten verlassen den Rechner
- **Unterstützte Formate**: .docx, .pdf, .txt, .eml
- **Local-First**: Alle Daten bleiben auf dem Anwalts-PC

## Tech Stack

- Framework: WPF (.NET 10.0 Windows)
- Packages:
  - DocumentFormat.OpenXml (Word-Dokumente)
  - iText7 (PDF-Verarbeitung, read+write)
  - MimeKit (EML-Verarbeitung)
  - DiffPlex (Text-Diffs)

## Projektstruktur

```
Anonymisierer/
├── Anonymisierer.csproj          # Projektdatei mit NuGet-References
├── App.xaml                      # App.xaml mit Ressourcen
├── App.xaml.cs                   # Application-Klasse
├── MainWindow.xaml               # Haupt-UI mit DockPanel + SplitPane
├── MainWindow.xaml.cs            # Haupt-Window-Logik
├── Main.cs                       # Anonymisierungs-Logik
├── Models/
│   └── FileEntry.cs              # Modell für Dateieinträge
├── Services/
│   └── DirectoryScannerService.cs # Rekursive Dateiscanner-Service
└── Anonymisierer.Tests/          # Unit-Tests
```

## UI-Layout

```
┌────────────────────┬──────────────────────────┐
│  [LOGO]            │  [ANONYMISIERUNG]        │
│  LexWolf           │  ┌────────────────────┐  │
├────────────────────┤  │ Vorschau           │  │
│  [Hilfe] [⚙️]     │  │                  │  │
├────────────────────┤  │ Erkannte         │  │
│                    │  │ Entitäten        │  │
│  📁 Dokumente      │  │                  │  │
│  ┌──────────────┐  │  └────────────────────┘  │
│  │ 📁 Ordner    │  │                        │
│  │ Mandanten    │  │  [🔄 Anonymisieren]    │
│  │              │  │  [🔄 De-Anonymisieren] │
│  │ Dokumenten   │  │  [📁 Exportieren]      │
│  │arten         │  │                        │
│  └──────────────┘  └────────────────────────┘
└────────────────────┴──────────────────────────┘
```

## API-Integration

Der Client kommuniziert mit dem LexWolf Backend über localhost:8000

## Entwicklung

Da dotnet CLI in diesem Container nicht verfügbar ist:
- Erstellen Sie das Projekt manuell oder mit Visual Studio
- Installieren Sie die NuGet-Pakete
- Kompilieren Sie mit `dotnet build`

## Tests

Die Akzeptanz-Tests liegen in:
`/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/tests/acceptance/test_acceptance_184.py`

## Quellen

- gesetze-im-internet.de (XML-API)
- openjur.de (JSON-API)
- BVerfG, BGH, BAG, BSG Entscheidungen
