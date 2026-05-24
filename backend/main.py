from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
import os

# Import API routers
from api.legal_db import router as legal_db_router
from api.email import router as email_router
from api.conversation import router as conversation_router
from api.documents import router as documents_router
from api.knowledge import router as knowledge_router

app = FastAPI(
    title="LexWolf Legal Database API",
    description="API for legal database crawler and search system",
    version="1.0.0"
)

# Add CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Include routers
app.include_router(legal_db_router)
app.include_router(email_router)
app.include_router(conversation_router)
app.include_router(documents_router)
app.include_router(knowledge_router)

@app.get("/")
async def root():
    return {"message": "LexWolf Legal Database API"}

@app.get("/health")
async def health_check():
    return {"status": "healthy"}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)