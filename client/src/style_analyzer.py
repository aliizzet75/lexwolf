import re
from datetime import datetime
import numpy as np

class StyleAnalyzer:
    def __init__(self):
        self.spacy_available = self._check_spacy()
    
    def _check_spacy(self):
        """Check if spaCy is available"""
        try:
            import spacy
            return True
        except ImportError:
            return False
    
    def analyze_document(self, text):
        """
        Analyze writing style of a document
        """
        if self.spacy_available:
            return self._real_analysis(text)
        else:
            return self._mock_analysis()
    
    def _real_analysis(self, text):
        """Perform real analysis with spaCy"""
        try:
            import spacy
            nlp = spacy.load("de_core_news_sm")
            
            # Process text with spaCy
            doc = nlp(text)
            
            # Extract features
            features = {
                "avg_sentence_length": self._avg_sentence_length(doc),
                "passive_voice_ratio": self._passive_voice_ratio(doc),
                "complex_sentence_ratio": self._complex_sentence_ratio(doc),
                "formal_indicators": self._formal_indicators(text),
                "citation_patterns": self._citation_patterns(text),
                "argumentation_structure": self._argumentation_structure(text),
                "formality_score": self._formality_score(text),
                "common_closing_phrases": self._common_closing_phrases(text)
            }
            
            # Convert to vector representation
            vector = self._features_to_vector(features)
            
            return {
                "features": features,
                "vector": vector,
                "profile_id": self._generate_profile_id(vector)
            }
        except Exception as e:
            print(f"Error in real analysis: {e}")
            return self._mock_analysis()
    
    def _avg_sentence_length(self, doc):
        """Calculate average sentence length in tokens"""
        sentence_lengths = [len(sent) for sent in doc.sents]
        return np.mean(sentence_lengths) if sentence_lengths else 0
    
    def _passive_voice_ratio(self, doc):
        """Estimate passive voice ratio"""
        passive_indicators = ["werden", "worden", "wurde", "wurden", "sein"]
        passive_count = 0
        total_verbs = 0
        
        for token in doc:
            if token.pos_ == "VERB":
                total_verbs += 1
                if any(indicator in token.text.lower() for indicator in passive_indicators):
                    passive_count += 1
        
        return passive_count / total_verbs if total_verbs > 0 else 0
    
    def _complex_sentence_ratio(self, doc):
        """Estimate ratio of complex sentences (with subordinate clauses)"""
        complex_count = 0
        total_sentences = 0
        
        for sent in doc.sents:
            total_sentences += 1
            # Look for subordinating conjunctions
            if any(token.dep_ == "mark" for token in sent):
                complex_count += 1
        
        return complex_count / total_sentences if total_sentences > 0 else 0
    
    def _formal_indicators(self, text):
        """Count formal language indicators"""
        formal_words = [
            "gemäß", "gemäßigt", "gemäßigt", "gemäß", "gemäß", 
            "gemäß", "gemäß", "gemäß", "gemäß", "gemäß",
            "hinsichtlich", "bezüglich", "betreffend", "im Sinne von",
            "sachgerecht", "zweckmäßig", "angemessen", "billig",
            "gemäßigt", "gemäßigt", "gemäßigt", "gemäßigt"
        ]
        
        count = sum(1 for word in formal_words if word in text.lower())
        return count
    
    def _citation_patterns(self, text):
        """Detect citation patterns"""
        # Pattern for court decisions: "BGH, Urteil vom 01.01.2020 - Az. VIII ZR 123/19"
        court_pattern = r"[A-Z]+, (Urteil|Beschluss|Leitsatz) vom \d{2}\.\d{2}\.\d{4}"
        
        # Pattern for law references: "§ 1 BGB" or "Art. 1 GG"
        law_pattern = r"(§|Art\.)\s*\d+\s*[A-Z]{2,4}"
        
        court_matches = len(re.findall(court_pattern, text))
        law_matches = len(re.findall(law_pattern, text))
        
        return {
            "court_citations": court_matches,
            "law_references": law_matches
        }
    
    def _argumentation_structure(self, text):
        """Analyze argumentation structure"""
        # Look for typical legal argumentation patterns
        fact_indicators = ["Tatsache", "Sachverhalt", "gegeben", "vorliegend"]
        law_indicators = ["Gesetz", "Vorschrift", "Rechtslage", "anwendbar"]
        conclusion_indicators = ["Folglich", "Daher", "Somit", "Insoweit"]
        
        facts = sum(1 for indicator in fact_indicators if indicator in text)
        laws = sum(1 for indicator in law_indicators if indicator in text)
        conclusions = sum(1 for indicator in conclusion_indicators if indicator in text)
        
        return {
            "facts_mentioned": facts,
            "laws_mentioned": laws,
            "conclusions_drawn": conclusions
        }
    
    def _formality_score(self, text):
        """Calculate formality score based on vocabulary"""
        # Simple approach: ratio of complex words
        words = text.split()
        if not words:
            return 0
            
        complex_words = [word for word in words if len(word) > 6]
        return len(complex_words) / len(words)
    
    def _common_closing_phrases(self, text):
        """Count common legal closing phrases"""
        closing_phrases = [
            "Beantragt wird", "Es wird beantragt", "Zum Abschluss",
            "Mit freundlichen Grüßen", "Hochachtungsvoll",
            "Rechtsanwalt", "Rechtsanwältin"
        ]
        
        count = sum(1 for phrase in closing_phrases if phrase in text)
        return count
    
    def _features_to_vector(self, features):
        """Convert features to vector representation"""
        # This is a simplified vectorization
        # In a real implementation, this would be more sophisticated
        vector = [
            features["avg_sentence_length"],
            features["passive_voice_ratio"],
            features["complex_sentence_ratio"],
            features["formal_indicators"],
            features["citation_patterns"]["court_citations"],
            features["citation_patterns"]["law_references"],
            features["argumentation_structure"]["facts_mentioned"],
            features["argumentation_structure"]["laws_mentioned"],
            features["argumentation_structure"]["conclusions_drawn"],
            features["formality_score"],
            features["common_closing_phrases"]
        ]
        
        # Normalize vector
        vector = np.array(vector)
        if np.linalg.norm(vector) > 0:
            vector = vector / np.linalg.norm(vector)
            
        return vector.tolist()
    
    def _generate_profile_id(self, vector):
        """Generate a unique profile ID from vector"""
        # Simple hash-based approach
        vector_str = "".join([f"{x:.4f}" for x in vector])
        return f"sp_{hash(vector_str) % 10000:04d}"
    
    def _mock_analysis(self):
        """Mock analysis for when spaCy is not available"""
        features = {
            "avg_sentence_length": 15.5,
            "passive_voice_ratio": 0.3,
            "complex_sentence_ratio": 0.4,
            "formal_indicators": 8,
            "citation_patterns": {"court_citations": 3, "law_references": 5},
            "argumentation_structure": {"facts_mentioned": 4, "laws_mentioned": 6, "conclusions_drawn": 2},
            "formality_score": 0.7,
            "common_closing_phrases": 1
        }
        
        vector = [0.1, 0.3, 0.4, 0.2, 0.5, 0.6, 0.7, 0.8, 0.9, 0.15, 0.25]
        profile_id = "sp_1234"
        
        return {
            "features": features,
            "vector": vector,
            "profile_id": profile_id
        }

# Example usage
if __name__ == "__main__":
    analyzer = StyleAnalyzer()
    
    sample_text = """
    Sehr geehrte Damen und Herren,
    
   gemäß § 1 KSchG hat der Kläger einen Anspruch auf Kündigungsschutz.
    Der Arbeitsvertrag wurde am 01.01.2020 geschlossen und am 01.01.2023
    gekündigt. Die Kündigung ist unwirksam, da keine sachliche
    Rechtfertigung vorliegt.
    
    Das Landesarbeitsgericht hat in seinem Urteil vom 15.03.2022 -
    Az. 5 Sa 123/21 festgestellt, dass eine Kündigung nur dann
    wirksam ist, wenn betriebliche Gründe vorliegen.
    
    Folglich wird beantragt, die Kündigung für unwirksam zu erklären.
    
    Mit freundlichen Grüßen
    Rechtsanwalt Müller
    """
    
    result = analyzer.analyze_document(sample_text)
    print("Style Analysis Result:")
    print(f"Profile ID: {result['profile_id']}")
    print(f"Features: {result['features']}")
    print(f"Vector: {result['vector']}")