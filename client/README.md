# LexWolf Client Application

## Overview

The LexWolf Client is a local application for German lawyers that provides:

- **Local Document Processing**: Style analysis and anonymization without uploading sensitive data
- **Conversation Recording**: Record and analyze client conversations with automatic entity extraction
- **Learning Assistant**: Adapts to your writing style and decision patterns
- **Document Generation**: Create legal documents using templates and AI assistance
- **Server Integration**: Securely connect to the LexWolf server for legal database access

## Features

### 1. Style Analysis
- Analyzes writing style locally (never uploads content)
- Creates anonymized style profiles
- Adapts to your preferences over time

### 2. Anonymization
- Automatically identifies and anonymizes personal data
- Uses spaCy NER for German language processing
- Maintains mapping locally for deanonymization

### 3. Conversation Recording
- Records client conversations (audio processing via Whisper)
- Extracts key entities (dates, persons, actions)
- Generates suggestions for next steps

### 4. Learning Assistant
- Tracks your decision patterns
- Learns from style corrections
- Remembers client preferences and history

### 5. Document Generation
- Template-based document creation
- AI-assisted content generation
- Style-consistent output

## Installation

### Prerequisites
- Python 3.7 or later
- pip package manager

### Setup
```bash
# Run the setup script
./setup.sh

# Or manually install requirements
pip install -r requirements.txt

# Download German language model
python -m spacy download de_core_news_sm
```

## Usage

```bash
# Start the LexWolf client
python lexwolf_client.py
```

## Architecture

```
┌─────────────────────────────────────────────┐
│  CLIENT (Anwaltsrechner — vollständig lokal) │
│                                             │
│  • Folder-Scanner: liest Schriftsatz-Ordner │
│  • Lokales KI-Modell: analysiert Schreibstil│
│  • Anonymisierer: Namen → [PERSON_1] etc.   │
│  • ID-Mapping: Mandant-Name ↔ interne ID    │
│  • De-Anonymisierer: empfangenen Entwurf    │
│    mit echten Namen befüllen                │
│  • Word-Export: .docx Ausgabe               │
│                                             │
│  Offen für: Dialoge, Korrekturen, E-Mail    │
└──────────────┬──────────────────────────────┘
               │ Nur: anonymer Sachverhalt
               │      Stil-Profil-ID
               │      Schriftsatztyp
               ↕ HTTPS
┌──────────────┴──────────────────────────────┐
│  SERVER (Hetzner Deutschland)               │
│                                             │
│  FastAPI Backend                            │
│  ├── Auth (NextAuth, Account pro Anwalt)    │
│  ├── Hybrid Search (LanceDB + BM25 + RRF)  │
│  ├── HyDE Query-Expansion                  │
│  ├── Claude API (anonymisierte Anfragen)   │
│  └── Stil-Profile (nur IDs, keine Inhalte) │
│                                             │
│  PostgreSQL + pgvector                      │
│  LanceDB (Vektoren)                         │
│  Crawler (nächtlich, openjur etc.)          │
└─────────────────────────────────────────────┘
```

## Modules

### Style Analyzer (`src/style_analyzer.py`)
- Analyzes writing style features
- Creates vector representations
- Generates style profiles

### Anonymizer (`src/anonymizer.py`)
- Identifies personal data using spaCy NER
- Replaces with placeholders
- Maintains local mapping for deanonymization

### Conversation Recorder (`src/conversation_recorder.py`)
- Records and transcribes conversations
- Extracts entities and action items
- Generates summaries and suggestions

### Document Generator (`src/document_generator.py`)
- Template-based document creation
- Fills templates with case data
- Supports multiple output formats

### Learning Assistant (`src/learning_assistant.py`)
- Tracks decision patterns
- Learns from style feedback
- Remembers client history

## Data Privacy

- **Local Processing**: All sensitive data processed locally
- **No Content Upload**: Only anonymized data and style profile IDs sent to server
- **Encrypted Storage**: Local data stored securely
- **GDPR Compliant**: Designed for German legal compliance

## Configuration

The client can be configured via `config/default.json`:

```json
{
  "server_url": "http://localhost:8000",
  "style_profile_id": null,
  "default_language": "de",
  "anonymization_enabled": true,
  "whisper_model": "small",
  "local_storage_path": "./data"
}
```

## Development

### Directory Structure
```
client/
├── src/                 # Source code
│   ├── main.py          # Main application
│   ├── style_analyzer.py # Style analysis module
│   ├── anonymizer.py    # Anonymization module
│   ├── conversation_recorder.py # Conversation recording
│   ├── document_generator.py # Document generation
│   └── learning_assistant.py # Learning assistant
├── config/              # Configuration files
├── data/                # Local data storage
├── assets/              # Images and resources
├── tests/               # Test files
├── requirements.txt     # Python dependencies
├── setup.sh            # Setup script
└── README.md           # This file
```

## Requirements

- tkinter (for GUI)
- spacy (for NLP processing)
- requests (for server communication)
- numpy (for numerical operations)
- pydub (for audio processing)

## License

This project is licensed under the MIT License - see the LICENSE file for details.