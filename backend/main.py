from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
import os

# Import API routers
from api.legal_db import router as legal_db_router
from api.email import router as email_router
from api.conversation import router as conversation_router
from api.documents import router as documents_router
from api.knowledge import router as knowledge_router
from api.quality import router as quality_router
from api.ask import router as ask_router
from api.subscription import router as subscription_router
from api.chat import router as chat_router
from api.mandant import router as mandant_router
from api.ner import router as ner_router
from api.client_update import router as client_update_router
from api.tool_update import router as tool_update_router
from api.downloads import router as downloads_router

# Import Neo4j service
from services.neo4j_service import Neo4jService

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

# Initialize Neo4j service
neo4j_service = Neo4jService()

# Connect to Neo4j and set up schema on startup
@app.on_event("startup")
async def startup_event():
    if neo4j_service.connect():
        if neo4j_service.setup_schema():
            print("Neo4j schema setup completed successfully")
        else:
            print("Failed to set up Neo4j schema")
    else:
        print("Failed to connect to Neo4j database")

# Close Neo4j connection on shutdown
@app.on_event("shutdown")
async def shutdown_event():
    neo4j_service.close()
    print("Neo4j connection closed")

# Include routers
app.include_router(legal_db_router)
app.include_router(email_router)
app.include_router(conversation_router)
app.include_router(documents_router)
app.include_router(knowledge_router)
app.include_router(quality_router)
app.include_router(ask_router)
app.include_router(subscription_router)
app.include_router(chat_router)
app.include_router(mandant_router)
app.include_router(ner_router)
app.include_router(client_update_router)
app.include_router(tool_update_router)
app.include_router(downloads_router)
app.mount(
    "/client/download",
    StaticFiles(directory=os.path.join(os.path.dirname(__file__), "static", "client-releases")),
    name="client-downloads",
)
app.mount(
    "/tools/download",
    StaticFiles(directory=os.path.join(os.path.dirname(__file__), "static", "tool-releases")),
    name="tool-downloads",
)

@app.get("/")
async def root():
    return {"message": "LexWolf Legal Database API"}

@app.get("/health")
async def health_check():
    return {"status": "healthy"}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)