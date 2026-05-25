# LexWolf Development Environment

This repository contains the development environment setup for LexWolf, a legal AI assistant.

## Prerequisites

- Docker and Docker Compose installed
- Git

## Quick Start

1. Clone the repository:
```bash
git clone <repository-url>
cd lexwolf
```

2. Copy the example environment file:
```bash
cp .env.example .env
```

3. Edit `.env` file to add your API keys:
```bash
nano .env
```

4. Start the development environment:
```bash
docker-compose up -d
```

5. Access the services:
- FastAPI: http://localhost:8000
- PostgreSQL: localhost:5432

## Services

### PostgreSQL with pgvector
- Database: `lexwolf`
- User: `postgres`
- Password: `postgres`
- Port: `5432`
- Extensions: `pgvector` for semantic search capabilities

### FastAPI Service
- Port: `8000`
- Auto-reload enabled for development
- Connected to PostgreSQL database

## Database Initialization

The database is automatically initialized with pgvector extension enabled. The initialization scripts in `init-scripts/` directory are executed on first run.

## Environment Variables

Copy `.env.example` to `.env` and customize the values:

```bash
cp .env.example .env
```

Key variables:
- `DATABASE_URL`: Connection string for PostgreSQL
- `OPENAI_API_KEY`: OpenAI API key for embeddings
- `CLAUDE_API_KEY`: Claude API key for generation

## Development Workflow

1. Make changes to the code in the `backend/` directory
2. The FastAPI service will automatically reload
3. View logs with:
```bash
docker-compose logs -f
```

## Stopping the Environment

To stop all services:
```bash
docker-compose down
```

To stop and remove all data (including database):
```bash
docker-compose down -v
```

## Troubleshooting

### Database Connection Issues
- Ensure Docker is running
- Check that port 5432 is not being used by another service
- Verify database credentials in `.env`

### API Service Issues
- Check logs: `docker-compose logs api`
- Ensure all dependencies are installed
- Verify environment variables are set correctly

### First Run Takes Too Long
- Initial database setup may take a few minutes
- Subsequent runs will be faster

## Data Persistence

Database data is persisted in a Docker volume. To reset the database:
```bash
docker-compose down -v
docker-compose up -d
```

## Testing the Setup

1. Check that services are running:
```bash
docker-compose ps
```

2. Test database connection:
```bash
docker-compose exec db pg_isready -U postgres
```

3. Test API health endpoint:
```bash
curl http://localhost:8000/health
```

## Next Steps

After the environment is running, you can:
1. Connect to the database using your preferred PostgreSQL client
2. Access the FastAPI documentation at http://localhost:8000/docs
3. Start developing your application features