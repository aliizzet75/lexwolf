import os
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
from models import Base

# Database setup
DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://lexwolf:lexwolf@localhost:5432/lexwolf")
engine = create_engine(DATABASE_URL)

# Create database tables
Base.metadata.create_all(bind=engine)

print("Database tables created successfully")