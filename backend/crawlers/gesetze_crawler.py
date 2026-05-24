import requests
import xml.etree.ElementTree as ET
from typing import List, Dict
from bs4 import BeautifulSoup
import time
import hashlib
import re

class GesetzeImInternetCrawler:
    """
    Crawler for gesetze-im-internet.de - German laws database
    """
    
    def __init__(self):
        self.base_url = "https://www.gesetze-im-internet.de"
        
    def get_law_list(self) -> List[Dict]:
        """
        Get list of all available laws by crawling alphabetical lists
        """
        try:
            laws = []
            
            # Crawl alphabetical lists (A-Z)
            for letter in ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 
                          'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z']:
                list_url = f"{self.base_url}/Teilliste_{letter}.html"
                print(f"Fetching law list for letter {letter}...")
                
                try:
                    response = requests.get(list_url, timeout=10)
                    response.raise_for_status()
                    
                    soup = BeautifulSoup(response.content, 'html.parser')
                    
                    # Find all law links
                    law_links = soup.find_all('a', href=re.compile(r'^/[a-z_]+/'))
                    
                    for link in law_links:
                        law_title = link.get_text().strip()
                        law_href = link.get('href', '')
                        
                        if law_title and law_href and not law_href.startswith('http'):
                            law_url = f"{self.base_url}{law_href}"
                            law = {
                                "title": law_title,
                                "link": law_url,
                                "description": f"Law starting with {letter}",
                                "pubDate": "",
                                "guid": law_url
                            }
                            laws.append(law)
                            
                except Exception as e:
                    print(f"Error fetching law list for letter {letter}: {e}")
                    continue
                    
                # Be respectful to the server
                time.sleep(0.5)
                
            return laws
        except Exception as e:
            print(f"Error fetching law list: {e}")
            return []
    
    def get_law_content(self, law_url: str) -> Dict:
        """
        Get content of a specific law
        """
        try:
            response = requests.get(law_url, timeout=10)
            response.raise_for_status()
            
            soup = BeautifulSoup(response.content, 'html.parser')
            
            # Extract law content
            content = {
                "title": "",
                "sections": [],
                "url": law_url
            }
            
            # Get law title
            title_elem = soup.find("h1")
            if title_elem:
                content["title"] = title_elem.get_text().strip()
            
            # Get all sections (Paragraphs)
            sections = soup.find_all(["h2", "h3", "h4"])
            for section in sections:
                section_data = {
                    "heading": section.get_text().strip(),
                    "content": ""
                }
                
                # Get content following the section heading
                next_elem = section.find_next_sibling()
                content_text = ""
                while next_elem and next_elem.name not in ["h2", "h3", "h4"]:
                    if next_elem.name in ["p", "div"]:
                        content_text += next_elem.get_text().strip() + "\n"
                    next_elem = next_elem.find_next_sibling()
                
                section_data["content"] = content_text.strip()
                content["sections"].append(section_data)
                
            return content
        except Exception as e:
            print(f"Error fetching law content from {law_url}: {e}")
            return {}
    
    def chunk_law_content(self, law_content: Dict) -> List[Dict]:
        """
        Chunk law content into parent-child structure
        """
        chunks = []
        
        # Create parent chunk for the entire law
        parent_text = f"{law_content.get('title', '')}\n"
        for section in law_content.get('sections', []):
            parent_text += f"{section.get('heading', '')}\n{section.get('content', '')}\n\n"
        
        parent_chunk = {
            "text": parent_text,
            "title": law_content.get('title', ''),
            "is_parent": True,
            "chunk_hash": hashlib.md5(parent_text.encode()).hexdigest()
        }
        chunks.append(parent_chunk)
        
        # Create child chunks for each section
        parent_id = len(chunks) - 1  # Index of parent chunk
        
        for i, section in enumerate(law_content.get('sections', [])):
            section_text = f"{section.get('heading', '')}\n{section.get('content', '')}"
            
            if section_text.strip():  # Only create chunk if there's content
                child_chunk = {
                    "text": section_text,
                    "title": section.get('heading', ''),
                    "parent_id": parent_id,
                    "is_parent": False,
                    "chunk_hash": hashlib.md5(section_text.encode()).hexdigest()
                }
                chunks.append(child_chunk)
        
        return chunks
    
    def crawl_laws(self, limit: int = None) -> List[Dict]:
        """
        Crawl laws from gesetze-im-internet.de
        """
        print("Starting crawl of gesetze-im-internet.de...")
        
        # Get list of laws
        laws = self.get_law_list()
        print(f"Found {len(laws)} laws")
        
        if limit:
            laws = laws[:limit]
            print(f"Limited to {len(laws)} laws")
        
        all_chunks = []
        
        for i, law in enumerate(laws):
            print(f"Processing law {i+1}/{len(laws)}: {law['title']}")
            
            # Get law content
            law_content = self.get_law_content(law['link'])
            
            if law_content:
                # Chunk the content
                chunks = self.chunk_law_content(law_content)
                
                # Add metadata
                for chunk in chunks:
                    chunk['source'] = 'gesetze-im-internet.de'
                    chunk['document_type'] = 'law'
                    chunk['url'] = law['link']
                
                all_chunks.extend(chunks)
            
            # Be respectful to the server
            time.sleep(1)
        
        print(f"Crawling completed. Generated {len(all_chunks)} chunks")
        return all_chunks