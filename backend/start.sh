#!/bin/bash

# Create data directory if it doesn't exist
mkdir -p data

# Initialize database
python init_db.py

# Start the FastAPI application
uvicorn main:app --host 0.0.0.0 --port 8000 --reload