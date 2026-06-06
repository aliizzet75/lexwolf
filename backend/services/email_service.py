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
import enum

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# ─── Auto-Reply Templates ───────────────────────────────────────────────────

class AutoReplyCase(str, enum.Enum):
    BEKANNT_BEZAHLT = "BEKANNT_BEZAHLT"           # Case 1: known + paid
    UNBEKANNT_AUSSCHREIBUNG = "UNBEKANNT_AUSSCHREIBUNG"  # Case 2: unknown + tender
    BEKANNT_NICHT_BEZAHLT = "BEKANNT_NICHT_BEZAHLT"     # Case 3: known + not paid
    BEKANNT_KEINE_AUSSCHREIBUNG = "BEKANNT_KEINE_AUSSCHREIBUNG"  # Case 4: known + no tender


AUTO_REPLY_TEMPLATES: Dict[AutoReplyCase, Dict[str, str]] = {
    AutoReplyCase.BEKANNT_BEZAHLT: {
        "subject": "Re: {original_subject} – Ihr Angebot wird bearbeitet",
        "body": """Sehr geehrte Damen und Herren,

vielen Dank für Ihre Anfrage.

Wir haben Ihre Ausschreibungsunterlagen erhalten und bestätigen die Eingang Ihres Angebots. Unser Team bearbeitet Ihre Anfrage umgehend und wird sich in Kürze mit einem detaillierten Angebot bei Ihnen melden.

Als geschätzter Kunde profitieren Sie von unserer bevorzugten Bearbeitung.

Mit freundlichen Grüßen
Ihr LexWolf-Team""",
        "html_body": """<p>Sehr geehrte Damen und Herren,</p>
<p>vielen Dank für Ihre Anfrage.</p>
<p>Wir haben Ihre Ausschreibungsunterlagen erhalten und <strong>bestätigen den Eingang Ihres Angebots</strong>. Unser Team bearbeitet Ihre Anfrage umgehend und wird sich in Kürze mit einem detaillierten Angebot bei Ihnen melden.</p>
<p>Als geschätzter Kunde profitieren Sie von unserer bevorzugten Bearbeitung.</p>
<p>Mit freundlichen Grüßen<br>Ihr <strong>LexWolf-Team</strong></p>"""
    },
    AutoReplyCase.UNBEKANNT_AUSSCHREIBUNG: {
        "subject": "Re: {original_subject} – Kostenloser Testzugang für 1 Monat",
        "body": """Sehr geehrte Damen und Herren,

vielen Dank für Ihr Interesse an LexWolf.

Wir haben Ihre Ausschreibungsanfrage erhalten und freuen uns, Ihnen ein besonderes Angebot machen zu können: Registrieren Sie sich jetzt bei LexWolf und erhalten Sie einen kostenlosen Testzugang für 1 Monat.

Mit LexWolf haben Sie Zugriff auf:
- Umfassende Rechtsdatenbank
- KI-gestützte Dokumentenanalyse
- Automatische Ausschreibungsverarbeitung

Zur Registrierung besuchen Sie bitte: https://lexwolf.de/registrierung

Unser Team hilft Ihnen gerne bei der Einrichtung Ihres Accounts.

Mit freundlichen Grüßen
Ihr LexWolf-Team""",
        "html_body": """<p>Sehr geehrte Damen und Herren,</p>
<p>vielen Dank für Ihr Interesse an LexWolf.</p>
<p>Wir haben Ihre Ausschreibungsanfrage erhalten und freuen uns, Ihnen ein besonderes Angebot machen zu können: <strong>Registrieren Sie sich jetzt bei LexWolf und erhalten Sie einen kostenlosen Testzugang für 1 Monat.</strong></p>
<p>Mit LexWolf haben Sie Zugriff auf:</p>
<ul>
  <li>Umfassende Rechtsdatenbank</li>
  <li>KI-gestützte Dokumentenanalyse</li>
  <li>Automatische Ausschreibungsverarbeitung</li>
</ul>
<p>Zur Registrierung besuchen Sie bitte: <a href="https://lexwolf.de/registrierung">https://lexwolf.de/registrierung</a></p>
<p>Unser Team hilft Ihnen gerne bei der Einrichtung Ihres Accounts.</p>
<p>Mit freundlichen Grüßen<br>Ihr <strong>LexWolf-Team</strong></p>"""
    },
    AutoReplyCase.BEKANNT_NICHT_BEZAHLT: {
        "subject": "Re: {original_subject} – Ihr Account wurde pausiert",
        "body": """Sehr geehrte Damen und Herren,

vielen Dank für Ihre Nachricht.

Wir haben festgestellt, dass Ihr LexWolf-Account derzeit pausiert ist, da kein aktives Abonnement vorliegt. Um Ihre Anfrage bearbeiten zu können, ist eine Reaktivierung Ihres Accounts erforderlich.

Reaktivieren Sie Ihren Account jetzt und profitieren Sie von allen LexWolf-Funktionen:
- Sofortiger Zugang nach Reaktivierung
- Nahtlose Fortsetzung Ihrer bisherigen Arbeit
- Zugriff auf alle gespeicherten Dokumente

Zur Reaktivierung wenden Sie sich bitte an: support@lexwolf.de oder besuchen Sie https://lexwolf.de/reaktivierung

Mit freundlichen Grüßen
Ihr LexWolf-Team""",
        "html_body": """<p>Sehr geehrte Damen und Herren,</p>
<p>vielen Dank für Ihre Nachricht.</p>
<p>Wir haben festgestellt, dass Ihr LexWolf-Account derzeit <strong>pausiert</strong> ist, da kein aktives Abonnement vorliegt. Um Ihre Anfrage bearbeiten zu können, ist eine Reaktivierung Ihres Accounts erforderlich.</p>
<p>Reaktivieren Sie Ihren Account jetzt und profitieren Sie von allen LexWolf-Funktionen:</p>
<ul>
  <li>Sofortiger Zugang nach Reaktivierung</li>
  <li>Nahtlose Fortsetzung Ihrer bisherigen Arbeit</li>
  <li>Zugriff auf alle gespeicherten Dokumente</li>
</ul>
<p>Zur Reaktivierung wenden Sie sich bitte an: <a href="mailto:support@lexwolf.de">support@lexwolf.de</a> oder besuchen Sie <a href="https://lexwolf.de/reaktivierung">https://lexwolf.de/reaktivierung</a></p>
<p>Mit freundlichen Grüßen<br>Ihr <strong>LexWolf-Team</strong></p>"""
    },
    AutoReplyCase.BEKANNT_KEINE_AUSSCHREIBUNG: {
        "subject": "Re: {original_subject} – Ihre Anfrage wurde an den Support weitergeleitet",
        "body": """Sehr geehrte Damen und Herren,

vielen Dank für Ihre Nachricht.

Wir haben Ihre Anfrage erhalten und an unser Support-Team weitergeleitet. Ein Mitarbeiter wird sich so schnell wie möglich bei Ihnen melden.

Referenznummer Ihrer Anfrage: {reference_id}

Für dringende Angelegenheiten erreichen Sie uns unter:
- E-Mail: support@lexwolf.de
- Telefon: +49 (0) 30 123456789

Mit freundlichen Grüßen
Ihr LexWolf-Team""",
        "html_body": """<p>Sehr geehrte Damen und Herren,</p>
<p>vielen Dank für Ihre Nachricht.</p>
<p>Wir haben Ihre Anfrage erhalten und an unser <strong>Support-Team weitergeleitet</strong>. Ein Mitarbeiter wird sich so schnell wie möglich bei Ihnen melden.</p>
<p>Referenznummer Ihrer Anfrage: <strong>{reference_id}</strong></p>
<p>Für dringende Angelegenheiten erreichen Sie uns unter:</p>
<ul>
  <li>E-Mail: <a href="mailto:support@lexwolf.de">support@lexwolf.de</a></li>
  <li>Telefon: +49 (0) 30 123456789</li>
</ul>
<p>Mit freundlichen Grüßen<br>Ihr <strong>LexWolf-Team</strong></p>"""
    },
}

