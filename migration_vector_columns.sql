
-- Migration script for updating VectorType to real pgvector columns

-- For PostgreSQL:
-- Ensure vector extension is available
CREATE EXTENSION IF NOT EXISTS vector;

-- For SQLite:
-- Since SQLite doesn't support direct column type changes for significant changes,
-- the typical approach is to recreate the table with the new schema.

-- Backup approach for SQLite:
-- 1. Create new table with correct schema
-- 2. Copy data from old table
-- 3. Drop old table
-- 4. Rename new table

-- This migration is handled automatically by the application on startup
-- when the new models.py is used.
