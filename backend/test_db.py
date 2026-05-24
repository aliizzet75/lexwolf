import os
from sqlalchemy import create_engine, text

# Database setup
DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://lexwolf:lexwolf@localhost:5432/lexwolf")

try:
    engine = create_engine(DATABASE_URL, pool_pre_ping=True)
    print("Database connection successful")
    
    # Test query
    with engine.connect() as connection:
        result = connection.execute(text("SELECT 1"))
        print(f"Query result: {result.fetchone()}")
        
except Exception as e:
    print(f"Database connection failed: {e}")