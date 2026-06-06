from fastapi import APIRouter, HTTPException, Depends, BackgroundTasks
from typing import List, Optional
from pydantic import BaseModel
import logging
from services.email_service import (
    EmailService, EmailConfig, EmailMessage, EmailDraft,
    AutoReplyCase, select_auto_reply_case, build_auto_reply,
    detect_ausschreibung,
)

router = APIRouter(prefix="/email", tags=["email"])

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Pydantic models for request/response
class EmailConfigRequest(BaseModel):
    imap_server: str
    imap_port: int = 993
    smtp_server: str
    smtp_port: int = 587
    username: str
    password: str
    use_ssl: bool = True

class SearchCriteria(BaseModel):
    criteria: List[str] = ['UNSEEN']
    folder: str = "INBOX"
    limit: int = 10

class EmailResponse(BaseModel):
    id: str
    subject: str
    sender: str
    recipients: List[str]
    date: str
    body_preview: str
    has_attachments: bool
    folder: str

class EmailDetailResponse(BaseModel):
    id: str
    subject: str
    sender: str
    recipients: List[str]
    date: str
    body: str
    html_body: Optional[str] = None
    attachments: List[dict] = []
    folder: str

class DraftRequest(BaseModel):
    to: List[str]
    subject: str
    body: str
    html_body: Optional[str] = None
    in_reply_to: Optional[str] = None
    references: Optional[str] = None

class DraftResponse(BaseModel):
    success: bool
    message: str

class EmailAnalysisResponse(BaseModel):
    entities: dict
    summary: str
    suggested_actions: List[str]

# Global email service instance (in production, this would be managed differently)
email_service = None

def get_email_service(config: EmailConfigRequest) -> EmailService:
    """Get email service instance"""
    global email_service
    if email_service is None:
        email_config = EmailConfig(
            imap_server=config.imap_server,
            imap_port=config.imap_port,
            smtp_server=config.smtp_server,
            smtp_port=config.smtp_port,
            username=config.username,
            password=config.password,
            use_ssl=config.use_ssl
        )
        email_service = EmailService(email_config)
    return email_service

@router.post("/configure", response_model=DraftResponse)
async def configure_email(config: EmailConfigRequest):
    """
    Configure email service with IMAP/SMTP settings
    """
    try:
        global email_service
        email_config = EmailConfig(
            imap_server=config.imap_server,
            imap_port=config.imap_port,
            smtp_server=config.smtp_server,
            smtp_port=config.smtp_port,
            username=config.username,
            password=config.password,
            use_ssl=config.use_ssl
        )
        email_service = EmailService(email_config)
        
        # Test connection
        if email_service.connect_imap():
            email_service.disconnect_imap()
            return {"success": True, "message": "Email service configured successfully"}
        else:
            return {"success": False, "message": "Failed to connect to IMAP server"}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Configuration error: {str(e)}")

@router.get("/folders", response_model=List[str])
async def list_folders(config: EmailConfigRequest):
    """
    List all email folders
    """
    try:
        service = get_email_service(config)
        folders = service.list_folders()
        return folders
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error listing folders: {str(e)}")

@router.post("/search", response_model=List[EmailResponse])
async def search_emails(criteria: SearchCriteria, config: EmailConfigRequest):
    """
    Search for emails based on criteria
    """
    try:
        service = get_email_service(config)
        message_ids = service.search_emails(criteria.criteria, criteria.folder)
        
        # Limit results
        limited_ids = message_ids[:criteria.limit] if message_ids else []
        
        # Fetch email details
        emails = service.fetch_emails(limited_ids, criteria.folder)
        
        # Convert to response format
        response_emails = []
        for email_msg in emails:
            response_emails.append(EmailResponse(
                id=email_msg.id,
                subject=email_msg.subject,
                sender=email_msg.sender,
                recipients=email_msg.recipients,
                date=email_msg.date,
                body_preview=email_msg.body[:200] + "..." if len(email_msg.body) > 200 else email_msg.body,
                has_attachments=len(email_msg.attachments) > 0,
                folder=email_msg.folder
            ))
        
        return response_emails
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error searching emails: {str(e)}")

