import asyncio
import time
from datetime import datetime, time as dt_time
from crawlers.main_crawler import LegalDatabaseCrawler

class CrawlScheduler:
    """
    Scheduler for nightly legal database crawling
    """
    
    def __init__(self):
        self.crawler = LegalDatabaseCrawler()
        self.running = False
    
    async def run_nightly_crawl(self):
        """
        Run the crawler once
        """
        print(f"Starting nightly crawl at {datetime.now()}")
        await self.crawler.crawl_all_sources()
        print(f"Nightly crawl completed at {datetime.now()}")
    
    def should_run_now(self) -> bool:
        """
        Check if it's time to run the nightly crawl (3:00 AM)
        """
        now = datetime.now()
        # Check if it's 3:00 AM
        return now.hour == 3 and now.minute == 0
    
    async def start_scheduler(self):
        """
        Start the scheduler to run nightly crawls
        """
        self.running = True
        print("Crawl scheduler started")
        
        while self.running:
            if self.should_run_now():
                try:
                    await self.run_nightly_crawl()
                except Exception as e:
                    print(f"Error during nightly crawl: {e}")
                
                # Wait until next day to avoid multiple runs in the same hour
                await asyncio.sleep(3600)  # Wait 1 hour
            else:
                # Check every minute
                await asyncio.sleep(60)
    
    def stop_scheduler(self):
        """
        Stop the scheduler
        """
        self.running = False
        print("Crawl scheduler stopped")

# For testing purposes
async def main():
    """
    Main function for testing
    """
    scheduler = CrawlScheduler()
    
    # Run one crawl for testing
    await scheduler.run_nightly_crawl()

if __name__ == "__main__":
    asyncio.run(main())