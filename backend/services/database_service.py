import os
import logging
from sqlalchemy import create_engine, text, func
from sqlalchemy.orm import sessionmaker
from models import Base, LegalDocument, LegalChunk, StyleProfile
from datetime import datetime
import hashlib

logger = logging.getLogger(__name__)


def _parse_date(date_str: str):
    """Parst DD.MM.YYYY und YYYY-MM-DD."""
    for fmt in ('%Y-%m-%d', '%d.%m.%Y', '%Y-%m-%dT%H:%M:%S', '%d.%m.%Y %H:%M'):
        try:
            return datetime.strptime(date_str.strip(), fmt)
        except ValueError:
            continue
    return None

class DatabaseService:
    """
    Service for database operations
    """
    
    def __init__(self):
        # Database setup - use PostgreSQL for production, SQLite for testing
        self.DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://postgres:postgres@host.docker.internal:5432/lexwolf")
        self.engine = create_engine(self.DATABASE_URL, connect_args={"check_same_thread": False} if "sqlite" in self.DATABASE_URL else {})
        self.SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=self.engine)
        
        # Create database tables
        Base.metadata.create_all(bind=self.engine)
    
    def store_chunk(self, chunk_data: dict):
        """
        Store a chunk in the database
        """
        db = self.SessionLocal()
        try:
            # Check if chunk already exists (deduplication)
            existing_chunk = db.query(LegalChunk).filter(
                LegalChunk.chunk_hash == chunk_data.get('chunk_hash')
            ).first()
            
            if existing_chunk:
                print(f"Chunk with hash {chunk_data.get('chunk_hash')} already exists, skipping")
                return
            
            # Create document if it doesn't exist
            document = db.query(LegalDocument).filter(
                LegalDocument.url == chunk_data.get('url')
            ).first()
            
            if not document:
                document = LegalDocument(
                    title=chunk_data.get('title', ''),
                    content='',  # Content will be in chunks
                    url=chunk_data.get('url', ''),
                    source=chunk_data.get('source', ''),
                    document_type=chunk_data.get('document_type', ''),
                    created_at=datetime.utcnow(),
                    updated_at=datetime.utcnow()
                )
                db.add(document)
                db.flush()  # Get document ID
            
            # Versioning: close previous active version of same title/paragraph
            valid_from_val = chunk_data.get('valid_from')
            valid_until_val = chunk_data.get('valid_until')
            if valid_from_val and not valid_until_val and chunk_data.get('title'):
                from datetime import date as date_type
                if isinstance(valid_from_val, str):
                    from datetime import datetime as dt
                    valid_from_val = dt.strptime(valid_from_val, '%Y-%m-%d').date()
                db.execute(
                    text(
                        "UPDATE legal_chunks SET valid_until = :vf "
                        "WHERE title = :t AND valid_until IS NULL AND valid_from IS NOT NULL"
                    ),
                    {"vf": valid_from_val, "t": chunk_data.get('title', '')}
                )

            # Create chunk
            chunk = LegalChunk(
                document_id=document.id,
                text=chunk_data.get('text', ''),
                vector=chunk_data.get('vector'),  # pgvector list[float]
                title=chunk_data.get('title', ''),
                court=chunk_data.get('court', ''),
                case_number=chunk_data.get('case_number', ''),
                date=_parse_date(chunk_data.get('date')) if chunk_data.get('date') else None,
                legal_field=chunk_data.get('legal_field', ''),
                tags=chunk_data.get('tags', ''),
                chunk_hash=chunk_data.get('chunk_hash', ''),
                parent_id=chunk_data.get('parent_id'),
                is_parent=chunk_data.get('is_parent', False),
                created_at=datetime.utcnow(),
                valid_from=valid_from_val,
                valid_until=valid_until_val,
            )
            
            db.add(chunk)
            db.flush()
            # ts_vector direkt per SQL befüllen (PostgreSQL to_tsvector)
            db.execute(
                text("UPDATE legal_chunks SET ts_vector = to_tsvector('german', :t) WHERE id = :id"),
                {"t": (chunk_data.get('text') or '')[:100000], "id": chunk.id}
            )
            db.commit()
            db.refresh(chunk)
            return {"id": chunk.id, "title": chunk.title}

        except Exception as e:
            db.rollback()
            logger.error(f"Error storing chunk: {e}")
            return None
        finally:
            db.close()
    
    def save_chunk(self, chunk_data: dict, embedding: list):
        """Save a chunk with embedding and return dict with id."""
        chunk_data['vector'] = embedding
        return self.store_chunk(chunk_data)
    
    def search_chunks_hybrid(self, query: str, limit: int = 10) -> list:
        """
        Perform hybrid search (dense + sparse) on chunks
        """
        db = self.SessionLocal()
        try:
            # For now, return all chunks (in a real implementation, this would do actual search)
            chunks = db.query(LegalChunk).limit(limit).all()
            
            results = []
            for chunk in chunks:
                results.append({
                    "id": chunk.id,
                    "text": chunk.text,
                    "title": chunk.title,
                    "score": 0.0,  # In a real implementation, this would be a relevance score
                    "source": chunk.document.source if chunk.document else "",
                    "url": chunk.document.url if chunk.document else ""
                })
            
            return results
        except Exception as e:
            print(f"Error searching chunks: {e}")
            return []
        finally:
            db.close()
    
    def get_chunk_by_id(self, chunk_id: int) -> dict:
        """
        Get a specific chunk by ID
        """
        db = self.SessionLocal()
        try:
            chunk = db.query(LegalChunk).filter(LegalChunk.id == chunk_id).first()
            
            if chunk:
                # Parse vector JSON if it exists
                vector_data = None
                if chunk.vector:
                    try:
                        vector_data = json.loads(chunk.vector)
                    except:
                        vector_data = None
                
                return {
                    "id": chunk.id,
                    "document_id": chunk.document_id,
                    "text": chunk.text,
                    "title": chunk.title,
                    "court": chunk.court,
                    "case_number": chunk.case_number,
                    "date": chunk.date.isoformat() if chunk.date else None,
                    "legal_field": chunk.legal_field,
                    "tags": chunk.tags,
                    "chunk_hash": chunk.chunk_hash,
                    "parent_id": chunk.parent_id,
                    "is_parent": chunk.is_parent,
                    "created_at": chunk.created_at.isoformat()
                }
            else:
                return None
        except Exception as e:
            print(f"Error getting chunk: {e}")
            return None
        finally:
            db.close()
    
    def store_style_profile(self, profile_id: str, vector: list):
        """
        Store a style profile in the database
        """
        db = self.SessionLocal()
        try:
            # Check if profile already exists
            profile = db.query(StyleProfile).filter(
                StyleProfile.profile_id == profile_id
            ).first()
            
            if profile:
                # Update existing profile
                profile.vector = json.dumps(vector)  # Store as JSON string
                profile.updated_at = datetime.utcnow()
            else:
                # Create new profile
                profile = StyleProfile(
                    profile_id=profile_id,
                    vector=json.dumps(vector),  # Store as JSON string
                    created_at=datetime.utcnow(),
                    updated_at=datetime.utcnow()
                )
                db.add(profile)
            
            db.commit()
            print(f"Stored style profile: {profile_id}")
            
        except Exception as e:
            db.rollback()
            print(f"Error storing style profile: {e}")
        finally:
            db.close()