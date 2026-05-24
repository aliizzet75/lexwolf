import asyncio
from crawlers.gesetze_crawler import GesetzeImInternetCrawler
from crawlers.openjur_crawler import OpenJurCrawler
from services.embedding_service import EmbeddingService, MockEmbeddingService
from services.database_service import DatabaseService
import logging

# Set up logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class LegalDatabaseCrawler:
    """
    Main orchestrator for crawling legal databases
    """
    
    def __init__(self):
        self.gesetze_crawler = GesetzeImInternetCrawler()
        self.openjur_crawler = OpenJurCrawler()
        # Use mock embedding service for testing
        self.embedding_service = MockEmbeddingService()
        self.database_service = DatabaseService()
        
    async def crawl_all_sources(self, limit_per_source: int = None):
        """
        Crawl all legal sources and store in database
        """
        logger.info("Starting legal database crawl...")
        
        # Crawl gesetze-im-internet.de
        logger.info("Crawling gesetze-im-internet.de...")
        try:
            law_chunks = self.gesetze_crawler.crawl_laws(limit=limit_per_source)
            logger.info(f"Generated {len(law_chunks)} chunks from laws")
            
            # Generate embeddings and store in database
            await self.process_and_store_chunks(law_chunks)
        except Exception as e:
            logger.error(f"Error crawling gesetze-im-internet.de: {e}")
        
        # Crawl openjur.de
        logger.info("Crawling openjur.de...")
        try:
            decision_chunks = self.openjur_crawler.crawl_decisions(days=1, limit=limit_per_source)
            logger.info(f"Generated {len(decision_chunks)} chunks from decisions")
            
            # Generate embeddings and store in database
            await self.process_and_store_chunks(decision_chunks)
        except Exception as e:
            logger.error(f"Error crawling openjur.de: {e}")
        
        logger.info("Legal database crawl completed")
    
    async def process_and_store_chunks(self, chunks: List[Dict]):
        """
        Generate embeddings for chunks and store in database
        """
        logger.info(f"Processing {len(chunks)} chunks...")
        
        # Generate embeddings for chunks
        for i, chunk in enumerate(chunks):
            try:
                # Generate embedding using the first 200 tokens of content
                content_preview = f"{chunk.get('title', '')} {chunk.get('text', '')[:200]}"
                embedding = self.embedding_service.generate_embedding(content_preview)
                
                # Add embedding to chunk
                chunk['vector'] = embedding
                
                # Store in database
                self.database_service.store_chunk(chunk)
                
                if (i + 1) % 100 == 0:
                    logger.info(f"Processed {i + 1}/{len(chunks)} chunks")
                    
            except Exception as e:
                logger.error(f"Error processing chunk {i}: {e}")
        
        logger.info(f"Completed processing {len(chunks)} chunks")

async def main():
    """
    Main function to run the crawler
    """
    crawler = LegalDatabaseCrawler()
    
    # Run crawl with limit for testing
    await crawler.crawl_all_sources(limit_per_source=5)

if __name__ == "__main__":
    asyncio.run(main())