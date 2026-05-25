import re
from typing import Dict, List, Tuple
from services.database_service import DatabaseService
import logging

# Set up logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class QualityVerificationService:
    """
    LLM-as-Judge service for quality testing and hallucination detection
    """
    
    def __init__(self):
        self.database_service = DatabaseService()
        self.metrics = {
            "total_checks": 0,
            "passed_checks": 0,
            "failed_checks": 0,
            "hallucinations_detected": 0,
            "source_accuracy": 0.0,
            "completeness_rate": 0.0
        }
    
    def verify_generated_content(self, content: str, context: Dict = None) -> Dict:
        """
        Verify generated content for accuracy, sources, and completeness
        """
        self.metrics["total_checks"] += 1
        
        try:
            # Check 1: Extract cited paragraphs and verify they exist
            cited_paragraphs = self._extract_cited_paragraphs(content)
            source_verification = self._verify_sources(cited_paragraphs)
            
            # Check 2: Verify content completeness
            completeness_check = self._check_completeness(content, context)
            
            # Check 3: Detect potential hallucinations
            hallucination_check = self._detect_hallucinations(content, cited_paragraphs)
            
            # Calculate overall quality score
            quality_score = self._calculate_quality_score(
                source_verification, 
                completeness_check, 
                hallucination_check
            )
            
            # Determine if content passes quality checks
            passes_quality = (
                source_verification["accuracy_rate"] > 0.99 and
                not hallucination_check["hallucinations_found"] and
                quality_score > 0.8
            )
            
            if passes_quality:
                self.metrics["passed_checks"] += 1
            else:
                self.metrics["failed_checks"] += 1
            
            result = {
                "passes_quality": passes_quality,
                "quality_score": quality_score,
                "source_verification": source_verification,
                "completeness_check": completeness_check,
                "hallucination_check": hallucination_check,
                "timestamp": __import__('datetime').datetime.now().isoformat()
            }
            
            logger.info(f"Quality check completed - Pass: {passes_quality}, Score: {quality_score:.2f}")
            return result
            
        except Exception as e:
            logger.error(f"Error in quality verification: {e}")
            self.metrics["failed_checks"] += 1
            return {
                "passes_quality": False,
                "error": str(e),
                "quality_score": 0.0
            }
    
    def _extract_cited_paragraphs(self, content: str) -> List[str]:
        """
        Extract cited paragraphs (§X, Art. Y, etc.) from content
        """
        # Pattern for German legal citations
        patterns = [
            r'§\s*\d+[a-z]*\s*[A-ZÄÖÜ][a-zA-Zßäöü]*',  # § 1 BGB, § 23 KSchG
            r'Art\.\s*\d+[a-z]*\s*[A-ZÄÖÜ][a-zA-Zßäöü]*',  # Art. 1 GG
            r'[A-ZÄÖÜ][a-zA-Zßäöü]*\s*§\s*\d+[a-z]*',  # BGB § 1
        ]
        
        cited_paragraphs = []
        for pattern in patterns:
            matches = re.findall(pattern, content, re.IGNORECASE)
            cited_paragraphs.extend(matches)
        
        # Remove duplicates and clean up
        cited_paragraphs = list(set([p.strip() for p in cited_paragraphs]))
        logger.info(f"Extracted {len(cited_paragraphs)} cited paragraphs")
        
        return cited_paragraphs
    
    def _verify_sources(self, cited_paragraphs: List[str]) -> Dict:
        """
        Verify that cited paragraphs exist in the database
        """
        verified_sources = []
        missing_sources = []
        
        for paragraph in cited_paragraphs:
            # Search for the paragraph in the database
            search_results = self.database_service.search_chunks_hybrid(paragraph, limit=5)
            
            if search_results and len(search_results) > 0:
                # Check if any result matches the cited paragraph
                found_match = False
                for result in search_results:
                    if paragraph.lower() in result["text"].lower():
                        verified_sources.append(paragraph)
                        found_match = True
                        break
                
                if not found_match:
                    missing_sources.append(paragraph)
            else:
                missing_sources.append(paragraph)
        
        accuracy_rate = len(verified_sources) / len(cited_paragraphs) if cited_paragraphs else 1.0
        
        if accuracy_rate < 0.99:
            self.metrics["source_accuracy"] = accuracy_rate
            
        return {
            "total_cited": len(cited_paragraphs),
            "verified_sources": verified_sources,
            "missing_sources": missing_sources,
            "accuracy_rate": accuracy_rate
        }
    
    def _check_completeness(self, content: str, context: Dict = None) -> Dict:
        """
        Check if the content is complete and includes all relevant information
        """
        # This would typically involve checking against the context
        # For now, we'll do basic completeness checks
        
        completeness_indicators = {
            "has_introduction": len(content) > 50,
            "has_conclusion": "Mit freundlichen Grüßen" in content or "Anwalt" in content,
            "has_citations": "§" in content,
            "has_reasoning": any(word in content.lower() for word in ["daher", "somit", "folglich", "gemäß"])
        }
        
        # Count how many completeness indicators are met
        indicators_met = sum(1 for val in completeness_indicators.values() if val)
        completeness_score = indicators_met / len(completeness_indicators)
        
        return {
            "completeness_score": completeness_score,
            "indicators": completeness_indicators,
            "indicators_met": indicators_met,
            "total_indicators": len(completeness_indicators)
        }
    
    def _detect_hallucinations(self, content: str, cited_paragraphs: List[str]) -> Dict:
        """
        Detect potential hallucinations in the content
        """
        hallucinations_found = False
        hallucination_reasons = []
        
        # Check for common hallucination patterns
        suspicious_patterns = [
            r"gemäß\s+§\s*\d+\s+[A-Z]+",  # Unusual § references
            r"[A-Z][a-z]+\s+vom\s+\d+\.\d+\.\d+",  # Suspicious date formats
            r"Aus diesem Grund\s+.*\s+§\s*\d+",  # Unusual reasoning patterns
        ]
        
        for pattern in suspicious_patterns:
            if re.search(pattern, content, re.IGNORECASE):
                hallucinations_found = True
                hallucination_reasons.append(f"Suspicious pattern detected: {pattern}")
        
        # Check if cited paragraphs are realistic
        for paragraph in cited_paragraphs:
            # Very basic check - in a real implementation, this would be more sophisticated
            if len(paragraph) > 50:  # Unusually long paragraph reference
                hallucinations_found = True
                hallucination_reasons.append(f"Unusually long paragraph reference: {paragraph}")
        
        if hallucinations_found:
            self.metrics["hallucinations_detected"] += 1
            
        return {
            "hallucinations_found": hallucinations_found,
            "reasons": hallucination_reasons
        }
    
    def _calculate_quality_score(self, source_verification: Dict, completeness_check: Dict, hallucination_check: Dict) -> float:
        """
        Calculate overall quality score based on all checks
        """
        # Weighted scoring system
        source_weight = 0.4
        completeness_weight = 0.3
        hallucination_weight = 0.3
        
        # Source accuracy score (inverted for hallucinations)
        source_score = source_verification["accuracy_rate"]
        
        # Completeness score
        completeness_score = completeness_check["completeness_score"]
        
        # Hallucination penalty (0 if hallucinations found, 1 if not)
        hallucination_score = 0.0 if hallucination_check["hallucinations_found"] else 1.0
        
        # Calculate weighted average
        quality_score = (
            source_score * source_weight +
            completeness_score * completeness_weight +
            hallucination_score * hallucination_weight
        )
        
        return quality_score
    
    def get_metrics(self) -> Dict:
        """
        Get current quality metrics
        """
        return self.metrics.copy()
    
    def reset_metrics(self):
        """
        Reset quality metrics
        """
        self.metrics = {
            "total_checks": 0,
            "passed_checks": 0,
            "failed_checks": 0,
            "hallucinations_detected": 0,
            "source_accuracy": 0.0,
            "completeness_rate": 0.0
        }

# Example usage
if __name__ == "__main__":
    # Create service instance
    quality_service = QualityVerificationService()
    
    # Example content to verify
    sample_content = """
    Sehr geehrte Damen und Herren,

    Bezüglich des Kündigungsverhältnisses ist festzustellen, dass 
    gemäß § 1 KSchG der Kündigungsschutz greift, sofern der Arbeitnehmer
    mindestens sechs Monate im Betrieb beschäftigt war.

    Die Kündigung ist daher unwirksam, da keine sachliche Rechtfertigung
    vorliegt und der Kündigungsgrund nicht ausreichend begründet wurde.

    Mit freundlichen Grüßen
    """
    
    # Verify the content
    result = quality_service.verify_generated_content(sample_content)
    
    print("Quality Verification Result:")
    print(f"Passes Quality: {result['passes_quality']}")
    print(f"Quality Score: {result['quality_score']:.2f}")
    print(f"Source Verification: {result['source_verification']}")
    print(f"Hallucinations Found: {result['hallucination_check']['hallucinations_found']}")