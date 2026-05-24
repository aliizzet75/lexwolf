import threading
import time
import queue
import json
from datetime import datetime
import re

class ConversationRecorder:
    def __init__(self):
        self.is_recording = False
        self.conversation_data = []
        self.recording_thread = None
        self.audio_queue = queue.Queue()
        
    def start_recording(self):
        """
        Start conversation recording
        """
        if self.is_recording:
            return False
            
        self.is_recording = True
        self.conversation_data = []
        
        # Start recording thread
        self.recording_thread = threading.Thread(target=self._record_conversation)
        self.recording_thread.daemon = True
        self.recording_thread.start()
        
        return True
    
    def stop_recording(self):
        """
        Stop conversation recording
        """
        if not self.is_recording:
            return False
            
        self.is_recording = False
        if self.recording_thread:
            self.recording_thread.join()
            
        return True
    
    def add_transcript(self, transcript):
        """
        Add transcript to conversation
        """
        timestamp = datetime.now().isoformat()
        self.conversation_data.append({
            "timestamp": timestamp,
            "transcript": transcript,
            "entities": self._extract_entities(transcript)
        })
    
    def _record_conversation(self):
        """
        Simulate conversation recording (in real implementation, this would interface with audio)
        """
        print("Conversation recording started...")
        start_time = time.time()
        
        # This is a simulation - in real implementation, this would capture audio
        while self.is_recording:
            # Simulate processing time
            time.sleep(1)
            
            # Check if we have audio data to process
            try:
                # In real implementation, this would get audio data from a microphone
                # For now, we'll just simulate
                if time.time() - start_time > 10:  # Stop after 10 seconds for demo
                    break
            except queue.Empty:
                continue
        
        print("Conversation recording stopped.")
    
    def _extract_entities(self, transcript):
        """
        Extract entities from transcript
        """
        entities = {
            "dates": self._extract_dates(transcript),
            "deadlines": self._extract_deadlines(transcript),
            "persons": self._extract_persons(transcript),
            "actions": self._extract_actions(transcript)
        }
        return entities
    
    def _extract_dates(self, text):
        """
        Extract dates from text
        """
        date_patterns = [
            r"\b\d{1,2}\.\d{1,2}\.\d{4}\b",  # DD.MM.YYYY
            r"\b\d{1,2}\.\s*(Januar|Februar|März|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember)\s*\d{4}\b"  # D. Month YYYY
        ]
        
        dates = []
        for pattern in date_patterns:
            matches = re.findall(pattern, text, re.IGNORECASE)
            dates.extend(matches)
        
        return dates
    
    def _extract_deadlines(self, text):
        """
        Extract deadlines and timeframes from text
        """
        deadline_patterns = [
            r"in\s+(\d+)\s+(Tagen|Wochen|Monaten)",
            r"bis\s+(zum\s+)?(\d{1,2}\.\d{1,2}\.\d{4})",
            r"spätestens\s+(am\s+)?(\d{1,2}\.\d{1,2}\.\d{4})",
            r"innerhalb\s+(von\s+)?(\d+)\s+(Tagen|Wochen|Monaten)"
        ]
        
        deadlines = []
        for pattern in deadline_patterns:
            matches = re.findall(pattern, text, re.IGNORECASE)
            deadlines.extend(matches)
        
        return deadlines
    
    def _extract_persons(self, text):
        """
        Extract person references from text
        """
        # Simple pattern for person references
        person_patterns = [
            r"(Herr|Frau)\s+[A-Z][a-z]+",
            r"Mandant(en)?\s+[A-Z][a-z]+",
            r"Kläger\s+[A-Z][a-z]+"
        ]
        
        persons = []
        for pattern in person_patterns:
            matches = re.findall(pattern, text)
            persons.extend(matches)
        
        return persons
    
    def _extract_actions(self, text):
        """
        Extract action items from text
        """
        action_keywords = [
            "müssen", "sollen", "werden", "muss", "soll",
            "beantragen", "einreichen", "versenden", "erstellen",
            "vorbereiten", "prüfen", "überprüfen"
        ]
        
        actions = []
        for keyword in action_keywords:
            if keyword in text.lower():
                # Find sentences containing the keyword
                sentences = re.split(r'[.!?]+', text)
                for sentence in sentences:
                    if keyword in sentence.lower():
                        actions.append(sentence.strip())
                        break
        
        return actions
    
    def generate_summary(self):
        """
        Generate conversation summary
        """
        if not self.conversation_data:
            return "Keine Gesprächsdaten verfügbar."
        
        summary = {
            "duration": len(self.conversation_data),
            "topics": self._extract_topics(),
            "dates": [],
            "deadlines": [],
            "persons": [],
            "actions": [],
            "suggestions": self._generate_suggestions()
        }
        
        # Collect all entities
        for entry in self.conversation_data:
            entities = entry.get("entities", {})
            summary["dates"].extend(entities.get("dates", []))
            summary["deadlines"].extend(entities.get("deadlines", []))
            summary["persons"].extend(entities.get("persons", []))
            summary["actions"].extend(entities.get("actions", []))
        
        # Remove duplicates
        summary["dates"] = list(set(summary["dates"]))
        summary["deadlines"] = list(set(summary["deadlines"]))
        summary["persons"] = list(set(summary["persons"]))
        summary["actions"] = list(set(summary["actions"]))
        
        return summary
    
    def _extract_topics(self):
        """
        Extract main topics from conversation
        """
        # Simple topic extraction based on keywords
        topics = []
        all_text = " ".join([entry["transcript"] for entry in self.conversation_data])
        
        topic_keywords = {
            "Kündigung": ["kündigung", "arbeitsvertrag", "betriebsrat"],
            "Mietrecht": ["miete", "vermieter", "mieter", "wohnung"],
            "Familienrecht": ["scheidung", "unterhalt", "sorgerecht"],
            "Verkehrsrecht": ["unfall", "versicherung", "fahrzeug"],
            "Strafrecht": ["strafverfahren", "anzeige", "verteidigung"]
        }
        
        all_text_lower = all_text.lower()
        for topic, keywords in topic_keywords.items():
            if any(keyword in all_text_lower for keyword in keywords):
                topics.append(topic)
        
        return topics if topics else ["Allgemeines Rechtsgespräch"]
    
    def _generate_suggestions(self):
        """
        Generate suggestions based on conversation content
        """
        all_text = " ".join([entry["transcript"] for entry in self.conversation_data])
        suggestions = []
        
        # Suggestion rules
        if "kündigung" in all_text.lower():
            suggestions.append("📄 Soll ich eine Kündigungsschutzklage vorbereiten?")
        
        if "miete" in all_text.lower():
            suggestions.append("📄 Soll ich einen Mietvertrag entwerfen?")
        
        if "scheidung" in all_text.lower():
            suggestions.append("📄 Soll ich Scheidungsfolgenvereinbarung vorbereiten?")
        
        if "vertrag" in all_text.lower():
            suggestions.append("📄 Soll ich einen Vertragsentwurf erstellen?")
        
        # Check for deadlines
        if "frist" in all_text.lower() or "bis" in all_text.lower():
            suggestions.append("📅 Soll ich einen Kalender-Eintrag für die Frist erstellen?")
        
        # General suggestions
        suggestions.append("📧 Soll ich dem Mandanten eine Zusammenfassung schicken?")
        suggestions.append("📝 Soll ich Notizen aus dem Gespräch strukturieren?")
        
        return suggestions
    
    def save_conversation(self, filename):
        """
        Save conversation to file
        """
        data = {
            "conversation": self.conversation_data,
            "summary": self.generate_summary(),
            "timestamp": datetime.now().isoformat()
        }
        
        with open(filename, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        
        return True
    
    def load_conversation(self, filename):
        """
        Load conversation from file
        """
        try:
            with open(filename, 'r', encoding='utf-8') as f:
                data = json.load(f)
            
            self.conversation_data = data.get("conversation", [])
            return True
        except Exception as e:
            print(f"Fehler beim Laden des Gesprächs: {e}")
            return False

# Example usage
if __name__ == "__main__":
    recorder = ConversationRecorder()
    
    # Simulate a conversation
    sample_transcripts = [
        "Herr Müller hat am 01.01.2020 einen Arbeitsvertrag mit der Schmidt GmbH geschlossen.",
        "Am 01.01.2023 wurde der Vertrag gekündigt.",
        "Die Kündigung ist unwirksam, da keine sachliche Rechtfertigung vorliegt.",
        "Wir müssen bis zum 15.06.2023 einen Widerspruch einreichen.",
        "Der Mandant möchte eine Kündigungsschutzklage beantragen."
    ]
    
    print("Starting conversation recording simulation...")
    recorder.start_recording()
    
    # Add sample transcripts
    for transcript in sample_transcripts:
        recorder.add_transcript(transcript)
        time.sleep(0.5)  # Simulate time between utterances
    
    # Stop recording
    recorder.stop_recording()
    
    # Generate summary
    summary = recorder.generate_summary()
    print("\nConversation Summary:")
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    
    # Save conversation
    recorder.save_conversation("sample_conversation.json")
    print("\nConversation saved to sample_conversation.json")