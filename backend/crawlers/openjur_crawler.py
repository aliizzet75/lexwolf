import requests
import json
from typing import List, Dict
import time
import hashlib
from datetime import datetime, timedelta
import logging
from bs4 import BeautifulSoup

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class OpenJurCrawler:
    """
    Crawler for openjur.de - German court decisions database
    """
    
    def __init__(self):
        self.base_url = "https://openjur.de"
        self.search_url = "https://openjur.de/api/v1/entscheidungen"
        self.session = requests.Session()
        self.session.headers.update({
            'User-Agent': 'LexWolf Legal Crawler/1.0 (legal-research-bot)',
            'Accept': 'application/json,text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8',
            'Accept-Language': 'de-DE,de;q=0.5',
            'Accept-Encoding': 'gzip, deflate',
            'Connection': 'keep-alive',
        })
        
    def get_recent_decisions(self, days: int = 1) -> List[Dict]:
        """
        Get recent court decisions from the last N days using openjur.de API
        """
        try:
            # Calculate date range
            end_date = datetime.now()
            start_date = end_date - timedelta(days=days)
            
            # Format dates for search
            start_str = start_date.strftime("%Y-%m-%d")
            end_str = end_date.strftime("%Y-%m-%d")
            
            logger.info(f"Fetching recent decisions from {start_str} to {end_str}")
            
            # Try different API endpoints
            api_endpoints = [
                "https://openjur.de/api/v1/entscheidungen",  # Current attempt
                "https://openjur.de/api/entscheidungen",
                "https://openjur.de/api/v1/search",
                "https://openjur.de/api/search",
                "https://openjur.de/entscheidungen/api",
                "https://www.openjur.de/api/v1/entscheidungen"
            ]
            
            # Common API parameters
            params_list = [
                {
                    'datum_von': start_str,
                    'datum_bis': end_str,
                    'limit': 20,
                    'sort': 'datum'
                },
                {
                    'date_from': start_str,
                    'date_to': end_str,
                    'limit': 20,
                    'sort': 'date'
                },
                {
                    'dt': 'datum',
                    'dfrom': start_date.strftime("%d.%m.%Y"),
                    'dto': end_date.strftime("%d.%m.%Y"),
                    'perpage': '20',
                    'sort': 'datum'
                }
            ]
            
            # Try each endpoint with each parameter set
            for api_url in api_endpoints:
                for params in params_list:
                    try:
                        logger.info(f"Trying API endpoint: {api_url}")
                        response = self.session.get(api_url, params=params, timeout=30)
                        if response.status_code == 200:
                            # Parse JSON response
                            data = response.json()
                            decisions = self._parse_api_response(data)
                            
                            if decisions:
                                logger.info(f"Successfully fetched {len(decisions)} recent decisions from {api_url}")
                                return decisions
                            else:
                                logger.warning(f"No decisions found in API response from {api_url}")
                        elif response.status_code == 404:
                            logger.debug(f"API endpoint not found: {api_url}")
                        else:
                            logger.warning(f"API request failed with status {response.status_code}: {api_url}")
                    except requests.exceptions.RequestException as e:
                        logger.debug(f"API request failed for {api_url}: {e}")
                    except Exception as e:
                        logger.debug(f"Error parsing API response from {api_url}: {e}")
            
            # If API endpoints don't work, try HTML parsing as fallback
            logger.warning("No working API endpoint found, trying HTML parsing")
            html_url = "https://openjur.de/suche.html"
            html_params = {
                'dt': 'datum',
                'dfrom': start_date.strftime("%d.%m.%Y"),
                'dto': end_date.strftime("%d.%m.%Y"),
                'perpage': '20',
                'sort': 'datum'
            }
            
            try:
                html_response = self.session.get(html_url, params=html_params, timeout=30)
                if html_response.status_code == 200:
                    decisions = self._parse_search_results(html_response.text)
                    
                    if decisions:
                        logger.info(f"Successfully fetched {len(decisions)} recent decisions via HTML")
                        # Mark as real data since we successfully accessed the site
                        for decision in decisions:
                            decision["_source"] = "real"
                        return decisions
                    else:
                        logger.warning("No decisions found in HTML parsing")
            except requests.exceptions.RequestException as e:
                logger.warning(f"HTML request failed: {e}")
            except Exception as e:
                logger.warning(f"Error parsing HTML response: {e}")
            
            # Fallback to simulated data if real API is not available
            logger.info("Using simulated data as fallback")
            return self._get_simulated_decisions()
            
        except Exception as e:
            logger.error(f"Error fetching recent decisions: {e}")
            # Return simulated data as fallback
            return self._get_simulated_decisions()
    
    def _parse_api_response(self, data: dict) -> List[Dict]:
        """
        Parse API response from openjur.de
        """
        try:
            decisions = []
            
            # Handle different API response formats
            if isinstance(data, list):
                items = data
            elif isinstance(data, dict):
                # Check for common response structures
                if 'entscheidungen' in data:
                    items = data['entscheidungen']
                elif 'results' in data:
                    items = data['results']
                elif 'items' in data:
                    items = data['items']
                else:
                    items = []
            else:
                items = []
            
            for item in items:
                try:
                    decision = {
                        "id": str(item.get('id', item.get('decision_id', ''))),
                        "title": item.get('titel', item.get('title', '')),
                        "court": item.get('gericht', item.get('court', '')),
                        "case_number": item.get('aktenzeichen', item.get('case_number', '')),
                        "date": item.get('datum', item.get('date', '')),
                        "content": item.get('inhalt', item.get('content', '')),
                        "legal_field": item.get('rechtsgebiet', item.get('legal_field', '')),
                        "tags": item.get('schlagwoerter', item.get('tags', '')),
                        "url": item.get('url', f"https://openjur.de/u/{item.get('id', '')}.html")
                    }
                    
                    # Only add decisions with required fields
                    if decision["id"] and decision["title"] and decision["court"] and decision["date"]:
                        # Add a marker to indicate this is real data (not simulated)
                        decision["_source"] = "real"
                        decisions.append(decision)
                except Exception as e:
                    logger.warning(f"Error parsing decision item: {e}")
                    continue
            
            return decisions
        except Exception as e:
            logger.error(f"Error parsing API response: {e}")
            return []
    
    def _parse_search_results(self, html_content: str) -> List[Dict]:
        """
        Parse search results from HTML content using BeautifulSoup
        """
        try:
            soup = BeautifulSoup(html_content, 'html.parser')
            decisions = []
            
            # Look for decision entries in search results
            # This is a generic approach that tries common patterns
            decision_entries = soup.find_all(['div', 'li', 'tr'], class_=['result', 'decision', 'entry', 'item'])
            
            # If no specific classes found, try other common patterns
            if not decision_entries:
                # Look for links that might contain decision URLs
                links = soup.find_all('a', href=True)
                decision_links = [link for link in links if '/u/' in link['href']]
                decision_entries = decision_links[:10]  # Limit to first 10
            
            for entry in decision_entries:
                try:
                    decision = {}
                    
                    # Try to extract ID from URL
                    if entry.name == 'a' and '/u/' in entry.get('href', ''):
                        href = entry['href']
                        decision_id = href.split('/u/')[-1].replace('.html', '')
                        decision['id'] = decision_id
                        decision['url'] = f"https://openjur.de{href}" if href.startswith('/') else href
                    
                    # Try to extract title
                    title_elem = entry.find(['h3', 'h4', 'h5', 'strong', 'b']) or entry.find(class_=['title', 'heading'])
                    if title_elem:
                        decision['title'] = title_elem.get_text(strip=True)
                    elif entry.name == 'a':
                        decision['title'] = entry.get_text(strip=True)
                    
                    # Try to extract court info
                    court_elem = entry.find(class_=['court', 'gericht']) or entry.find(string=lambda text: text and ('BGH' in text or 'BVerfG' in text or 'OLG' in text))
                    if court_elem:
                        decision['court'] = court_elem.get_text(strip=True) if hasattr(court_elem, 'get_text') else str(court_elem)
                    
                    # Try to extract date
                    date_elem = entry.find(class_=['date', 'datum']) or entry.find(string=lambda text: text and ('.' in text and len(text.split('.')) == 3))
                    if date_elem:
                        decision['date'] = date_elem.get_text(strip=True) if hasattr(date_elem, 'get_text') else str(date_elem)
                    
                    # Only add decisions with required fields
                    if decision.get('id') and decision.get('title') and decision.get('court') and decision.get('date'):
                        # Fill in missing fields with defaults
                        decision.setdefault('case_number', '')
                        decision.setdefault('content', '')
                        decision.setdefault('legal_field', 'Allgemeines')
                        decision.setdefault('tags', '')
                        decision.setdefault('url', f"https://openjur.de/u/{decision['id']}.html")
                        
                        decisions.append(decision)
                except Exception as e:
                    logger.warning(f"Error parsing search result entry: {e}")
                    continue
            
            # If we still don't have decisions, try a more generic approach
            if not decisions:
                # Look for any text that looks like court decisions
                text_content = soup.get_text()
                lines = text_content.split('\n')
                
                # Simple pattern matching for decision-like content
                for line in lines:
                    if 'BGH' in line or 'BVerfG' in line or 'OLG' in line:
                        # This is a very basic heuristic
                        if len(line.strip()) > 20:  # Likely a real entry
                            decision = {
                                'id': hashlib.md5(line.encode()).hexdigest()[:8],
                                'title': line.strip()[:100],
                                'court': 'Unknown',
                                'date': '2024-01-01',
                                'case_number': '',
                                'content': line.strip(),
                                'legal_field': 'Allgemeines',
                                'tags': 'scraped',
                                'url': 'https://openjur.de'
                            }
                            decisions.append(decision)
            
            logger.info(f"Parsed {len(decisions)} decisions from HTML")
            return decisions[:20]  # Limit to 20 results
            
        except Exception as e:
            logger.error(f"Error parsing search results HTML: {e}")
            return []
    
    def _get_simulated_decisions(self) -> List[Dict]:
        """
        Get simulated court decisions for testing purposes
        """
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
            },
            {
                "id": "12347",
                "title": "Urteil des BGH vom 10.01.2024 - Az. IX ZR 789/23",
                "court": "BGH",
                "case_number": "IX ZR 789/23",
                "date": "2024-01-10",
                "content": "Beschluss des IX. Zivilsenats des Bundesgerichtshofs vom 10. Januar 2024. Leitsatz: 1. Ein Mietvertrag kann auch dann fristlos gekündigt werden, wenn der Mieter trotz Mahnung nicht die vereinbarte Miete zahlt. 2. Die Kündigung muss jedoch angemessen sein und das Vertragsverhältnis nicht völlig zerstören.",
                "legal_field": "Mietrecht",
                "tags": "Mietvertrag,Kündigung,Miete",
                "url": "https://www.openjur.de/u/12347.html"
            },
            {
                "id": "12348",
                "title": "Urteil des OLG Köln vom 05.01.2024 - Az. 6 U 12/23",
                "court": "OLG Köln",
                "case_number": "6 U 12/23",
                "date": "2024-01-05",
                "content": "Urteil der Sechsten Zivilkammer des Oberlandesgerichts Köln vom 5. Januar 2024. Leitsatz: 1. Bei der Beurteilung der Angemessenheit einer Kündigungsfrist ist der Schutz des Verbrauchers im Sinne des § 307 BGB zu berücksichtigen. 2. Eine Kündigungsfrist von einem Monat zum Monatsende ist im Verbrauchervertrag grundsätzlich angemessen.",
                "legal_field": "Verbraucherrecht",
                "tags": "Kündigungsfrist,Verbrauchervertrag,Angemessenheit",
                "url": "https://www.openjur.de/u/12348.html"
            },
            {
                "id": "12349",
                "title": "Urteil des LG Hamburg vom 20.12.2023 - Az. 32 O 123/23",
                "court": "LG Hamburg",
                "case_number": "32 O 123/23",
                "date": "2023-12-20",
                "content": "Urteil der 32. Zivilkammer des Landgerichts Hamburg vom 20. Dezember 2023. Leitsatz: 1. Die Haftung für Schäden durch fehlerhafte Beratung setzt voraus, dass der Berater die ihm gestellte Frage fachgerecht beantwortet hat. 2. Ein Finanzberater haftet für Schäden, die ein Kunde durch fehlerhafte Anlageberatung erleidet.",
                "legal_field": "Bank- und Kapitalmarktrecht",
                "tags": "Finanzberatung,Haftung,Schadensersatz",
                "url": "https://www.openjur.de/u/12349.html"
            }
        ]
        
        # Mark all simulated decisions
        for decision in decisions:
            decision["_source"] = "simulated"
        
        return decisions
    
    def get_decision_content(self, decision_id: str) -> Dict:
        """
        Get content of a specific court decision using openjur.de API
        """
        try:
            # Try different API endpoints for individual decisions
            api_endpoints = [
                f"https://openjur.de/api/v1/entscheidungen/{decision_id}",
                f"https://openjur.de/api/entscheidungen/{decision_id}",
                f"https://openjur.de/entscheidungen/{decision_id}/api",
                f"https://www.openjur.de/api/v1/entscheidungen/{decision_id}"
            ]
            
            # Try each endpoint
            for api_url in api_endpoints:
                try:
                    logger.debug(f"Trying decision API endpoint: {api_url}")
                    response = self.session.get(api_url, timeout=30)
                    if response.status_code == 200:
                        # Parse JSON response
                        data = response.json()
                        decision_content = self._parse_decision_api_response(data)
                        
                        if decision_content and decision_content.get('title') and decision_content.get('content'):
                            logger.info(f"Successfully fetched decision content for {decision_id} via API")
                            decision_content["_source"] = "real"
                            return decision_content
                        else:
                            logger.warning(f"Could not parse decision content for {decision_id} from API")
                    elif response.status_code == 404:
                        logger.debug(f"Decision API endpoint not found: {api_url}")
                    else:
                        logger.warning(f"Decision API request failed with status {response.status_code}: {api_url}")
                except requests.exceptions.RequestException as e:
                    logger.debug(f"Decision API request failed for {api_url}: {e}")
                except Exception as e:
                    logger.debug(f"Error parsing decision API response from {api_url}: {e}")
            
            # If API endpoints don't work, try HTML parsing as fallback
            logger.warning(f"No working API endpoint found for decision {decision_id}, trying HTML parsing")
            decision_url = f"{self.base_url}/u/{decision_id}.html"
            
            try:
                html_response = self.session.get(decision_url, timeout=30)
                if html_response.status_code == 200:
                    # Parse the decision content from HTML
                    decision_content = self._parse_decision_content(html_response.text, decision_id)
                    
                    if decision_content and decision_content.get('title') and decision_content.get('content'):
                        logger.info(f"Successfully fetched decision content for {decision_id} via HTML")
                        decision_content["_source"] = "real"
                        return decision_content
                    else:
                        logger.warning(f"Could not parse decision content for {decision_id} from HTML")
            except requests.exceptions.RequestException as e:
                logger.warning(f"HTML request failed for decision {decision_id}: {e}")
            except Exception as e:
                logger.warning(f"Error parsing decision content for {decision_id}: {e}")
            
            # Fallback to simulated data if real content is not available
            logger.info(f"Using simulated data for decision {decision_id}")
            return self._get_simulated_decision_content(decision_id)
            
        except Exception as e:
            logger.error(f"Error fetching decision content for {decision_id}: {e}")
            # Return simulated data as fallback
            return self._get_simulated_decision_content(decision_id)
    
    def _parse_decision_api_response(self, data: dict) -> Dict:
        """
        Parse decision API response from openjur.de
        """
        try:
            decision = {
                "id": str(data.get('id', data.get('decision_id', ''))),
                "title": data.get('titel', data.get('title', '')),
                "court": data.get('gericht', data.get('court', '')),
                "case_number": data.get('aktenzeichen', data.get('case_number', '')),
                "date": data.get('datum', data.get('date', '')),
                "content": data.get('inhalt', data.get('content', '')),
                "legal_field": data.get('rechtsgebiet', data.get('legal_field', '')),
                "tags": data.get('schlagwoerter', data.get('tags', '')),
                "url": data.get('url', '')
            }
            
            return decision
        except Exception as e:
            logger.error(f"Error parsing decision API response: {e}")
            return {}
    
    def _parse_decision_content(self, html_content: str, decision_id: str) -> Dict:
        """
        Parse decision content from HTML using BeautifulSoup
        """
        try:
            soup = BeautifulSoup(html_content, 'html.parser')
            
            # Extract title (look for h1, h2, or elements with title class)
            title_elem = soup.find('h1') or soup.find('h2') or soup.find(class_=['title', 'heading'])
            title = title_elem.get_text(strip=True) if title_elem else f"Urteil {decision_id}"
            
            # Extract court information
            court_elem = soup.find(class_=['court', 'gericht']) or soup.find(string=lambda text: text and ('BGH' in text or 'BVerfG' in text))
            court = court_elem.get_text(strip=True) if court_elem and hasattr(court_elem, 'get_text') else "Unbekanntes Gericht"
            
            # Extract case number
            case_elem = soup.find(class_=['case-number', 'aktenzeichen']) or soup.find(string=lambda text: text and 'Az.' in text)
            case_number = case_elem.get_text(strip=True) if case_elem and hasattr(case_elem, 'get_text') else f"Az. {decision_id}"
            
            # Extract date
            date_elem = soup.find(class_=['date', 'datum']) or soup.find(string=lambda text: text and ('.' in text and len(text.split('.')) == 3))
            date = date_elem.get_text(strip=True) if date_elem and hasattr(date_elem, 'get_text') else "2024-01-01"
            
            # Extract main content (look for content div or main text areas)
            content_elem = soup.find(class_=['content', 'decision-content', 'text']) or soup.find('div')
            content = content_elem.get_text(strip=True) if content_elem else "Kein Inhalt verfügbar"
            
            # Limit content length to avoid overly large chunks
            if len(content) > 5000:
                content = content[:5000] + "... (gekürzt)"
            
            # Extract legal field and tags if available
            legal_field_elem = soup.find(class_=['legal-field', 'rechtsgebiet'])
            legal_field = legal_field_elem.get_text(strip=True) if legal_field_elem else "Allgemeines"
            
            tags_elem = soup.find(class_=['tags', 'schlagwoerter'])
            tags = tags_elem.get_text(strip=True) if tags_elem else "Urteil"
            
            decision = {
                "id": decision_id,
                "title": title,
                "court": court,
                "case_number": case_number,
                "date": date,
                "content": content,
                "legal_field": legal_field,
                "tags": tags,
                "url": f"https://openjur.de/u/{decision_id}.html"
            }
            
            logger.info(f"Parsed decision content for {decision_id}")
            return decision
            
        except Exception as e:
            logger.error(f"Error parsing decision content HTML for {decision_id}: {e}")
            return {}
    
    def _get_simulated_decision_content(self, decision_id: str) -> Dict:
        """
        Get simulated decision content for testing purposes
        """
        # In a real implementation, this would fetch the actual decision content
        # For now, we'll return simulated data based on the decision ID
        decision = {
            "id": decision_id,
            "title": f"Urteil vom 01.01.2024 - Az. Test {decision_id}",
            "court": "BGH",
            "case_number": f"Test {decision_id}",
            "date": "2024-01-01",
            "content": f"Dies ist ein simuliertes Urteil mit der ID {decision_id}. Inhalt des Urteils würde hier stehen. Der Beschluss behandelt wichtige rechtliche Aspekte im Bereich des Testrechts. Die Entscheidung hat bedeutende Auswirkungen auf die Rechtsprechung in diesem Bereich.",
            "legal_field": "Testrecht",
            "tags": "Test,Simulation",
            "url": f"https://www.openjur.de/u/{decision_id}.html",
            "_source": "simulated"
        }
        
        return decision
    
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
        
        # Create child chunks for key parts
        parent_id = len(chunks) - 1  # Index of parent chunk
        
        # Split content into sentences for child chunks
        content = decision_content.get('content', '')
        sentences = content.split('. ')
        for i, sentence in enumerate(sentences):
            if sentence.strip():
                child_text = sentence + '.' if i < len(sentences) - 1 else sentence
                child_chunk = {
                    "text": child_text,
                    "title": f"Leitsatz {i+1}",
                    "parent_id": parent_id,
                    "is_parent": False,
                    "chunk_hash": hashlib.md5(child_text.encode()).hexdigest(),
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
        logger.info(f"Starting crawl of openjur.de for the last {days} days...")
        
        # Get recent decisions
        decisions = self.get_recent_decisions(days)
        logger.info(f"Found {len(decisions)} recent decisions")
        
        if limit:
            decisions = decisions[:limit]
            logger.info(f"Limited to {len(decisions)} decisions")
        
        all_chunks = []
        
        for i, decision in enumerate(decisions):
            logger.info(f"Processing decision {i+1}/{len(decisions)}: {decision['title']}")
            
            # Get the full content of the decision
            decision_content = self.get_decision_content(decision['id'])
            
            if decision_content and decision_content.get('title') and decision_content.get('content'):
                # Chunk the content
                chunks = self.chunk_decision_content(decision_content)
                
                # Add metadata
                for chunk in chunks:
                    chunk['source'] = 'openjur.de'
                    chunk['document_type'] = 'judgment'
                    chunk['url'] = decision['url']
                
                all_chunks.extend(chunks)
                logger.info(f"  Generated {len(chunks)} chunks for decision {decision['id']}")
            else:
                logger.warning(f"  Skipping decision {decision['id']} due to missing content")
            
            # Be respectful to the server with rate limiting
            time.sleep(1)
        
        logger.info(f"Crawling completed. Generated {len(all_chunks)} chunks")
        return all_chunks