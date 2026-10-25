from fastapi import APIRouter, HTTPException, Depends
from typing import Any, Dict, List, Optional
from pydantic import BaseModel
import logging
from services.conversation_assistant import ConversationAssistant
from services.feedback_service import FeedbackService, FeedbackValidationError
from services.style_learner import StyleLearner, GUELTIGE_VORSCHLAG_ENTSCHEIDUNGEN

router = APIRouter(prefix="/conversation", tags=["conversation"])

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Pydantic models for request/response
class TranscriptRequest(BaseModel):
    transcript: str
    client_id: Optional[str] = None
    context: Optional[str] = None

class EntityResponse(BaseModel):
    dates: List[str]
    deadlines: List[str]
    persons: List[str]
    legal_terms: List[str]
    actions: List[str]
    amounts: List[str]

class ConversationResponse(BaseModel):
    timestamp: str
    client_id: Optional[str]
    transcript: str
    entities: EntityResponse
    topics: List[str]
    sentiment: dict

class SummaryRequest(BaseModel):
    conversation_data: ConversationResponse

class SummaryResponse(BaseModel):
    timestamp: str
    topics: List[str]
    key_entities: dict
    sentiment: dict
    suggested_actions: List[str]
    next_steps: List[str]

class AnonymizedConversationResponse(BaseModel):
    timestamp: str
    client_id: Optional[str]
    anonymized: bool
    entities: EntityResponse
    topics: List[str]
    sentiment: dict

class HistoryRequest(BaseModel):
    client_id: Optional[str] = None
    limit: int = 10

class HistoryResponse(BaseModel):
    conversations: List[ConversationResponse]

class Frist(BaseModel):
    datum: Optional[str] = None
    beschreibung: Optional[str] = None

class Aufgabe(BaseModel):
    was: Optional[str] = None
    bis_wann: Optional[str] = None

class AnalyzeResponse(BaseModel):
    fristen: List[Frist]
    aufgaben: List[Aufgabe]
    themen: List[str]
    naechste_schritte: List[str]
    timestamp: str

class Vorschlag(BaseModel):
    typ: str
    titel: str
    text: str
    metadata: Dict[str, Any] = {}


class SuggestionsRequest(BaseModel):
    strukturiert: Dict[str, Any]
    mandant_history: List[str] = []


class SuggestionsResponse(BaseModel):
    vorschlaege: List[Vorschlag]


class SuggestionDecisionRequest(BaseModel):
    vorschlag_typ: str
    vorschlag_text: str
    entscheidung: str  # "ja" | "nein" | "geaendert"
    geaenderter_text: Optional[str] = None
    account_id: str = "default"


# Global conversation assistant instance
conversation_assistant = ConversationAssistant()

