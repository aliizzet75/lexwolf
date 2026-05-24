from sqlalchemy import Column, Integer, String, Text, DateTime, Float, ForeignKey
from sqlalchemy.ext.declarative import declarative_base
from sqlalchemy.orm import relationship
from pgvector.sqlalchemy import Vector
from datetime import datetime

Base = declarative_base()

class LegalDocument(Base):
    __tablename__ = 'legal_documents'
    
    id = Column(Integer, primary_key=True, index=True)
    title = Column(String, index=True)
    content = Column(Text)
    document_type = Column(String, index=True)
    created_at = Column(DateTime, default=datetime.utcnow)
    updated_at = Column(DateTime, default=datetime.utcnow, onupdate=datetime.utcnow)
    
    # Relationship to versions
    versions = relationship("DocumentVersion", back_populates="document")

class DocumentVersion(Base):
    __tablename__ = 'document_versions'
    
    id = Column(Integer, primary_key=True, index=True)
    document_id = Column(Integer, ForeignKey('legal_documents.id'))
    content = Column(Text)
    timestamp = Column(DateTime, default=datetime.utcnow)
    
    # Relationship back to document
    document = relationship("LegalDocument", back_populates="versions")

class LegalKnowledge(Base):
    __tablename__ = 'legal_knowledge'
    
    id = Column(Integer, primary_key=True, index=True)
    title = Column(String, index=True)
    content = Column(Text)
    embedding = Column(Vector(1536))  # Using ada-002 with 1536 dimensions
    created_at = Column(DateTime, default=datetime.utcnow)
    updated_at = Column(DateTime, default=datetime.utcnow, onupdate=datetime.utcnow)

class User(Base):
    __tablename__ = 'users'
    
    id = Column(Integer, primary_key=True, index=True)
    email = Column(String, unique=True, index=True)
    name = Column(String)
    created_at = Column(DateTime, default=datetime.utcnow)