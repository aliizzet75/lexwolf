from fastapi import FastAPI, HTTPException, Depends
from sqlalchemy import create_engine, text, func
from sqlalchemy.orm import sessionmaker, Session
from typing import List, Optional
from pydantic import BaseModel
from datetime import datetime
import os
import openai
import logging

# Try to import Vector from pgvector, fallback if not available
try:
    from pgvector.sqlalchemy import Vector
except ImportError:
    # Dummy Vector class for development
    class Vector:
        def __init__(self, dimensions):
            self.dimensions = dimensions

from models import Base, LegalDocument, DocumentVersion, LegalKnowledge, User

# Set up logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Database setup
DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://lexwolf:lexwolf@localhost:5432/lexwolf")
engine = create_engine(DATABASE_URL, pool_pre_ping=True)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)

# Create database tables
try:
    Base.metadata.create_all(bind=engine)
    logger.info("Database tables created successfully")
except Exception as e:
    logger.error(f"Error creating database tables: {e}")

# OpenAI setup
openai.api_key = os.getenv("OPENAI_API_KEY", "your_openai_api_key_here")

app = FastAPI(
    title="LexWolf API",
    description="Legal AI assistant for German lawyers",
    version="1.0.0"
)

# Dependency
def get_db():
    db = SessionLocal()
    try:
        yield db
    except Exception as e:
        logger.error(f"Database session error: {e}")
        raise
    finally:
        db.close()

# Pydantic models
class LegalDocumentBase(BaseModel):
    title: str
    content: str
    document_type: str

class LegalDocumentCreate(LegalDocumentBase):
    pass

class LegalDocumentUpdate(LegalDocumentBase):
    pass

class LegalDocumentInDB(LegalDocumentBase):
    id: int
    created_at: datetime
    updated_at: datetime
    
    class Config:
        orm_mode = True

class LegalKnowledgeBase(BaseModel):
    title: str
    content: str

class LegalKnowledgeCreate(LegalKnowledgeBase):
    pass

class LegalKnowledgeInDB(LegalKnowledgeBase):
    id: int
    created_at: datetime
    updated_at: datetime
    
    class Config:
        orm_mode = True

class UserBase(BaseModel):
    email: str
    name: str

class UserCreate(UserBase):
    pass

class UserInDB(UserBase):
    id: int
    created_at: datetime
    
    class Config:
        orm_mode = True

# API Endpoints

@app.get("/")
async def root():
    return {"message": "LexWolf API is running"}

# Document endpoints
@app.get("/documents", response_model=List[LegalDocumentInDB])
async def get_documents(skip: int = 0, limit: int = 100, db: Session = Depends(get_db)):
    try:
        documents = db.query(LegalDocument).offset(skip).limit(limit).all()
        return documents
    except Exception as e:
        logger.error(f"Error fetching documents: {e}")
        raise HTTPException(status_code=500, detail=f"Error fetching documents: {str(e)}")

@app.post("/documents", response_model=LegalDocumentInDB)
async def create_document(document: LegalDocumentCreate, db: Session = Depends(get_db)):
    try:
        db_document = LegalDocument(**document.dict())
        db.add(db_document)
        db.commit()
        db.refresh(db_document)
        return db_document
    except Exception as e:
        db.rollback()
        logger.error(f"Error creating document: {e}")
        raise HTTPException(status_code=500, detail=f"Error creating document: {str(e)}")

@app.get("/documents/{document_id}", response_model=LegalDocumentInDB)
async def get_document(document_id: int, db: Session = Depends(get_db)):
    try:
        document = db.query(LegalDocument).filter(LegalDocument.id == document_id).first()
        if document is None:
            raise HTTPException(status_code=404, detail="Document not found")
        return document
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error fetching document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=f"Error fetching document: {str(e)}")

