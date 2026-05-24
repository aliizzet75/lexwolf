import re
from datetime import datetime

class Anonymizer:
    def __init__(self):
        self.spacy_available = self._check_spacy()
    
    def _check_spacy(self):
        """Check if spaCy is available"""
        try:
            import spacy
            return True
        except ImportError:
            return False
    
    def anonymize_text(self, text):
        """
        Anonymize personal information in text
        """
        if self.spacy_available:
            return self._real_anonymize(text)
        else:
            return self._mock_anonymize(text)
    
    def _real_anonymize(self, text):
        """Perform real anonymization with spaCy"""
        try:
            import spacy
            nlp = spacy.load("de_core_news_sm")
            
            # Process text with spaCy
            doc = nlp(text)
            
            # Find named entities
            entities = []
            for ent in doc.ents:
                if ent.label_ in ["PERSON", "ORG", "GPE"]:  # Person, Organization, Geopolitical Entity
                    entities.append({
                        "text": ent.text,
                        "label": ent.label_,
                        "start": ent.start_char,
                        "end": ent.end_char
                    })
            
            # Find dates
            date_entities = self._find_dates(text)
            entities.extend(date_entities)
            
            # Sort entities by position (reverse order for replacement)
            entities.sort(key=lambda x: x["start"], reverse=True)
            
            # Replace entities with placeholders
            anonymized_text = text
            mapping = {}
            
            for i, entity in enumerate(entities):
                placeholder = self._get_placeholder(entity["label"], i)
                mapping[placeholder] = entity["text"]
                anonymized_text = anonymized_text[:entity["start"]] + placeholder + anonymized_text[entity["end"]:]
            
            return {
                "anonymized_text": anonymized_text,
                "mapping": mapping
            }
        except Exception as e:
            print(f"Error in real anonymization: {e}")
            return self._mock_anonymize(text)
    
    def deanonymize_text(self, anonymized_text, mapping):
        """
        Restore original text from anonymized version using mapping
        """
        restored_text = anonymized_text
        for placeholder, original in mapping.items():
            restored_text = restored_text.replace(placeholder, original)
        return restored_text
    
    def _find_dates(self, text):
        """
        Find dates in text using regex patterns
        """
        date_patterns = [
            r"\d{2}\.\d{2}\.\d{4}",  # DD.MM.YYYY
            r"\d{1,2}\.\s*(Januar|Februar|März|April|Mai|Juni|Juli|August|September|Oktober|November|Dezember)\s*\d{4}",  # D. Month YYYY
            r"\d{4}-\d{2}-\d{2}"  # YYYY-MM-DD
        ]
        
        entities = []
        for pattern in date_patterns:
            matches = re.finditer(pattern, text)
            for match in matches:
                entities.append({
                    "text": match.group(),
                    "label": "DATE",
                    "start": match.start(),
                    "end": match.end()
                })
        
        return entities
    
    def _get_placeholder(self, label, index):
        """
        Generate placeholder based on entity label
        """
        placeholders = {
            "PERSON": f"[PERSON_{index+1}]",
            "ORG": f"[ORG_{index+1}]",
            "GPE": f"[ORT_{index+1}]",
            "DATE": f"[DATUM_{index+1}]"
        }
        return placeholders.get(label, f"[ENT_{index+1}]")
    
    def _mock_anonymize(self, text):
        """
        Mock anonymization for when spaCy is not available
        """
        # Simple pattern-based anonymization
        patterns = [
            (r"[A-Z][a-z]+, [A-Z][a-z]+", "[PERSON_1]"),  # Names like "Müller, Hans"
            (r"\d{2}\.\d{2}\.\d{4}", "[DATUM_1]"),  # Dates
            (r"[A-Z][a-z]+ GmbH", "[ORG_1]"),  # Company names
            (r"[A-Z][a-z]+straße \d+", "[ORT_1]")  # Addresses
        ]
        
        anonymized_text = text
        mapping = {}
        
        for pattern, placeholder in patterns:
            matches = re.findall(pattern, anonymized_text)
            for match in matches:
                mapping[placeholder] = match
                anonymized_text = re.sub(re.escape(match), placeholder, anonymized_text)
        
        return {
            "anonymized_text": anonymized_text,
            "mapping": mapping
        }

# Example usage
if __name__ == "__main__":
    anonymizer = Anonymizer()
    
    sample_text = """
    Sehr geehrte Damen und Herren,
    
    mein Mandant Hans Müller hat am 01.01.2020 einen Arbeitsvertrag 
    mit der Firma Schmidt GmbH in Berlin geschlossen. Am 01.01.2023
    wurde der Vertrag gekündigt.
    
    Mit freundlichen Grüßen
    Rechtsanwalt Weber
    """
    
    result = anonymizer.anonymize_text(sample_text)
    print("Original text:")
    print(sample_text)
    print("\nAnonymized text:")
    print(result["anonymized_text"])
    print("\nMapping:")
    for placeholder, original in result["mapping"].items():
        print(f"  {placeholder} -> {original}")
    
    # Test deanonymization
    restored = anonymizer.deanonymize_text(result["anonymized_text"], result["mapping"])
    print("\nRestored text:")
    print(restored)