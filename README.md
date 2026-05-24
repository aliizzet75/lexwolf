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

## Deployment to Hostinger VPS

1. Copy the deployment directory to your Hostinger VPS (IP: 187.124.28.216)
2. Install Docker and Docker Compose on the VPS
3. Run `docker-compose up -d` in the deployment directory
4. Configure Nginx reverse proxy to forward port 8095 to port 3000

## API Endpoints

### Documents
- GET /documents - Retrieve all legal documents
- POST /documents - Create a new legal document
- GET /documents/{id} - Retrieve a specific document
- PUT /documents/{id} - Update a specific document
- DELETE /documents/{id} - Delete a specific document

### Knowledge Base
- GET /knowledge - Retrieve all knowledge base entries
- POST /knowledge - Add a new knowledge base entry
- GET /knowledge/search - Search knowledge base with semantic search

### Users
- GET /users - Retrieve all users
- POST /users - Create a new user

## Development

### Frontend
```bash
cd frontend
npm install
npm run dev
```

### Backend
```bash
cd backend
pip install -r requirements.txt
python main.py
```

## Environment Variables

Create a `.env` file in the backend directory with:
```
DATABASE_URL=postgresql://lexwolf:lexwolf@localhost:5432/lexwolf
OPENAI_API_KEY=your_openai_api_key_here
```

## License

MIT