@router.post("/process", response_model=ConversationResponse)
async def process_transcript(request: TranscriptRequest):
    """
    Process a conversation transcript and extract relevant information
    """
    try:
        result = conversation_assistant.process_transcript(
            request.transcript, 
            request.client_id
        )
        
        # Convert entities to the expected format
        entities = result.get("entities", {})
        entity_response = EntityResponse(
            dates=entities.get("dates", []),
            deadlines=entities.get("deadlines", []),
            persons=entities.get("persons", []),
            legal_terms=entities.get("legal_terms", []),
            actions=entities.get("actions", []),
            amounts=entities.get("amounts", [])
        )
        
        return ConversationResponse(
            timestamp=result.get("timestamp", ""),
            client_id=result.get("client_id"),
            transcript=result.get("transcript", ""),
            entities=entity_response,
            topics=result.get("topics", []),
            sentiment=result.get("sentiment", {})
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error processing transcript: {str(e)}")

@router.post("/analyze", response_model=AnalyzeResponse)
async def analyze_transcript(request: TranscriptRequest):
    """
    Strukturiert ein bereits anonymisiertes Gesprächstranskript über die
    zentrale KI-Schnittstelle (services.llm_client.ask_llm): Fristen,
    Aufgaben, Themen und nächste Schritte.
    """
    try:
        result = conversation_assistant.analyze_transcript(request.transcript)

        return AnalyzeResponse(
            fristen=[Frist(**f) for f in result.get("fristen", [])],
            aufgaben=[Aufgabe(**a) for a in result.get("aufgaben", [])],
            themen=result.get("themen", []),
            naechste_schritte=result.get("naechste_schritte", []),
            timestamp=result.get("timestamp", ""),
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error analyzing transcript: {str(e)}")

@router.post("/suggestions", response_model=SuggestionsResponse)
async def generate_suggestions(request: SuggestionsRequest):
    """
    T#131 (Pilot-Scope): Erzeugt aus dem strukturierten Gesprächsergebnis
    (Ausgabe von /conversation/analyze) konkrete Vorschläge für die nächsten
    Schritte — mindestens Fristennotiz und E-Mail-Entwurf. Der Anwalt
    entscheidet im WPF-Dialog mit Ja/Nein/Geändert (siehe /suggestions/decision).
    """
    try:
        vorschlaege = conversation_assistant.generate_suggestions(
            request.strukturiert, request.mandant_history
        )
        return SuggestionsResponse(vorschlaege=[Vorschlag(**v) for v in vorschlaege])
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error generating suggestions: {str(e)}")

@router.post("/suggestions/decision", status_code=201)
async def submit_suggestion_decision(request: SuggestionDecisionRequest):
    """
    T#131: Nimmt die Anwalt-Entscheidung (Ja/Nein/Geändert) zu einem Post-
    Gespräch-Vorschlag entgegen und speist sie in die BESTEHENDE Lernschleife
    ein — NICHT separat gespeichert:
      - style_learner.tracke_vorschlag_entscheidung(): zählt Ja/Nein/Geändert
        pro Vorschlags-Typ im StyleProfile (style_profiles.vorschlag_feedback_json).
      - Bei "geaendert" zusätzlich feedback_service.process_feedback(): der
        tatsächliche Korrektur-Diff (Vorschlagstext -> geänderter Text) fließt
        wie jede andere Anwalt-Korrektur in feedback_table/style_learner ein.
    """
    entscheidung = (request.entscheidung or "").strip().lower()
    if entscheidung not in GUELTIGE_VORSCHLAG_ENTSCHEIDUNGEN:
        raise HTTPException(
            status_code=400,
            detail=f"Ungültige Entscheidung: {request.entscheidung!r} (erlaubt: ja, nein, geaendert)",
        )
    if entscheidung == "geaendert" and not request.geaenderter_text:
        raise HTTPException(status_code=400, detail="geaenderter_text ist bei 'geaendert' erforderlich")

    try:
        learner = StyleLearner(account_id=request.account_id)
        style_ergebnis = learner.tracke_vorschlag_entscheidung(request.vorschlag_typ, entscheidung)

        feedback_eintrag = None
        if entscheidung == "geaendert":
            try:
                feedback_eintrag = FeedbackService().process_feedback({
                    "original": request.vorschlag_text,
                    "korrigiert": request.geaenderter_text,
                })
            except FeedbackValidationError as e:
                logger.warning(f"Vorschlag-Korrektur von feedback_service abgelehnt: {e}")

        return {
            "status": "gespeichert",
            "style_profile": style_ergebnis,
            "feedback_entry": feedback_eintrag,
        }
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Fehler beim Verarbeiten der Vorschlag-Entscheidung: {e}")
        raise HTTPException(status_code=500, detail="Interner Fehler bei der Entscheidungs-Verarbeitung")

@router.post("/summarize", response_model=SummaryResponse)
async def generate_summary(request: SummaryRequest):
    """
    Generate a summary of a conversation
    """
    try:
        # Convert the request data back to the format expected by the assistant
        conversation_data = {
            "timestamp": request.conversation_data.timestamp,
            "client_id": request.conversation_data.client_id,
            "transcript": request.conversation_data.transcript,
            "entities": {
                "dates": request.conversation_data.entities.dates,
                "deadlines": request.conversation_data.entities.deadlines,
                "persons": request.conversation_data.entities.persons,
                "legal_terms": request.conversation_data.entities.legal_terms,
                "actions": request.conversation_data.entities.actions,
                "amounts": request.conversation_data.entities.amounts
            },
            "topics": request.conversation_data.topics,
            "sentiment": request.conversation_data.sentiment
        }
        
        summary = conversation_assistant.generate_summary(conversation_data)
        
        return SummaryResponse(
            timestamp=summary.get("timestamp", ""),
            topics=summary.get("topics", []),
            key_entities=summary.get("key_entities", {}),
            sentiment=summary.get("sentiment", {}),
            suggested_actions=summary.get("suggested_actions", []),
            next_steps=summary.get("next_steps", [])
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error generating summary: {str(e)}")

@router.post("/anonymize", response_model=AnonymizedConversationResponse)
async def anonymize_conversation(request: ConversationResponse):
    """
    Anonymize conversation data for server transmission
    """
    try:
        # Convert the request data back to the format expected by the assistant
        conversation_data = {
            "timestamp": request.timestamp,
            "client_id": request.client_id,
            "transcript": request.transcript,
            "entities": {
                "dates": request.entities.dates,
                "deadlines": request.entities.deadlines,
                "persons": request.entities.persons,
                "legal_terms": request.entities.legal_terms,
                "actions": request.entities.actions,
                "amounts": request.entities.amounts
            },
            "topics": request.topics,
            "sentiment": request.sentiment
        }
        
        anonymized = conversation_assistant.anonymize_conversation(conversation_data)
        
        # Convert entities to the expected format
        entities = anonymized.get("entities", {})
        entity_response = EntityResponse(
            dates=entities.get("dates", []),
            deadlines=entities.get("deadlines", []),
            persons=entities.get("persons", []),
            legal_terms=entities.get("legal_terms", []),
            actions=entities.get("actions", []),
            amounts=entities.get("amounts", [])
        )
        
        return AnonymizedConversationResponse(
            timestamp=anonymized.get("timestamp", ""),
            client_id=anonymized.get("client_id"),
            anonymized=anonymized.get("anonymized", False),
            entities=entity_response,
            topics=anonymized.get("topics", []),
            sentiment=anonymized.get("sentiment", {})
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error anonymizing conversation: {str(e)}")

@router.post("/history", response_model=HistoryResponse)
async def get_conversation_history(request: HistoryRequest):
    """
    Get conversation history for a client
    """
    try:
        history = conversation_assistant.get_conversation_history(
            request.client_id, 
            request.limit
        )
        
        # Convert history to response format
        conversations = []
        for conv in history:
            entities = conv.get("entities", {})
            entity_response = EntityResponse(
                dates=entities.get("dates", []),
                deadlines=entities.get("deadlines", []),
                persons=entities.get("persons", []),
                legal_terms=entities.get("legal_terms", []),
                actions=entities.get("actions", []),
                amounts=entities.get("amounts", [])
            )
            
            conversations.append(ConversationResponse(
                timestamp=conv.get("timestamp", ""),
                client_id=conv.get("client_id"),
                transcript=conv.get("transcript", ""),
                entities=entity_response,
                topics=conv.get("topics", []),
                sentiment=conv.get("sentiment", {})
            ))
        
        return HistoryResponse(conversations=conversations)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error fetching conversation history: {str(e)}")

@router.get("/health")
async def health_check():
    """
    Health check endpoint
    """
    return {"status": "healthy", "service": "conversation_assistant"}