AUSSCHREIBUNG_KEYWORDS = [
    "ausschreibung", "vergabe", "vergabeverfahren", "tender", "angebot",
    "ausschreibungsunterlagen", "leistungsverzeichnis", "bieter", "submission",
    "rfp", "request for proposal", "angebotsanfrage", "angebotsaufforderung",
    "public tender", "öffentliche ausschreibung", "beschaffung",
]


def detect_ausschreibung(subject: str, body: str) -> bool:
    """Return True if the email appears to be about a tender/Ausschreibung."""
    text = (subject + " " + body).lower()
    return any(kw in text for kw in AUSSCHREIBUNG_KEYWORDS)


def select_auto_reply_case(
    sender_email: str,
    subject: str,
    body: str,
    subscription_status: str,  # "ACTIVE", "INACTIVE", "UNKNOWN"
) -> AutoReplyCase:
    """
    Determine the correct auto-reply case based on sender status and email content.

    Decision logic:
      UNKNOWN + Ausschreibung  → CASE 2
      UNKNOWN + no Ausschreibung → CASE 2 (still try to onboard)
      KNOWN  + ACTIVE + Ausschreibung → CASE 1
      KNOWN  + ACTIVE + no Ausschreibung → CASE 4 (support forward)
      KNOWN  + INACTIVE → CASE 3
    """
    is_known = subscription_status != "UNKNOWN"
    is_paid = subscription_status == "ACTIVE"
    is_ausschreibung = detect_ausschreibung(subject, body)

    if not is_known:
        return AutoReplyCase.UNBEKANNT_AUSSCHREIBUNG

    if not is_paid:
        return AutoReplyCase.BEKANNT_NICHT_BEZAHLT

    if is_ausschreibung:
        return AutoReplyCase.BEKANNT_BEZAHLT

    return AutoReplyCase.BEKANNT_KEINE_AUSSCHREIBUNG


def build_auto_reply(
    case: AutoReplyCase,
    original_subject: str,
    reference_id: Optional[str] = None,
) -> Dict[str, str]:
    """Render a template for the given case and return subject/body/html_body."""
    template = AUTO_REPLY_TEMPLATES[case]
    ref = reference_id or datetime.utcnow().strftime("LW-%Y%m%d-%H%M%S")
    subject = template["subject"].format(original_subject=original_subject)
    body = template["body"].format(original_subject=original_subject, reference_id=ref)
    html_body = template["html_body"].format(original_subject=original_subject, reference_id=ref)
    return {"subject": subject, "body": body, "html_body": html_body}

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
    
    def send_auto_reply(
        self,
        recipient: str,
        original_subject: str,
        original_message_id: Optional[str],
        subscription_status: str,
        body_text: str = "",
    ) -> Dict:
        """
        Select the correct auto-reply template, render it, send via SMTP,
        and return the case + success flag.
        """
        case = select_auto_reply_case(recipient, original_subject, body_text, subscription_status)
        rendered = build_auto_reply(case, original_subject)

        draft = EmailDraft(
            to=[recipient],
            subject=rendered["subject"],
            body=rendered["body"],
            html_body=rendered["html_body"],
            in_reply_to=original_message_id,
            references=original_message_id,
        )

        success = self.send_email(draft)
        return {"case": case.value, "success": success, "subject_sent": rendered["subject"]}

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