@app.put("/documents/{document_id}", response_model=LegalDocumentInDB)
async def update_document(document_id: int, document: LegalDocumentUpdate, db: Session = Depends(get_db)):
    try:
        db_document = db.query(LegalDocument).filter(LegalDocument.id == document_id).first()
        if db_document is None:
            raise HTTPException(status_code=404, detail="Document not found")
        
        # Save current content to versions table
        version = DocumentVersion(document_id=document_id, content=db_document.content)
        db.add(version)
        
        # Update document
        for key, value in document.dict().items():
            setattr(db_document, key, value)
        db_document.updated_at = datetime.utcnow()
        
        db.commit()
        db.refresh(db_document)
        return db_document
    except HTTPException:
        raise
    except Exception as e:
        db.rollback()
        logger.error(f"Error updating document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=f"Error updating document: {str(e)}")

@app.delete("/documents/{document_id}")
async def delete_document(document_id: int, db: Session = Depends(get_db)):
    try:
        document = db.query(LegalDocument).filter(LegalDocument.id == document_id).first()
        if document is None:
            raise HTTPException(status_code=404, detail="Document not found")
        
        db.delete(document)
        db.commit()
        return {"message": "Document deleted successfully"}
    except HTTPException:
        raise
    except Exception as e:
        db.rollback()
        logger.error(f"Error deleting document {document_id}: {e}")
        raise HTTPException(status_code=500, detail=f"Error deleting document: {str(e)}")

# Knowledge base endpoints
@app.get("/knowledge", response_model=List[LegalKnowledgeInDB])
async def get_knowledge(skip: int = 0, limit: int = 100, db: Session = Depends(get_db)):
    try:
        knowledge_items = db.query(LegalKnowledge).offset(skip).limit(limit).all()
        return knowledge_items
    except Exception as e:
        logger.error(f"Error fetching knowledge items: {e}")
        raise HTTPException(status_code=500, detail=f"Error fetching knowledge items: {str(e)}")

@app.post("/knowledge", response_model=LegalKnowledgeInDB)
async def add_knowledge(knowledge: LegalKnowledgeCreate, db: Session = Depends(get_db)):
    try:
        # Generate embedding using OpenAI ada-002 model (1536 dimensions)
        embedding = None
        try:
            response = openai.embeddings.create(
                input=knowledge.content,
                model="text-embedding-ada-002"
            )
            embedding = response.data[0].embedding
        except Exception as openai_error:
            # If OpenAI fails, log the error but continue with null embedding
            print(f"Warning: Failed to generate embedding: {openai_error}")
        
        db_knowledge = LegalKnowledge(
            title=knowledge.title,
            content=knowledge.content,
            embedding=embedding
        )
        db.add(db_knowledge)
        db.commit()
        db.refresh(db_knowledge)
        return db_knowledge
    except Exception as e:
        db.rollback()
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/knowledge/search", response_model=List[LegalKnowledgeInDB])
async def search_knowledge(query: str, limit: int = 10, db: Session = Depends(get_db)):
    try:
        # Generate embedding for the query
        response = openai.embeddings.create(
            input=query,
            model="text-embedding-ada-002"
        )
        query_embedding = response.data[0].embedding
        
        # Check if there are any knowledge items in the database
        knowledge_count = db.query(LegalKnowledge).count()
        if knowledge_count == 0:
            # Return empty list if no knowledge items exist
            return []
        
        # Perform semantic search using pgvector, filtering out items with null embeddings
        results = db.query(LegalKnowledge).filter(
            LegalKnowledge.embedding.isnot(None)
        ).order_by(
            LegalKnowledge.embedding.l2_distance(query_embedding)
        ).limit(limit).all()
        
        return results
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

# User endpoints
@app.get("/users", response_model=List[UserInDB])
async def get_users(skip: int = 0, limit: int = 100, db: Session = Depends(get_db)):
    try:
        users = db.query(User).offset(skip).limit(limit).all()
        return users
    except Exception as e:
        logger.error(f"Error fetching users: {e}")
        raise HTTPException(status_code=500, detail=f"Error fetching users: {str(e)}")

@app.post("/users", response_model=UserInDB)
async def create_user(user: UserCreate, db: Session = Depends(get_db)):
    try:
        db_user = User(**user.dict())
        db.add(db_user)
        db.commit()
        db.refresh(db_user)
        return db_user
    except Exception as e:
        db.rollback()
        logger.error(f"Error creating user: {e}")
        raise HTTPException(status_code=500, detail=f"Error creating user: {str(e)}")