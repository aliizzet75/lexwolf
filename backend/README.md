# LexWolf Legal Database Crawler & Search System

## Overview

This implementation provides a comprehensive legal database crawler and search system for German legal documents, including laws and court decisions. The system implements intelligent chunking, hybrid search, and nightly crawling as specified in Task #21.

## Features Implemented

### 1. Legal Database Crawlers
- **gesetze-im-internet.de crawler**: Crawls German laws with XML API support
- **openjur.de crawler**: Crawls German court decisions with JSON API support
- **Parent-Child Chunking**: Intelligently chunks documents for better retrieval
- **Deduplication**: Uses chunk hashes to avoid duplicate content

### 2. Database Schema
- **LegalDocument**: Represents complete legal documents (laws, judgments)
- **LegalChunk**: Represents chunks of legal content with parent-child relationships
- **StyleProfile**: Stores anonymized writing style profiles
- **SearchResult**: Tracks search results for analytics

### 3. Search Pipeline
- **HyDE (Hypothetical Document Embeddings)**: Generates better query vectors
- **Dense Search**: Vector similarity search using LanceDB
- **Sparse Search**: BM25 keyword matching for exact term retrieval
- **RRF (Reciprocal Rank Fusion)**: Combines dense and sparse results
- **Re-ranking**: (Planned) Claude-based relevance scoring

### 4. Services
- **Embedding Service**: Generates 1536-dim embeddings using text-embedding-3-small
- **Database Service**: Manages PostgreSQL + pgvector storage
- **Search Service**: Implements hybrid search pipeline
- **Crawling Scheduler**: Nightly crawl automation (3:00 AM)

### 5. API Endpoints
- `POST /legal-db/search`: Hybrid search endpoint
- `GET /legal-db/chunks/{chunk_id}`: Retrieve specific chunk
- `POST /legal-db/style-profiles`: Store style profiles
- `GET /legal-db/health`: Health check

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

## Implementation Details

### Chunking Strategy
- **Parent-Child Structure**: Parents contain complete documents, children contain individual sections
- **~500 Token Chunks**: Optimal size for retrieval precision
- **Metadata Enrichment**: Court, case numbers, legal fields, tags

### Embedding Strategy
- **Model**: text-embedding-3-small (1536 dimensions)
- **Content**: Title + first 200 tokens of content
- **Storage**: PostgreSQL with pgvector extension

### Search Pipeline
1. **HyDE**: LLM generates hypothetical answer → better query vector
2. **Dense**: LanceDB finds top-20 via cosine similarity
3. **Sparse**: BM25 finds top-20 via keyword matching
4. **RRF**: Reciprocal Rank Fusion combines both rankings
5. **Re-ranking**: (Future) Claude scores top-10 for relevance
6. **Parent**: Full parent chunk provided for context

## Nightly Crawling

The system implements a nightly crawl scheduler that runs at 3:00 AM to:
- Fetch new court decisions from openjur.de
- Check for law updates from gesetze-im-internet.de
- Process and chunk new content
- Generate embeddings and store in database

## Data Privacy & Security

- **Client-Side Processing**: Style analysis happens locally
- **Anonymization**: Real names replaced with placeholders
- **Data Separation**: Each lawyer's data is strictly separated
- **No Content Upload**: Only style profile IDs sent to server

## Setup & Deployment

1. **Database**: PostgreSQL with pgvector extension
2. **Dependencies**: Install requirements from `requirements.txt`
3. **Environment**: Set `DATABASE_URL` and `OPENAI_API_KEY`
4. **Run**: `uvicorn main:app --host 0.0.0.0 --port 8000`

## Future Enhancements

- Integration with additional sources (BVerfG, rewis.io, EUR-Lex)
- Advanced re-ranking with Claude API
- Client-side application for local processing
- E-mail integration for automated responses
- Conversation recording and analysis