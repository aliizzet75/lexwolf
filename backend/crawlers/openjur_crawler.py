import requests
import json
from typing import List, Dict
import time
import hashlib
from datetime import datetime, timedelta

class OpenJurCrawler:
    """
    Crawler for openjur.de - German court decisions database
    """
    
    def __init__(self):
        self.base_url = "https://www.openjur.de"
        self.api_base = "https://www.openjur.de/api/v1"
        
    def get_recent_decisions(self, days: int = 1) -> List[Dict]:
        """
        Get recent court decisions from the last N days
        """
        try:
            # Calculate date range
            end_date = datetime.now()
            start_date = end_date - timedelta(days=days)
            
            # Format dates for API
            start_str = start_date.strftime("%Y-%m-%d")
            end_str = end_date.strftime("%Y-%m-%d")
            
            # For now, we'll simulate getting decisions
            # In a real implementation, this would call the openjur API
            decisions = [
                {
                    "id": "12345",
                    "title": "Urteil des BGH vom 01.01.2024 - Az. VIII ZR 123/23",
                    "court": "BGH",
                    "case_number": "VIII ZR 123/23",
                    "date": "2024-01-01",
                    "content": "Beschluss des VIII. Zivilsenats des Bundesgerichtshofs vom 1. Januar 2024. Leitsatz: 1. Die Kündigung eines Arbeitsverhältnisses durch den Arbeitgeber ist auch dann sozial ungerechtfertigt, wenn der Arbeitnehmer während der Probezeit erhebliche Pflichtverletzungen begangen hat, diese aber nicht geeignet waren, den Betrieb zu gefährden oder erheblich zu schädigen.",
                    "legal_field": "Arbeitsrecht",
                    "tags": "Kündigung,Probezeit,Soziale Ungerechtfertigkeit",
                    "url": "https://www.openjur.de/u/12345.html"
                },
                {
                    "id": "12346",
                    "title": "Urteil des BVerfG vom 15.01.2024 - Az. 1 BvR 456/23",
                    "court": "BVerfG",
                    "case_number": "1 BvR 456/23",
                    "date": "2024-01-15",
                    "content": "Beschluss des Ersten Senats des Bundesverfassungsgerichts vom 15. Januar 2024. Leitsatz: 1. Die Grundrechte sind unmittelbar geltendes Recht und binden als solche die Rechtsprechung unmittelbar. 2. Die Verletzung von Grundrechten durch öffentliche Gewalt kann auch durch Unterlassen erfolgen.",
                    "legal_field": "Verfassungsrecht",
                    "tags": "Grundrechte,Verfassungsrecht,öffentliche Gewalt",
                    "url": "https://www.openjur.de/u/12346.html"
                }
            ]
            
            return decisions
        except Exception as e:
            print(f"Error fetching recent decisions: {e}")
            return []
    
    def get_decision_content(self, decision_id: str) -> Dict:
        """
        Get content of a specific court decision
        """
        try:
            # In a real implementation, this would fetch the actual decision content
            # For now, we'll return simulated data
            decision = {
                "id": decision_id,
                "title": f"Urteil vom 01.01.2024 - Az. Test {decision_id}",
                "court": "BGH",
                "case_number": f"Test {decision_id}",
                "date": "2024-01-01",
                "content": f"Dies ist ein simuliertes Urteil mit der ID {decision_id}. Inhalt des Urteils würde hier stehen.",
                "legal_field": "Testrecht",
                "tags": "Test,Simulation",
                "url": f"https://www.openjur.de/u/{decision_id}.html"
            }
            
            return decision
        except Exception as e:
            print(f"Error fetching decision content for {decision_id}: {e}")
            return {}
    
    def chunk_decision_content(self, decision_content: Dict) -> List[Dict]:
        """
        Chunk decision content into parent-child structure
        """
        chunks = []
        
        # Create parent chunk for the entire decision
        parent_text = f"{decision_content.get('title', '')}\n"
        parent_text += f"Gericht: {decision_content.get('court', '')}\n"
        parent_text += f"Aktenzeichen: {decision_content.get('case_number', '')}\n"
        parent_text += f"Datum: {decision_content.get('date', '')}\n\n"
        parent_text += decision_content.get('content', '')
        
        parent_chunk = {
            "text": parent_text,
            "title": decision_content.get('title', ''),
            "court": decision_content.get('court', ''),
            "case_number": decision_content.get('case_number', ''),
            "date": decision_content.get('date', ''),
            "legal_field": decision_content.get('legal_field', ''),
            "tags": decision_content.get('tags', ''),
            "is_parent": True,
            "chunk_hash": hashlib.md5(parent_text.encode()).hexdigest()
        }
        chunks.append(parent_chunk)
        
        # Create child chunks for key parts (in a real implementation, this would be more sophisticated)
        parent_id = len(chunks) - 1  # Index of parent chunk
        
        # Split content into sentences for child chunks
        sentences = decision_content.get('content', '').split('. ')
        for i, sentence in enumerate(sentences):
            if sentence.strip():
                child_chunk = {
                    "text": sentence + '.' if i < len(sentences) - 1 else sentence,
                    "title": f"Leitsatz {i+1}",
                    "parent_id": parent_id,
                    "is_parent": False,
                    "chunk_hash": hashlib.md5((sentence + '.').encode()).hexdigest(),
                    "court": decision_content.get('court', ''),
                    "case_number": decision_content.get('case_number', ''),
                    "date": decision_content.get('date', ''),
                    "legal_field": decision_content.get('legal_field', ''),
                    "tags": decision_content.get('tags', '')
                }
                chunks.append(child_chunk)
        
        return chunks
    
    def crawl_decisions(self, days: int = 1, limit: int = None) -> List[Dict]:
        """
        Crawl recent court decisions from openjur.de
        """
        print(f"Starting crawl of openjur.de for the last {days} days...")
        
        # Get recent decisions
        decisions = self.get_recent_decisions(days)
        print(f"Found {len(decisions)} recent decisions")
        
        if limit:
            decisions = decisions[:limit]
            print(f"Limited to {len(decisions)} decisions")
        
        all_chunks = []
        
        for i, decision in enumerate(decisions):
            print(f"Processing decision {i+1}/{len(decisions)}: {decision['title']}")
            
            # In a real implementation, we would get the full content
            # decision_content = self.get_decision_content(decision['id'])
            
            # For now, use the decision data we already have
            decision_content = decision
            
            if decision_content:
                # Chunk the content
                chunks = self.chunk_decision_content(decision_content)
                
                # Add metadata
                for chunk in chunks:
                    chunk['source'] = 'openjur.de'
                    chunk['document_type'] = 'judgment'
                    chunk['url'] = decision['url']
                
                all_chunks.extend(chunks)
            
            # Be respectful to the server
            time.sleep(1)
        
        print(f"Crawling completed. Generated {len(all_chunks)} chunks")
        return all_chunks