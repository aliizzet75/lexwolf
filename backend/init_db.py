import os
from sqlalchemy import create_engine, text
from sqlalchemy.orm import sessionmaker
from models import Base, LegalDocument, DocumentVersion, LegalKnowledge, User

# Database setup
DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://lexwolf:lexwolf@localhost:5432/lexwolf")
engine = create_engine(DATABASE_URL)

# Create database tables
try:
    Base.metadata.create_all(bind=engine)
    print("Database tables created successfully")
except Exception as e:
    print(f"Error creating database tables: {e}")

# Create a session to check if tables exist and are accessible
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)

def init_db():
    try:
        # Create a test session
        db = SessionLocal()
        
        # Test query to check if tables are accessible
        try:
            # Test legal_documents table
            doc_count = db.query(LegalDocument).count()
            print(f"Legal documents table accessible, count: {doc_count}")
            
            # Test legal_knowledge table
            knowledge_count = db.query(LegalKnowledge).count()
            print(f"Legal knowledge table accessible, count: {knowledge_count}")
            
            # Test users table
            user_count = db.query(User).count()
            print(f"Users table accessible, count: {user_count}")
            
        except Exception as query_error:
            print(f"Error querying tables: {query_error}")
            
        db.close()
        
    except Exception as e:
        print(f"Error initializing database: {e}")

if __name__ == "__main__":
    init_db()