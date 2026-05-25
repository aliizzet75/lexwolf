
-- Migration script for adding ts_vector column for PostgreSQL Full-Text Search

-- For PostgreSQL:
-- Add ts_vector column to legal_chunks table
ALTER TABLE legal_chunks 
ADD COLUMN IF NOT EXISTS ts_vector tsvector;

-- Populate ts_vector column with data from text, title, and other relevant fields
UPDATE legal_chunks 
SET ts_vector = setweight(to_tsvector('german', coalesce(title, '')), 'A') ||
                setweight(to_tsvector('german', coalesce(text, '')), 'B');

-- Create GIN index on ts_vector for fast searching
CREATE INDEX IF NOT EXISTS idx_legal_chunks_ts_vector 
ON legal_chunks 
USING GIN (ts_vector);

-- Create a trigger to automatically update ts_vector when text changes
CREATE OR REPLACE FUNCTION update_ts_vector()
RETURNS TRIGGER AS $$
BEGIN
    NEW.ts_vector := setweight(to_tsvector('german', coalesce(NEW.title, '')), 'A') ||
                     setweight(to_tsvector('german', coalesce(NEW.text, '')), 'B');
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Drop existing trigger if exists
DROP TRIGGER IF EXISTS update_legal_chunks_ts_vector ON legal_chunks;

-- Create trigger for automatic ts_vector updates
CREATE TRIGGER update_legal_chunks_ts_vector
BEFORE INSERT OR UPDATE ON legal_chunks
FOR EACH ROW EXECUTE FUNCTION update_ts_vector();