@router.get("/emails/{email_id}", response_model=EmailDetailResponse)
async def get_email(email_id: str, config: EmailConfigRequest, folder: str = "INBOX"):
    """
    Get detailed information about a specific email
    """
    try:
        service = get_email_service(config)
        emails = service.fetch_emails([email_id], folder)
        
        if not emails:
            raise HTTPException(status_code=404, detail="Email not found")
        
        email_msg = emails[0]
        return EmailDetailResponse(
            id=email_msg.id,
            subject=email_msg.subject,
            sender=email_msg.sender,
            recipients=email_msg.recipients,
            date=email_msg.date,
            body=email_msg.body,
            html_body=email_msg.html_body,
            attachments=email_msg.attachments,
            folder=email_msg.folder
        )
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error fetching email: {str(e)}")

@router.post("/drafts", response_model=DraftResponse)
async def create_draft(draft: DraftRequest, config: EmailConfigRequest):
    """
    Create an email draft
    """
    try:
        service = get_email_service(config)
        email_draft = EmailDraft(
            to=draft.to,
            subject=draft.subject,
            body=draft.body,
            html_body=draft.html_body,
            in_reply_to=draft.in_reply_to,
            references=draft.references
        )
        
        success = service.create_draft(email_draft)
        if success:
            return {"success": True, "message": "Draft created successfully"}
        else:
            return {"success": False, "message": "Failed to create draft"}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error creating draft: {str(e)}")

@router.post("/send", response_model=DraftResponse)
async def send_email(draft: DraftRequest, config: EmailConfigRequest):
    """
    Send an email
    """
    try:
        service = get_email_service(config)
        email_draft = EmailDraft(
            to=draft.to,
            subject=draft.subject,
            body=draft.body,
            html_body=draft.html_body,
            in_reply_to=draft.in_reply_to,
            references=draft.references
        )
        
        success = service.send_email(email_draft)
        if success:
            return {"success": True, "message": "Email sent successfully"}
        else:
            return {"success": False, "message": "Failed to send email"}
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error sending email: {str(e)}")

@router.post("/analyze/{email_id}", response_model=EmailAnalysisResponse)
async def analyze_email(email_id: str, config: EmailConfigRequest, folder: str = "INBOX"):
    """
    Analyze an email and extract relevant information
    """
    try:
        service = get_email_service(config)
        emails = service.fetch_emails([email_id], folder)
        
        if not emails:
            raise HTTPException(status_code=404, detail="Email not found")
        
        email_msg = emails[0]
        entities = service.extract_entities(email_msg.body)
        
        # Generate summary and suggestions
        summary = f"Email from {email_msg.sender} regarding {email_msg.subject}"
        suggested_actions = []
        
        if entities["deadlines"]:
            suggested_actions.append(f"Note deadline: {', '.join(entities['deadlines'])}")
        
        if entities["actions"]:
            suggested_actions.append(f"Consider: {', '.join(entities['actions'][:3])}")
        
        if not suggested_actions:
            suggested_actions.append("Review email content and respond as appropriate")
        
        return EmailAnalysisResponse(
            entities=entities,
            summary=summary,
            suggested_actions=suggested_actions
        )
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error analyzing email: {str(e)}")

@router.post("/generate-response/{email_id}", response_model=DraftResponse)
async def generate_response(email_id: str, config: EmailConfigRequest, folder: str = "INBOX", context: str = ""):
    """
    Generate a response draft for an email
    """
    try:
        service = get_email_service(config)
        emails = service.fetch_emails([email_id], folder)
        
        if not emails:
            raise HTTPException(status_code=404, detail="Email not found")
        
        email_msg = emails[0]
        draft = service.generate_response_draft(email_msg, context)
        
        # Create the draft
        success = service.create_draft(draft)
        if success:
            return {"success": True, "message": "Response draft created successfully"}
        else:
            return {"success": False, "message": "Failed to create response draft"}
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Error generating response: {str(e)}")

