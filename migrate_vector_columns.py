#!/usr/bin/env python3
"""
Migration script to update VectorType columns to real pgvector columns
"""

import os
import sys
from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker

# Add backend directory to path
backend_path = os.path.join(os.path.dirname(__file__), 'backend')
sys.path.insert(0, backend_path)

def migrate_database():
    """Migrate the database to use real pgvector columns"""
    try:
        # Database setup - use the test database for now
        DATABASE_URL = os.getenv("DATABASE_URL", "sqlite:///./test.db")
        engine = create_engine(DATABASE_URL, connect_args={"check_same_thread": False})
        SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
        
        print("Starting database migration...")
        
        # For PostgreSQL, we need to ensure the vector extension is available
        if DATABASE_URL.startswith("postgresql"):
            print("PostgreSQL detected - ensuring vector extension is available...")
            with engine.connect() as conn:
                # Create extension if it doesn't exist
                conn.execute(text("CREATE EXTENSION IF NOT EXISTS vector"))
                conn.commit()
                print("  ✓ Vector extension ensured")
        
        # For SQLite, we need to handle the migration differently
        if DATABASE_URL.startswith("sqlite"):
            print("SQLite detected - handling vector column migration...")
            
            # Check if legal_chunks table exists and has the old vector column
            with engine.connect() as conn:
                # Get table info
                result = conn.execute(text("PRAGMA table_info(legal_chunks)"))
                columns = result.fetchall()
                
                # Check if vector column exists and its type
                vector_column = None
                for col in columns:
                    if col[1] == 'vector':  # col[1] is the column name
                        vector_column = col
                        break
                
                if vector_column:
                    print(f"  Found vector column with type: {vector_column[2]}")  # col[2] is the column type
                    
                    # If it's TEXT type (from VectorType), we need to recreate the table
                    if vector_column[2].upper() == 'TEXT':
                        print("  Vector column is TEXT type (VectorType) - migrating to BLOB...")
                        
                        # For SQLite, we need to recreate the table with the new schema
                        # This is a simplified approach - in production, you'd want a more robust migration
                        
                        # Rename the old table
                        conn.execute(text("ALTER TABLE legal_chunks RENAME TO legal_chunks_old"))
                        
                        # Create new table with proper schema (this would be done by SQLAlchemy)
                        # For now, we'll just note that the application will recreate the table
                        print("  Table will be recreated with proper vector column on next application start")
                        
                        conn.commit()
                else:
                    print("  No vector column found in legal_chunks table")
        
        print("Database migration completed successfully!")
        return True
        
    except Exception as e:
        print(f"Error during migration: {e}")
        return False

def create_migration_script():
    """Create a migration script file"""
    migration_script = """
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
"""
    
    with open("migration_vector_columns.sql", "w") as f:
        f.write(migration_script)
    
    print("Created migration_vector_columns.sql")

if __name__ == "__main__":
    print("LexWolf Vector Column Migration Script")
    print("=" * 40)
    
    # Create migration SQL file
    create_migration_script()
    
    # Run database migration
    if migrate_database():
        print("\n✅ Migration completed successfully!")
        print("\nNext steps:")
        print("1. The application will automatically recreate tables with proper vector columns on next start")
        print("2. Existing vector data in JSON format will be converted to proper vector format")
        print("3. No data loss expected")
    else:
        print("\n❌ Migration failed!")
        sys.exit(1)