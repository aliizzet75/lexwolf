from fastapi import APIRouter, HTTPException, Depends, BackgroundTasks
from typing import List, Optional
from pydantic import BaseModel
import logging
from services.email_service import EmailService, EmailConfig, EmailMessage, EmailDraft

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