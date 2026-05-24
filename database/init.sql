-- Create extensions
CREATE EXTENSION IF NOT EXISTS vector;

-- Create tables for legal documents
CREATE TABLE IF NOT EXISTS legal_documents (
    id SERIAL PRIMARY KEY,
    title VARCHAR(255) NOT NULL,
    content TEXT,
    document_type VARCHAR(100),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Create table for legal knowledge base
CREATE TABLE IF NOT EXISTS legal_knowledge (
    id SERIAL PRIMARY KEY,
    title VARCHAR(255) NOT NULL,
    content TEXT,
    embedding vector(1536),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Create table for users
CREATE TABLE IF NOT EXISTS users (
    id SERIAL PRIMARY KEY,
    email VARCHAR(255) UNIQUE NOT NULL,
    name VARCHAR(255),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Create indexes
CREATE INDEX IF NOT EXISTS idx_legal_documents_type ON legal_documents(document_type);
CREATE INDEX IF NOT EXISTS idx_legal_knowledge_embedding ON legal_knowledge USING ivfflat (embedding vector_l2_ops);