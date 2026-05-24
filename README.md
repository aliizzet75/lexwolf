# LexWolf

LexWolf is a legal AI assistant designed to help German lawyers generate documents with AI.

## Features

- AI-powered legal document generation
- German legal framework support
- Secure authentication with NextAuth
- Vector database for legal knowledge storage
- Modern web interface with Next.js 15

## Tech Stack

- **Frontend**: Next.js 15
- **Backend**: FastAPI (Python)
- **Database**: PostgreSQL with pgvector
- **Authentication**: NextAuth
- **Deployment**: Docker Compose

## Project Structure

```
├── frontend/          # Next.js 15 frontend
├── backend/           # FastAPI backend
├── database/          # Database initialization scripts
├── docker-compose.yml # Container orchestration
└── README.md          # This file
```

## Getting Started

1. Clone the repository
2. Run `docker-compose up` to start all services
3. Access the application at http://localhost:3000

## License

MIT