import imapclient
import email
from email.mime.text import MIMEText
from email.mime.multipart import MIMEMultipart
from email.header import decode_header
import smtplib
import ssl
from typing import List, Dict, Optional
import logging
from pydantic import BaseModel
import json
import base64
from datetime import datetime
import re

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class EmailConfig(BaseModel):
    """Email configuration model"""
    imap_server: str
    imap_port: int = 993
    smtp_server: str
    smtp_port: int = 587
    username: str
    password: str
    use_ssl: bool = True

class EmailMessage(BaseModel):
    """Email message model"""
    id: str
    subject: str
    sender: str
    recipients: List[str]
    date: str
    body: str
    html_body: Optional[str] = None
    attachments: List[Dict] = []
    folder: str = "INBOX"

class EmailDraft(BaseModel):
    """Email draft model"""
    to: List[str]
    subject: str
    body: str
    html_body: Optional[str] = None
    in_reply_to: Optional[str] = None
    references: Optional[str] = None

class EmailService:
    """Service for handling email integration (IMAP/SMTP)"""
    
    def __init__(self, config: EmailConfig):
        self.config = config
        self.imap_client = None
        self.smtp_client = None
    
    def connect_imap(self):
        """Connect to IMAP server"""
        try:
            self.imap_client = imapclient.IMAPClient(
                self.config.imap_server, 
                port=self.config.imap_port, 
                ssl=self.config.use_ssl
            )
            self.imap_client.login(self.config.username, self.config.password)
            logger.info(f"Connected to IMAP server {self.config.imap_server}")
            return True
        except Exception as e:
            logger.error(f"Failed to connect to IMAP server: {e}")
            return False
    
    def disconnect_imap(self):
        """Disconnect from IMAP server"""
        if self.imap_client:
            try:
                self.imap_client.logout()
                logger.info("Disconnected from IMAP server")
            except Exception as e:
                logger.error(f"Error disconnecting from IMAP server: {e}")
    
    def connect_smtp(self):
        """Connect to SMTP server"""
        try:
            context = ssl.create_default_context()
            self.smtp_client = smtplib.SMTP(self.config.smtp_server, self.config.smtp_port)
            self.smtp_client.starttls(context=context)
            self.smtp_client.login(self.config.username, self.config.password)
            logger.info(f"Connected to SMTP server {self.config.smtp_server}")
            return True
        except Exception as e:
            logger.error(f"Failed to connect to SMTP server: {e}")
            return False
    
    def disconnect_smtp(self):
        """Disconnect from SMTP server"""
        if self.smtp_client:
            try:
                self.smtp_client.quit()
                logger.info("Disconnected from SMTP server")
            except Exception as e:
                logger.error(f"Error disconnecting from SMTP server: {e}")
    
    def list_folders(self) -> List[str]:
        """List all email folders"""
        if not self.imap_client:
            if not self.connect_imap():
                return []
        
        try:
            folders = self.imap_client.list_folders()
            return [folder[2] for folder in folders]
        except Exception as e:
            logger.error(f"Error listing folders: {e}")
            return []
    
    def select_folder(self, folder: str = "INBOX") -> bool:
        """Select an email folder"""
        if not self.imap_client:
            if not self.connect_imap():
                return False
        
        try:
            self.imap_client.select_folder(folder)
            logger.info(f"Selected folder: {folder}")
            return True
        except Exception as e:
            logger.error(f"Error selecting folder {folder}: {e}")
            return False
    
    def search_emails(self, criteria: List[str] = ['UNSEEN'], folder: str = "INBOX") -> List[str]:
        """Search for emails based on criteria"""
        if not self.select_folder(folder):
            return []
        
        try:
            messages = self.imap_client.search(criteria)
            logger.info(f"Found {len(messages)} messages matching criteria")
            return messages
        except Exception as e:
            logger.error(f"Error searching emails: {e}")
            return []
    
    def fetch_emails(self, message_ids: List[str], folder: str = "INBOX") -> List[EmailMessage]:
        """Fetch email messages by IDs"""
        if not self.select_folder(folder):
            return []
        
        emails = []
        try:
            # Fetch message data
            messages = self.imap_client.fetch(message_ids, ['RFC822'])
            
            for msg_id, message_data in messages.items():
                try:
                    email_message = email.message_from_bytes(message_data[b'RFC822'])
                    
                    # Extract basic information
                    subject = self._decode_header(email_message.get('Subject', ''))
                    sender = email_message.get('From', '')
                    recipients = email_message.get('To', '').split(',')
                    date = email_message.get('Date', '')
                    
                    # Extract body
                    body, html_body = self._extract_body(email_message)
                    
                    # Extract attachments
                    attachments = self._extract_attachments(email_message)
                    
                    email_obj = EmailMessage(
                        id=str(msg_id),
                        subject=subject,
                        sender=sender,
                        recipients=[r.strip() for r in recipients],
                        date=date,
                        body=body,
                        html_body=html_body,
                        attachments=attachments,
                        folder=folder
                    )
                    
                    emails.append(email_obj)
                except Exception as e:
                    logger.error(f"Error processing message {msg_id}: {e}")
                    continue
            
            logger.info(f"Fetched {len(emails)} emails")
            return emails
        except Exception as e:
            logger.error(f"Error fetching emails: {e}")
            return []
    
    def _decode_header(self, header: str) -> str:
        """Decode email header"""
        if not header:
            return ""
        
        try:
            decoded_parts = decode_header(header)
            decoded_string = ""
            for part, encoding in decoded_parts:
                if isinstance(part, bytes):
                    if encoding:
                        decoded_string += part.decode(encoding)
                    else:
                        decoded_string += part.decode('utf-8', errors='ignore')
                else:
                    decoded_string += part
            return decoded_string
        except Exception as e:
            logger.error(f"Error decoding header: {e}")
            return header
    
    def _extract_body(self, email_message) -> tuple[str, Optional[str]]:
        """Extract text and HTML body from email"""
        text_body = ""
        html_body = None
        
        if email_message.is_multipart():
            for part in email_message.walk():
                content_type = part.get_content_type()
                content_disposition = str(part.get("Content-Disposition"))
                
                # Skip attachments
                if "attachment" in content_disposition:
                    continue
                
                try:
                    body = part.get_payload(decode=True)
                    if body:
                        body = body.decode('utf-8', errors='ignore')
                        if content_type == "text/plain":
                            text_body = body
                        elif content_type == "text/html":
                            html_body = body
                except Exception as e:
                    logger.error(f"Error extracting body part: {e}")
        else:
            try:
                body = email_message.get_payload(decode=True)
                if body:
                    body = body.decode('utf-8', errors='ignore')
                    if email_message.get_content_type() == "text/plain":
                        text_body = body
                    elif email_message.get_content_type() == "text/html":
                        html_body = body
            except Exception as e:
                logger.error(f"Error extracting body: {e}")
        
        return text_body, html_body
    
    def _extract_attachments(self, email_message) -> List[Dict]:
        """Extract attachments from email"""
        attachments = []
        
        if email_message.is_multipart():
            for part in email_message.walk():
                content_disposition = str(part.get("Content-Disposition"))
                
                if "attachment" in content_disposition:
                    filename = part.get_filename()
                    if filename:
                        try:
                            file_data = part.get_payload(decode=True)
                            if file_data:
                                # Encode file data as base64 for storage
                                encoded_data = base64.b64encode(file_data).decode('utf-8')
                                attachments.append({
                                    "filename": filename,
                                    "content_type": part.get_content_type(),
                                    "data": encoded_data,
                                    "size": len(file_data)
                                })
                        except Exception as e:
                            logger.error(f"Error extracting attachment {filename}: {e}")
        
        return attachments
    
    def create_draft(self, draft: EmailDraft) -> bool:
        """Create an email draft in the Drafts folder"""
        if not self.connect_smtp():
            return False
        
        try:
            # Create message
            msg = MIMEMultipart('alternative')
            msg['From'] = self.config.username
            msg['To'] = ', '.join(draft.to)
            msg['Subject'] = draft.subject
            
            if draft.in_reply_to:
                msg['In-Reply-To'] = draft.in_reply_to
                msg['References'] = draft.references or draft.in_reply_to
            
            # Add text body
            text_part = MIMEText(draft.body, 'plain', 'utf-8')
            msg.attach(text_part)
            
            # Add HTML body if provided
            if draft.html_body:
                html_part = MIMEText(draft.html_body, 'html', 'utf-8')
                msg.attach(html_part)
            
            # Send to Drafts folder via SMTP (this is a simplified approach)
            # In a real implementation, you would use IMAP to save to Drafts folder
            logger.info(f"Created draft email to {draft.to} with subject: {draft.subject}")
            return True
        except Exception as e:
            logger.error(f"Error creating draft: {e}")
            return False
    
    def send_email(self, draft: EmailDraft) -> bool:
        """Send an email"""
        if not self.connect_smtp():
            return False
        
        try:
            # Create message
            msg = MIMEMultipart('alternative')
            msg['From'] = self.config.username
            msg['To'] = ', '.join(draft.to)
            msg['Subject'] = draft.subject
            
            if draft.in_reply_to:
                msg['In-Reply-To'] = draft.in_reply_to
                msg['References'] = draft.references or draft.in_reply_to
            
            # Add text body
            text_part = MIMEText(draft.body, 'plain', 'utf-8')
            msg.attach(text_part)
            
            # Add HTML body if provided
            if draft.html_body:
                html_part = MIMEText(draft.html_body, 'html', 'utf-8')
                msg.attach(html_part)
            
            # Send email
            text = msg.as_string()
            self.smtp_client.sendmail(self.config.username, draft.to, text)
            logger.info(f"Sent email to {draft.to} with subject: {draft.subject}")
            return True
        except Exception as e:
            logger.error(f"Error sending email: {e}")
            return False
    
    def extract_entities(self, email_text: str) -> Dict:
        """Extract relevant entities from email text"""
        entities = {
            "dates": [],
            "deadlines": [],
            "persons": [],
            "actions": [],
            "legal_terms": []
        }
        
        # Extract dates (simple pattern matching)
        date_patterns = [
            r'\d{1,2}\.\d{1,2}\.\d{4}',
            r'\d{1,2}\.\d{1,2}\.\d{2}',
            r'(?:Montag|Dienstag|Mittwoch|Donnerstag|Freitag|Samstag|Sonntag),?\s+\d{1,2}\.\d{1,2}\.\d{4}'
        ]
        
        for pattern in date_patterns:
            matches = re.findall(pattern, email_text)
            entities["dates"].extend(matches)
        
        # Extract deadlines
        deadline_patterns = [
            r'(?:bis|Bis)\s+(?:zum\s+)?(\d{1,2}\.\d{1,2}\.\d{4})',
            r'(?:Frist|frist)\s+(?:bis\s+)?(\d{1,2}\.\d{1,2}\.\d{4})'
        ]
        
        for pattern in deadline_patterns:
            matches = re.findall(pattern, email_text)
            entities["deadlines"].extend(matches)
        
        # Extract legal terms (simple list)
        legal_terms = [
            "Klage", "Kündigung", "Vertrag", "Mietvertrag", "Arbeitsvertrag",
            "Abmahnung", "Mahnbescheid", "Gericht", "Urteil", "Beschluss",
            "Anwalt", "Recht", "Gesetz", "Verordnung", "Vertrag"
        ]
        
        for term in legal_terms:
            if term.lower() in email_text.lower():
                entities["legal_terms"].append(term)
        
        # Extract action items (simple pattern matching)
        action_patterns = [
            r'(?:muss|soll|kann)\s+(?:ich|wir)\s+([^.,;!?]+)',
            r'(?:bitte|Bitte)\s+([^.,;!?]+)',
            r'(?:benötige|brauche)\s+([^.,;!?]+)'
        ]
        
        for pattern in action_patterns:
            matches = re.findall(pattern, email_text)
            entities["actions"].extend(matches)
        
        return entities
    
    def generate_response_draft(self, email_message: EmailMessage, context: str = "") -> EmailDraft:
        """Generate a response draft based on the email and context"""
        # This is a simplified implementation
        # In a real implementation, this would use an AI model to generate the response
        
        response_body = f"""Sehr geehrte Damen und Herren,

vielen Dank für Ihre E-Mail vom {email_message.date}.

{context if context else 'Wir haben Ihre Anfrage erhalten und werden uns in Kürze bei Ihnen melden.'}

Mit freundlichen Grüßen

[Ihr Name]
Rechtsanwalt"""

        return EmailDraft(
            to=[email_message.sender],
            subject=f"Re: {email_message.subject}",
            body=response_body
        )

# Example usage
if __name__ == "__main__":
    # Example configuration (would come from environment variables in production)
    config = EmailConfig(
        imap_server="imap.gmail.com",
        smtp_server="smtp.gmail.com",
        username="your-email@gmail.com",
        password="your-password"
    )
    
    email_service = EmailService(config)
    
    # Example: Search for unread emails
    message_ids = email_service.search_emails(['UNSEEN'])
    if message_ids:
        # Fetch the emails
        emails = email_service.fetch_emails(message_ids[:5])  # Fetch first 5
        for email_msg in emails:
            print(f"Subject: {email_msg.subject}")
            print(f"From: {email_msg.sender}")
            print(f"Body preview: {email_msg.body[:100]}...")
            print("-" * 50)