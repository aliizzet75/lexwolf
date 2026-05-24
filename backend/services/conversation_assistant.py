import json
import re
from datetime import datetime
from typing import Dict, List, Optional
import logging

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class ConversationAssistant:
    """Service for handling conversation analysis and assistance"""
    
    def __init__(self):
        self.conversation_history = []
    
    def process_transcript(self, transcript: str, client_id: str = None) -> Dict:
        """
        Process a conversation transcript and extract relevant information
        """
        timestamp = datetime.now().isoformat()
        
        # Store conversation data
        conversation_entry = {
            "timestamp": timestamp,
            "client_id": client_id,
            "transcript": transcript,
            "entities": self._extract_entities(transcript),
            "topics": self._extract_topics(transcript),
            "sentiment": self._analyze_sentiment(transcript)
        }
        
        self.conversation_history.append(conversation_entry)
        
        return conversation_entry
    
    def _extract_entities(self, transcript: str) -> Dict:
        """
        Extract entities from conversation transcript
        """
        entities = {
            "dates": self._extract_dates(transcript),
            "deadlines": self._extract_deadlines(transcript),
            "persons": self._extract_persons(transcript),
            "legal_terms": self._extract_legal_terms(transcript),
            "actions": self._extract_actions(transcript),
            "amounts": self._extract_amounts(transcript)
        }
        
        return entities
    
    def _extract_dates(self, text: str) -> List[str]:
        """
        Extract dates from text
        """
        date_patterns = [
            r"\b\d{1,2}\.\d{1,2}\.\d{4}\b",  # DD.MM.YYYY
            r"\b\d{1,2}\.\s*(Januar|Februar|März|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember)\s*\d{4}\b",  # D. Month YYYY
            r"\b\d{4}-\d{1,2}-\d{1,2}\b"  # YYYY-MM-DD
        ]
        
        dates = []
        for pattern in date_patterns:
            matches = re.findall(pattern, text, re.IGNORECASE)
            dates.extend([match if isinstance(match, str) else match[0] for match in matches])
        
        return list(set(dates))  # Remove duplicates
    
    def _extract_deadlines(self, text: str) -> List[str]:
        """
        Extract deadlines and timeframes from text
        """
        deadline_patterns = [
            r"(?:in\s+)?(\d+)\s+(Tagen|Wochen|Monaten)(?:\s+Frist)?",
            r"(?:bis\s+(?:zum\s+)?)?(\d{1,2}\.\d{1,2}\.\d{4})",
            r"(?:spätestens\s+(?:am\s+)?)?(\d{1,2}\.\d{1,2}\.\d{4})",
            r"(?:innerhalb\s+(?:von\s+)?)?(\d+)\s+(Tagen|Wochen|Monaten)"
        ]
        
        deadlines = []
        for pattern in deadline_patterns:
            matches = re.findall(pattern, text, re.IGNORECASE)
            for match in matches:
                if isinstance(match, tuple):
                    deadlines.append(" ".join(match))
                else:
                    deadlines.append(match)
        
        return list(set(deadlines))  # Remove duplicates
    
    def _extract_persons(self, text: str) -> List[str]:
        """
        Extract person references from text
        """
        # Simple pattern for person references
        person_patterns = [
            r"(Herr|Frau)\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?",
            r"Mandant(?:en)?\s+[A-Z][a-z]+",
            r"Kläger\s+[A-Z][a-z]+",
            r"Anwalt\s+[A-Z][a-z]+"
        ]
        
        persons = []
        for pattern in person_patterns:
            matches = re.findall(pattern, text)
            persons.extend(matches)
        
        return list(set(persons))  # Remove duplicates
    
    def _extract_legal_terms(self, text: str) -> List[str]:
        """
        Extract legal terms from text
        """
        legal_terms = [
            "Klage", "Kündigung", "Vertrag", "Mietvertrag", "Arbeitsvertrag",
            "Abmahnung", "Mahnbescheid", "Gericht", "Urteil", "Beschluss",
            "Anwalt", "Recht", "Gesetz", "Verordnung", "Vertrag",
            "Kündigungsschutz", "Mietrecht", "Familienrecht", "Arbeitsrecht",
            "Strafrecht", "Verkehrsrecht"
        ]
        
        found_terms = []
        text_lower = text.lower()
        for term in legal_terms:
            if term.lower() in text_lower:
                found_terms.append(term)
        
        return list(set(found_terms))  # Remove duplicates
    
    def _extract_actions(self, text: str) -> List[str]:
        """
        Extract action items from text
        """
        action_keywords = [
            "müssen", "sollen", "werden", "muss", "soll",
            "beantragen", "einreichen", "versenden", "erstellen",
            "vorbereiten", "prüfen", "überprüfen", "anfordern",
            "zahlen", "erhalten", "erledigen", "besprechen"
        ]
        
        actions = []
        sentences = re.split(r'[.!?]+', text)
        for sentence in sentences:
            sentence = sentence.strip()
            if any(keyword in sentence.lower() for keyword in action_keywords):
                actions.append(sentence)
        
        return actions
    
    def _extract_amounts(self, text: str) -> List[str]:
        """
        Extract monetary amounts from text
        """
        amount_patterns = [
            r"(\d+(?:\.\d{3})*(?:,\d{2})?)\s*€",
            r"(\d+(?:\.\d{3})*(?:,\d{2})?)\s*Euro",
            r"(\d+(?:\.\d{3})*(?:,\d{2})?)\s*EUR"
        ]
        
        amounts = []
        for pattern in amount_patterns:
            matches = re.findall(pattern, text, re.IGNORECASE)
            amounts.extend(matches)
        
        return list(set(amounts))  # Remove duplicates
    
    def _extract_topics(self, text: str) -> List[str]:
        """
        Extract main topics from conversation
        """
        topics = []
        text_lower = text.lower()
        
        topic_keywords = {
            "Kündigung": ["kündigung", "arbeitsvertrag", "betriebsrat", "kündigungsschutz"],
            "Mietrecht": ["miete", "vermieter", "mieter", "wohnung", "mietvertrag"],
            "Familienrecht": ["scheidung", "unterhalt", "sorgerecht", "ehescheidung"],
            "Verkehrsrecht": ["unfall", "versicherung", "fahrzeug", "verkehrsunfall"],
            "Strafrecht": ["strafverfahren", "anzeige", "verteidigung", "strafe"],
            "Vertragsrecht": ["vertrag", "vertragsverhältnis", "vertragspartner"],
            "Arbeitsrecht": ["arbeitsvertrag", "kündigung", "urlaub", "lohn"],
            "Versicherungsrecht": ["versicherung", "versicherungsfall", "versicherungsnehmer"]
        }
        
        for topic, keywords in topic_keywords.items():
            if any(keyword in text_lower for keyword in keywords):
                topics.append(topic)
        
        return topics if topics else ["Allgemeines Rechtsgespräch"]
    
    def _analyze_sentiment(self, text: str) -> Dict:
        """
        Simple sentiment analysis for conversation tone
        """
        # Simple keyword-based sentiment analysis
        positive_keywords = ["zufrieden", "gut", "einverstanden", "akzeptabel", "positiv"]
        negative_keywords = ["unzufrieden", "schlecht", "problem", "beschwerde", "negativ", "ärgerlich"]
        neutral_keywords = ["verstanden", "notiert", "danke", "bitte"]
        
        text_lower = text.lower()
        positive_count = sum(1 for keyword in positive_keywords if keyword in text_lower)
        negative_count = sum(1 for keyword in negative_keywords if keyword in text_lower)
        neutral_count = sum(1 for keyword in neutral_keywords if keyword in text_lower)
        
        if positive_count > negative_count and positive_count > neutral_count:
            sentiment = "positiv"
        elif negative_count > positive_count and negative_count > neutral_count:
            sentiment = "negativ"
        else:
            sentiment = "neutral"
        
        return {
            "tone": sentiment,
            "positive_indicators": positive_count,
            "negative_indicators": negative_count,
            "neutral_indicators": neutral_count
        }
    
    def generate_summary(self, conversation_data: Dict) -> Dict:
        """
        Generate conversation summary
        """
        entities = conversation_data.get("entities", {})
        topics = conversation_data.get("topics", [])
        sentiment = conversation_data.get("sentiment", {})
        
        summary = {
            "timestamp": conversation_data.get("timestamp"),
            "topics": topics,
            "key_entities": {
                "dates": entities.get("dates", [])[:5],  # Limit to 5 dates
                "deadlines": entities.get("deadlines", [])[:5],  # Limit to 5 deadlines
                "persons": entities.get("persons", [])[:5],  # Limit to 5 persons
                "legal_terms": entities.get("legal_terms", [])[:10],  # Limit to 10 terms
                "amounts": entities.get("amounts", [])[:5]  # Limit to 5 amounts
            },
            "sentiment": sentiment,
            "suggested_actions": self._generate_suggestions(conversation_data),
            "next_steps": self._generate_next_steps(conversation_data)
        }
        
        return summary
    
    def _generate_suggestions(self, conversation_data: Dict) -> List[str]:
        """
        Generate suggestions based on conversation content
        """
        transcript = conversation_data.get("transcript", "")
        topics = conversation_data.get("topics", [])
        entities = conversation_data.get("entities", {})
        
        suggestions = []
        
        # Topic-based suggestions
        if "Kündigung" in topics:
            suggestions.append("📄 Soll ich eine Kündigungsschutzklage vorbereiten?")
        
        if "Mietrecht" in topics:
            suggestions.append("📄 Soll ich einen Mietvertrag entwerfen?")
        
        if "Familienrecht" in topics:
            suggestions.append("📄 Soll ich eine Scheidungsfolgenvereinbarung vorbereiten?")
        
        if "Vertrag" in entities.get("legal_terms", []):
            suggestions.append("📄 Soll ich einen Vertragsentwurf erstellen?")
        
        # Deadline-based suggestions
        if entities.get("deadlines"):
            suggestions.append("📅 Soll ich einen Kalender-Eintrag für die Frist erstellen?")
        
        # General suggestions
        suggestions.append("📧 Soll ich dem Mandanten eine Zusammenfassung schicken?")
        suggestions.append("📝 Soll ich Notizen aus dem Gespräch strukturieren?")
        
        return suggestions
    
    def _generate_next_steps(self, conversation_data: Dict) -> List[str]:
        """
        Generate next steps based on conversation content
        """
        entities = conversation_data.get("entities", {})
        actions = entities.get("actions", [])
        
        next_steps = []
        
        # Action-based next steps
        if any("einreichen" in action.lower() for action in actions):
            next_steps.append("Dokumente für Einreichung vorbereiten")
        
        if any("prüfen" in action.lower() for action in actions):
            next_steps.append("Relevante Unterlagen sichten und prüfen")
        
        if any("erstellen" in action.lower() for action in actions):
            next_steps.append("Entwurf des benötigten Dokuments erstellen")
        
        # Entity-based next steps
        if entities.get("dates") or entities.get("deadlines"):
            next_steps.append("Fristen im Kalender eintragen")
        
        if entities.get("amounts"):
            next_steps.append("Finanzplanung für Kosten prüfen")
        
        # Default next steps
        if not next_steps:
            next_steps.append("Zusammenfassung des Gesprächs erstellen")
            next_steps.append("Weitere Schritte mit Mandanten besprechen")
        
        return next_steps
    
    def anonymize_conversation(self, conversation_data: Dict) -> Dict:
        """
        Anonymize conversation data for server transmission
        """
        # This would typically integrate with the anonymizer service
        # For now, we'll just return the data as-is but mark it as anonymized
        anonymized_data = conversation_data.copy()
        anonymized_data["anonymized"] = True
        anonymized_data["original_transcript"] = "[REDACTED]"
        
        return anonymized_data
    
    def get_conversation_history(self, client_id: str = None, limit: int = 10) -> List[Dict]:
        """
        Get conversation history for a client
        """
        if client_id:
            filtered_history = [conv for conv in self.conversation_history if conv.get("client_id") == client_id]
        else:
            filtered_history = self.conversation_history
        
        # Return most recent conversations
        return filtered_history[-limit:] if filtered_history else []

# Example usage
if __name__ == "__main__":
    assistant = ConversationAssistant()
    
    # Simulate a conversation
    sample_transcript = """
    Herr Müller hat am 01.01.2020 einen Arbeitsvertrag mit der Schmidt GmbH geschlossen.
    Am 01.01.2023 wurde der Vertrag gekündigt.
    Die Kündigung ist unwirksam, da keine sachliche Rechtfertigung vorliegt.
    Wir müssen bis zum 15.06.2023 einen Widerspruch einreichen.
    Der Mandant möchte eine Kündigungsschutzklage beantragen.
    Die monatliche Miete beträgt 850 Euro.
    """
    
    # Process the conversation
    result = assistant.process_transcript(sample_transcript, client_id="client_123")
    print("Conversation Processing Result:")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    
    # Generate summary
    summary = assistant.generate_summary(result)
    print("\nConversation Summary:")
    print(json.dumps(summary, ensure_ascii=False, indent=2))