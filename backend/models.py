from sqlalchemy import Column, Integer, String, Text, DateTime, Float, ForeignKey, Boolean, Date, Enum
from sqlalchemy.dialects.postgresql import TSVECTOR
import enum
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
    vector = mapped_column(Vector(768))  # paraphrase-multilingual-mpnet-base-v2, lokal
    
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
    valid_from = Column(Date, nullable=True)   # Gültig ab (Versionierung)
    valid_until = Column(Date, nullable=True)  # Gültig bis (NULL = aktuelle Version)
    embedding_model = Column(String, nullable=True)       # Modellname beim letzten Re-Embedding
    embedding_timestamp = Column(DateTime, nullable=True)  # Zeitpunkt des letzten Re-Embeddings

    # Full-text search vector column
    ts_vector = mapped_column("ts_vector", TSVECTOR, nullable=True)
    
    # Relationship back to document
    document = relationship("LegalDocument", back_populates="chunks")

class StyleProfile(Base):
    __tablename__ = 'style_profiles'
    
    id = Column(Integer, primary_key=True, index=True)
    profile_id = Column(String, unique=True)  # e.g., "sp_a7f3b2"
    
    # Use real pgvector Vector column
    vector = mapped_column(Vector(768))  # paraphrase-multilingual-mpnet-base-v2, lokal
    
    # Gelernte Stil-Metadaten (aus feedback_table, täglich aktualisiert)
    durchschnittliche_satzlaenge = Column(Float, nullable=True)
    formulierungs_praeferenzen = Column(Text, nullable=True)  # JSON
    korrigierte_muster = Column(Text, nullable=True)           # JSON
    anzahl_eintraege = Column(Integer, nullable=True)
    
    # A/B-Test State pro Account (T#90)
    ab_test_json = Column(Text, nullable=True)  # JSON: laufende Tests + implizite Praeferenzen
    
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

class FeedbackEntry(Base):
    __tablename__ = 'feedback_table'

    id = Column(Integer, primary_key=True, index=True)
    original = Column(Text, nullable=True)       # NICHT mehr genutzt / auf NULL gesetzt
    korrigiert = Column(Text, nullable=True)     # NICHT mehr genutzt / auf NULL gesetzt
    kategorie = Column(String)    # formulierung | satzstruktur | paragraph | laenge

    # Anonymisierte, aggregierte Stil-Metriken (DSGVO-konform, T#89)
    satzlaengen_verteilung = Column(Text, nullable=True)      # JSON
    wortklassen_haeufigkeiten = Column(Text, nullable=True)   # JSON
    korrektur_kategorien = Column(Text, nullable=True)        # JSON

    zeitstempel = Column(DateTime, default=datetime.utcnow)
    created_at = Column(DateTime, default=datetime.utcnow)

class SubscriptionStatus(str, enum.Enum):
    active = "active"
    inactive = "inactive"
    trial = "trial"

class User(Base):
    __tablename__ = 'users'

    id = Column(Integer, primary_key=True, index=True)
    email = Column(String, unique=True, nullable=False, index=True)
    subscription_status = Column(
        Enum(SubscriptionStatus, name='subscription_status_enum', create_type=True),
        nullable=False,
        default=SubscriptionStatus.inactive
    )
    paid_until = Column(Date, nullable=True)
    created_at = Column(DateTime, default=datetime.utcnow)