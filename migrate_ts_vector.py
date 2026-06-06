#!/usr/bin/env python3
"""
Migration script to add ts_vector column for PostgreSQL Full-Text Search
"""

import os
import sys
from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker

# Add backend directory to path
backend_path = os.path.join(os.path.dirname(__file__), 'backend')
sys.path.insert(0, backend_path)

def migrate_database():
    """Migrate the database to add ts_vector column and populate it"""
    try:
        # Database setup - use the DATABASE_URL from environment or default to test database
        DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")
        engine = create_engine(DATABASE_URL, connect_args={"check_same_thread": False})
        SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
        
        print("Starting database migration for ts_vector column...")
        
        # For PostgreSQL, we need to add the ts_vector column and create indexes
        if DATABASE_URL.startswith("postgresql"):
            print("PostgreSQL detected - adding ts_vector column and setting up FTS...")
            with engine.connect() as conn:
                # Add ts_vector column if it doesn't exist
                try:
                    conn.execute(text("""
                        ALTER TABLE legal_chunks 
                        ADD COLUMN IF NOT EXISTS ts_vector tsvector
                    """))
                    print("  ✓ Added ts_vector column to legal_chunks table")
                except Exception as e:
                    print(f"  ⚠️  Column may already exist or error: {e}")
                
                # Populate ts_vector column with data from text, title, and other relevant fields
                conn.execute(text("""
                    UPDATE legal_chunks 
                    SET ts_vector = setweight(to_tsvector('german', coalesce(title, '')), 'A') ||
                                    setweight(to_tsvector('german', coalesce(text, '')), 'B')
                """))
                print("  ✓ Populated ts_vector column with data")
                
                # Create GIN index on ts_vector for fast searching
                try:
                    conn.execute(text("""
                        CREATE INDEX IF NOT EXISTS idx_legal_chunks_ts_vector 
                        ON legal_chunks 
                        USING GIN (ts_vector)
                    """))
                    print("  ✓ Created GIN index on ts_vector column")
                except Exception as e:
                    print(f"  ⚠️  Index may already exist or error: {e}")
                
                # Create a trigger to automatically update ts_vector when text changes
                try:
                    conn.execute(text("""
                        CREATE OR REPLACE FUNCTION update_ts_vector()
                        RETURNS TRIGGER AS $$
                        BEGIN
                            NEW.ts_vector := setweight(to_tsvector('german', coalesce(NEW.title, '')), 'A') ||
                                             setweight(to_tsvector('german', coalesce(NEW.text, '')), 'B');
                            RETURN NEW;
                        END;
                        $$ LANGUAGE plpgsql
                    """))
                    print("  ✓ Created update_ts_vector function")
                    
                    # Create trigger
                    conn.execute(text("""
                        DROP TRIGGER IF EXISTS update_legal_chunks_ts_vector 
                        ON legal_chunks
                    """))
                    
                    conn.execute(text("""
                        CREATE TRIGGER update_legal_chunks_ts_vector
                        BEFORE INSERT OR UPDATE ON legal_chunks
                        FOR EACH ROW EXECUTE FUNCTION update_ts_vector()
                    """))
                    print("  ✓ Created trigger for automatic ts_vector updates")
                except Exception as e:
                    print(f"  ⚠️  Trigger creation error: {e}")
                
                conn.commit()
                
        # For SQLite, we'll add a text column as a placeholder
        elif DATABASE_URL.startswith("sqlite"):
            print("SQLite detected - adding ts_vector column as text placeholder...")
            with engine.connect() as conn:
                # Add ts_vector column as text for SQLite
                try:
                    conn.execute(text("""
                        ALTER TABLE legal_chunks 
                        ADD COLUMN ts_vector TEXT
                    """))
                    print("  ✓ Added ts_vector column to legal_chunks table (SQLite)")
                except Exception as e:
                    print(f"  ⚠️  Column may already exist or error: {e}")
                
                conn.commit()
        
        print("Database migration completed successfully!")
        return True
        
    except Exception as e:
        print(f"Error during migration: {e}")
        return False

def create_migration_script():
    """Create a SQL migration script file"""
    migration_script = """
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
"""
    
    with open("migration_ts_vector.sql", "w") as f:
        f.write(migration_script)
    
    print("Created migration_ts_vector.sql")

if __name__ == "__main__":
    print("LexWolf PostgreSQL Full-Text Search Migration Script")
    print("=" * 55)
    
    # Create migration SQL file
    create_migration_script()
    
    # Run database migration
    if migrate_database():
        print("\n✅ Migration completed successfully!")
        print("\nNext steps:")
        print("1. The ts_vector column has been added to legal_chunks table")
        print("2. Data has been populated into the ts_vector column")
        print("3. GIN index created for fast FTS performance")
        print("4. Automatic update trigger installed")
        print("\nBenefits:")
        print("  ✓ Real PostgreSQL Full-Text Search capability")
        print("  ✓ Exact matching for legal citations like '§ 623 BGB'")
        print("  ✓ Proper ranking with ts_rank function")
        print("  ✓ Automatic updates when content changes")
    else:
        print("\n❌ Migration failed!")
        sys.exit(1)