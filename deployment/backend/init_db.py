import sqlite3
import os

# Create SQLite database and tables
def init_db():
    # Ensure database directory exists
    db_path = "data/lexwolf.db"
    os.makedirs(os.path.dirname(db_path), exist_ok=True)
    
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    
    # Create entries table
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS entries (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            title TEXT NOT NULL,
            content TEXT,
            chapter TEXT,
            tags TEXT,
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        )
    ''')
    
    # Create versions table for edit history
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS versions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            entry_id INTEGER,
            content TEXT,
            timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (entry_id) REFERENCES entries (id)
        )
    ''')
    
    # Create roadmap table
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS roadmap (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            task TEXT NOT NULL,
            quarter TEXT,
            status TEXT DEFAULT 'planned',
            progress_percent REAL DEFAULT 0.0,
            created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        )
    ''')
    
    # Create FTS5 virtual table for full-text search
    cursor.execute('''
        CREATE VIRTUAL TABLE IF NOT EXISTS entries_fts USING fts5(
            title, 
            content, 
            chapter, 
            tags,
            content='entries',
            content_rowid='id'
        )
    ''')
    
    # Create triggers to keep FTS table in sync
    cursor.execute('''
        CREATE TRIGGER IF NOT EXISTS entries_ai AFTER INSERT ON entries BEGIN
            INSERT INTO entries_fts(rowid, title, content, chapter, tags) 
            VALUES (new.id, new.title, new.content, new.chapter, new.tags);
        END
    ''')
    
    cursor.execute('''
        CREATE TRIGGER IF NOT EXISTS entries_ad AFTER DELETE ON entries BEGIN
            INSERT INTO entries_fts(fts_entries, rowid, title, content, chapter, tags) 
            VALUES('delete', old.id, old.title, old.content, old.chapter, old.tags);
        END
    ''')
    
    cursor.execute('''
        CREATE TRIGGER IF NOT EXISTS entries_au AFTER UPDATE ON entries BEGIN
            INSERT INTO entries_fts(fts_entries, rowid, title, content, chapter, tags) 
            VALUES('delete', old.id, old.title, old.content, old.chapter, old.tags);
            INSERT INTO entries_fts(rowid, title, content, chapter, tags) 
            VALUES (new.id, new.title, new.content, new.chapter, new.tags);
        END
    ''')
    
    # Insert sample data for chapters
    chapters = [
        "PRODUKT",
        "ARCHITEKTUR", 
        "ENTWICKLUNG",
        "DOKUMENTATION",
        "TESTING & QUALITÄT"
    ]
    
    for chapter in chapters:
        cursor.execute('''
            INSERT OR IGNORE INTO entries (title, content, chapter, tags) 
            VALUES (?, ?, ?, ?)
        ''', (f"{chapter} Overview", f"Overview of {chapter} section", chapter, "overview"))
    
    # Insert sample roadmap items
    roadmap_items = [
        ("Implement authentication", "Q2 2026", "in_progress", 75.0),
        ("Add document generation", "Q2 2026", "planned", 0.0),
        ("Deploy to production", "Q3 2026", "planned", 0.0),
        ("Add AI features", "Q3 2026", "planned", 0.0),
        ("User testing", "Q3 2026", "planned", 0.0)
    ]
    
    for task, quarter, status, progress in roadmap_items:
        cursor.execute('''
            INSERT OR IGNORE INTO roadmap (task, quarter, status, progress_percent)
            VALUES (?, ?, ?, ?)
        ''', (task, quarter, status, progress))
    
    conn.commit()
    conn.close()
    print("Database initialized successfully!")

if __name__ == "__main__":
    init_db()