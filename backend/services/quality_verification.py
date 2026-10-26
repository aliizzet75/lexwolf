import re
from typing import Dict, List, Optional, Tuple
from services.feedback_service import FeedbackService
# T#299: dieselbe Zitat-Regex und DB-Prüfung wie api.akte.pruefe_belege nutzen
# statt einer eigenen, abweichenden (und trunkierenden) Implementierung - siehe
# _extract_cited_paragraphs/_verify_sources unten.
from api.akte import _PARAGRAPH_ZITAT, _paragraph_in_db
import logging

# Set up logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class QualityVerificationService:
    """
    LLM-as-Judge service for quality testing and hallucination detection
    """

    def __init__(self):
        # T#298: M5-Feedback-Loop (Anwalt-Korrekturen) zusätzlich für die
        # Qualitätspipeline nutzen - fachliche Korrekturen werden als
        # Testset-Kandidaten markiert und landen in einer Review-Queue.
        self.feedback_service = FeedbackService()
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
    
    def _extract_cited_paragraphs(self, content: str) -> List[Dict]:
        """
        Extract cited paragraphs (§X, Art. Y, etc.) from content.

        T#299: nutzt dieselbe Regex wie api.akte.pruefe_belege (_PARAGRAPH_ZITAT)
        statt einer eigenen, flacheren Variante - die erfasst eine zusammengesetzte
        Zitatkette ("§ 573 Abs. 2 Nr. 2 BGB") als EIN Zitat statt beim ersten
        Abs./Nr.-Fragment abzuschneiden ("§ 573 Abs").
        """
        cited_paragraphs = []
        seen = set()
        for m in _PARAGRAPH_ZITAT.finditer(content):
            zeichen, nr, gesetz = m.group(1), m.group(2), m.group(3).strip()
            if gesetz in ("Abs", "Satz", "Nr") or (nr, gesetz) in seen:
                continue
            seen.add((nr, gesetz))
            art = "Art." if zeichen == "Art." else "§"
            cited_paragraphs.append({
                "zitat": m.group(0).strip(),
                "art": art,
                "nr": nr,
                "gesetz": gesetz,
            })
        logger.info(f"Extracted {len(cited_paragraphs)} cited paragraphs")

        return cited_paragraphs

    def _verify_sources(self, cited_paragraphs: List[Dict]) -> Dict:
        """
        Verify that cited paragraphs exist in the database.

        T#299: Verifikation läuft jetzt wie in api.akte::_paragraph_in_db über
        einen Tag+Titel-Lookup in legal_chunks statt über eine wörtliche
        Teilstring-Suche des (trunkierten) Zitats im rohen Chunk-Text - letztere
        schlug fast immer fehl, weil Chunks strukturiert sind und nicht als
        Fließtext in exakter Zitierform vorliegen.
        """
        verified_sources = []
        missing_sources = []
        unverifiable_sources = []

        for zitat in cited_paragraphs:
            text = zitat["zitat"]
            if zitat["art"] == "Art.":
                # Artikel (GG, EGBGB, DSGVO ...) stehen nicht unter "§" in der
                # DB - wie in api.akte.pruefe_belege nicht per DB-Lookup prüfbar.
                unverifiable_sources.append(text)
                continue

            status, _chunk_id = _paragraph_in_db(zitat["nr"], zitat["gesetz"])
            if status == "geprüft":
                verified_sources.append(text)
            elif status == "nicht gefunden":
                missing_sources.append(text)
            else:
                # "nicht in Datenbank" (Gesetz nicht gecrawlt) oder "nicht
                # prüfbar" (DB-Fehler) - fehlende Abdeckung heißt nicht falsch,
                # siehe Docstring von _paragraph_in_db in akte.py.
                unverifiable_sources.append(text)

        pruefbar = len(verified_sources) + len(missing_sources)
        accuracy_rate = (len(verified_sources) / pruefbar) if pruefbar else 1.0

        if accuracy_rate < 0.99:
            self.metrics["source_accuracy"] = accuracy_rate

        return {
            "total_cited": len(cited_paragraphs),
            "verified_sources": verified_sources,
            "missing_sources": missing_sources,
            "unverifiable_sources": unverifiable_sources,
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
    
    def _detect_hallucinations(self, content: str, cited_paragraphs: List[Dict]) -> Dict:
        """
        Detect potential hallucinations in the content
        """
        hallucinations_found = False
        hallucination_reasons = []

        # Check for common hallucination patterns
        # T#299: Das frühere Muster r"gemäß\s+§\s*\d+\s+[A-Z]+" wurde entfernt -
        # es markierte die normale, korrekte Zitierweise ("gemäß § 573 BGB")
        # fälschlich als Halluzination und widersprach sogar
        # _check_completeness()["has_reasoning"], das "gemäß" positiv wertet.
        suspicious_patterns = [
            r"[A-Z][a-z]+\s+vom\s+\d+\.\d+\.\d+",  # Suspicious date formats
            r"Aus diesem Grund\s+.*\s+§\s*\d+",  # Unusual reasoning patterns
        ]

        for pattern in suspicious_patterns:
            if re.search(pattern, content, re.IGNORECASE):
                hallucinations_found = True
                hallucination_reasons.append(f"Suspicious pattern detected: {pattern}")

        # Check if cited paragraphs are realistic
        for zitat in cited_paragraphs:
            paragraph = zitat["zitat"]
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
    
    def get_testset_kandidaten(self, status: Optional[str] = "offen") -> List[Dict]:
        """
        T#298: Liefert Anwalt-Korrekturen mit fachlicher (nicht nur stilistischer)
        Änderung aus dem M5-Feedback-Loop als Kandidaten für das 100-Fragen-
        Testset. `status` filtert nach Review-Status ("offen" = Default,
        None = alle Stati). Review-Queue für Ali, bevor ein Kandidat offiziell
        ins Testset übernommen wird.
        """
        return self.feedback_service.list_testset_kandidaten(status=status)

    def review_testset_kandidat(self, kandidat_id: int, entscheidung: str) -> Dict:
        """
        T#298: Ali-Entscheidung (angenommen/abgelehnt) für einen Testset-
        Kandidaten aus der Review-Queue.
        """
        return self.feedback_service.review_testset_kandidat(kandidat_id, entscheidung)

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