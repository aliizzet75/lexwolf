#!/bin/bash

# Deployment script for LexWolf to Hostinger VPS
# Host: 187.124.28.216
# Port: 8095

echo "Starting LexWolf deployment to Hostinger VPS..."

# Build the frontend
echo "Building frontend..."
cd /data/.openclaw/workspace-codex/projects/lexwolf/frontend
npm install
npm run build

# Copy frontend build to deployment directory
echo "Copying frontend build..."
mkdir -p /data/.openclaw/workspace-codex/projects/lexwolf/deployment/frontend
cp -r /data/.openclaw/workspace-codex/projects/lexwolf/frontend/.next /data/.openclaw/workspace-codex/projects/lexwolf/deployment/frontend/
cp -r /data/.openclaw/workspace-codex/projects/lexwolf/frontend/public /data/.openclaw/workspace-codex/projects/lexwolf/deployment/frontend/
cp /data/.openclaw/workspace-codex/projects/lexwolf/frontend/package.json /data/.openclaw/workspace-codex/projects/lexwolf/deployment/frontend/

# Prepare backend for deployment
echo "Preparing backend..."
mkdir -p /data/.openclaw/workspace-codex/projects/lexwolf/deployment/backend
cp -r /data/.openclaw/workspace-codex/projects/lexwolf/backend/*.py /data/.openclaw/workspace-codex/projects/lexwolf/deployment/backend/
cp /data/.openclaw/workspace-codex/projects/lexwolf/backend/requirements.txt /data/.openclaw/workspace-codex/projects/lexwolf/deployment/backend/
cp /data/.openclaw/workspace-codex/projects/lexwolf/backend/Dockerfile /data/.openclaw/workspace-codex/projects/lexwolf/deployment/backend/

# Create deployment docker-compose.yml
cat > /data/.openclaw/workspace-codex/projects/lexwolf/deployment/docker-compose.yml << EOF
version: '3.8'

services:
  frontend:
    build:
      context: ./frontend
      dockerfile: Dockerfile
    ports:
      - "3000:3000"
    environment:
      - NEXT_PUBLIC_API_URL=http://localhost:8000
    depends_on:
      - backend
    volumes:
      - ./frontend:/app
      - /app/node_modules

  backend:
    build:
      context: ./backend
      dockerfile: Dockerfile
    ports:
      - "8000:8000"
    environment:
      - DATABASE_URL=postgresql://lexwolf:lexwolf@db:5432/lexwolf
      - OPENAI_API_KEY=your_openai_api_key_here
    depends_on:
      db:
        condition: service_healthy
    volumes:
      - ./backend:/app

  db:
    image: ankane/pgvector:latest
    ports:
      - "5432:5432"
    environment:
      - POSTGRES_USER=lexwolf
      - POSTGRES_PASSWORD=lexwolf
      - POSTGRES_DB=lexwolf
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./database/init.sql:/docker-entrypoint-initdb.d/init.sql
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U lexwolf"]
      interval: 5s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
EOF

echo "Deployment package created successfully!"
echo "To deploy to Hostinger:"
echo "1. Copy the deployment directory to your Hostinger VPS"
echo "2. Install Docker and Docker Compose on the VPS"
echo "3. Run 'docker-compose up -d' in the deployment directory"
echo "4. Configure Nginx reverse proxy to forward port 8095 to port 3000"