@router.get("/health")
async def health_check():
    """
    Health check endpoint
    """
    return {"status": "healthy", "service": "email_integration"}


# ─── Auto-Reply endpoints ────────────────────────────────────────────────────

class AutoReplyRequest(BaseModel):
    config: EmailConfigRequest
    recipient: str
    original_subject: str
    original_message_id: Optional[str] = None
    body_text: str = ""
    subscription_status: str  # "ACTIVE" | "INACTIVE" | "UNKNOWN"


class AutoReplyResponse(BaseModel):
    case: str
    success: bool
    subject_sent: str


class TemplatePreviewRequest(BaseModel):
    original_subject: str
    body_text: str = ""
    subscription_status: str  # "ACTIVE" | "INACTIVE" | "UNKNOWN"
    recipient: str = "unknown@example.com"


class TemplatePreviewResponse(BaseModel):
    case: str
    subject: str
    body: str
    html_body: str
    is_ausschreibung: bool


@router.post("/auto-reply", response_model=AutoReplyResponse)
async def send_auto_reply(request: AutoReplyRequest):
    """
    Select the correct auto-reply template for an incoming email and send it.

    Template selection:
    - CASE 1 (BEKANNT_BEZAHLT): known + active subscription + Ausschreibung → confirmation
    - CASE 2 (UNBEKANNT_AUSSCHREIBUNG): unknown sender → free 1-month registration offer
    - CASE 3 (BEKANNT_NICHT_BEZAHLT): known + inactive subscription → reactivation
    - CASE 4 (BEKANNT_KEINE_AUSSCHREIBUNG): known + active + no Ausschreibung → support forward
    """
    try:
        service = get_email_service(request.config)
        result = service.send_auto_reply(
            recipient=request.recipient,
            original_subject=request.original_subject,
            original_message_id=request.original_message_id,
            subscription_status=request.subscription_status,
            body_text=request.body_text,
        )
        return AutoReplyResponse(**result)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Auto-reply error: {str(e)}")


@router.post("/auto-reply/preview", response_model=TemplatePreviewResponse)
async def preview_auto_reply_template(request: TemplatePreviewRequest):
    """
    Preview which template would be selected and its rendered content — no email is sent.
    """
    try:
        case = select_auto_reply_case(
            sender_email=request.recipient,
            subject=request.original_subject,
            body=request.body_text,
            subscription_status=request.subscription_status,
        )
        rendered = build_auto_reply(case, request.original_subject)
        is_ausschreibung = detect_ausschreibung(request.original_subject, request.body_text)
        return TemplatePreviewResponse(
            case=case.value,
            subject=rendered["subject"],
            body=rendered["body"],
            html_body=rendered["html_body"],
            is_ausschreibung=is_ausschreibung,
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Preview error: {str(e)}")


@router.get("/auto-reply/templates")
async def list_auto_reply_templates():
    """
    List all available auto-reply templates with their trigger conditions.
    """
    return {
        "templates": [
            {
                "case": AutoReplyCase.BEKANNT_BEZAHLT.value,
                "trigger": "Known sender with active subscription + Ausschreibung detected",
                "description": "Confirmation: offer is being processed"
            },
            {
                "case": AutoReplyCase.UNBEKANNT_AUSSCHREIBUNG.value,
                "trigger": "Unknown sender (not in DB)",
                "description": "Registration offer: 1 month free trial"
            },
            {
                "case": AutoReplyCase.BEKANNT_NICHT_BEZAHLT.value,
                "trigger": "Known sender with inactive/expired subscription",
                "description": "Reactivation: account paused"
            },
            {
                "case": AutoReplyCase.BEKANNT_KEINE_AUSSCHREIBUNG.value,
                "trigger": "Known sender with active subscription + no Ausschreibung",
                "description": "Support-Forward: ticket created"
            },
        ]
    }