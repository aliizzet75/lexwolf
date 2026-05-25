from sqlalchemy import Column, Integer, String, Text, DateTime, Float, ForeignKey, Boolean
from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy.orm import relationship, mapped_column
from datetime import datetime

# Import Vector from pgvector
from pgvector.sqlalchemy import Vector

Base = declarative_base()

class LegalDocument(Base):
    __tablename__ = 'legal_documents'
    
    id = Column(Integer, primary_key=True, index=True)
    title = Column(String, index=True)
    content = Column(Text)
    url = Column(String)
    source = Column(String)  # e.g., 'gesetze-im-internet.de', 'openjur.de'
    document_type = Column(String)  # e.g., 'law', 'judgment', 'regulation'
    created_at = Column(DateTime, default=datetime.utcnow)
    updated_at = Column(DateTime, default=datetime.utcnow, onupdate=datetime.utcnow)
    
    # Relationship to chunks
    chunks = relationship("LegalChunk", back_populates="document")

class LegalChunk(Base):
    __tablename__ = 'legal_chunks'
    
    id = Column(Integer, primary_key=True, index=True)
    document_id = Column(Integer, ForeignKey('legal_documents.id'))
    text = Column(Text)
    
    # Use real pgvector Vector column
    vector = mapped_column(Vector(1536))  # Using text-embedding-3-small with 1536 dimensions
    
    title = Column(String)
    court = Column(String)  # For judgments
    case_number = Column(String)  # Aktenzeichen
    date = Column(DateTime)
    legal_field = Column(String)  # Rechtsgebiet
    tags = Column(String)  # Comma-separated tags
    chunk_hash = Column(String)  # For deduplication
    parent_id = Column(Integer)  # For parent-child chunking
    is_parent = Column(Boolean, default=False)
    created_at = Column(DateTime, default=datetime.utcnow)
    
    # Relationship back to document
    document = relationship("LegalDocument", back_populates="chunks")

class StyleProfile(Base):
    __tablename__ = 'style_profiles'
    
    id = Column(Integer, primary_key=True, index=True)
    profile_id = Column(String, unique=True)  # e.g., "sp_a7f3b2"
    
    # Use real pgvector Vector column
    vector = mapped_column(Vector(1536))  # Stil-Vektor-Repräsentation
    
    created_at = Column(DateTime, default=datetime.utcnow)
    updated_at = Column(DateTime, default=datetime.utcnow, onupdate=datetime.utcnow)

class SearchResult(Base):
    __tablename__ = 'search_results'
    
    id = Column(Integer, primary_key=True, index=True)
    query = Column(String)
    chunk_id = Column(Integer)
    relevance_score = Column(Float)
    search_type = Column(String)  # 'hyde', 'dense', 'sparse', 'rrf'
    created_at = Column(DateTime, default=datetime.utcnow)