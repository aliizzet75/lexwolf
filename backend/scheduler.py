import asyncio
import time
from datetime import datetime, time as dt_time
import logging
from typing import Optional
import os
from crawlers.main_crawler import LegalDatabaseCrawler

# Set up logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

class CrawlScheduler:
    """
    Enhanced scheduler for nightly legal database crawling
    """
    
    def __init__(self, crawl_hour: int = 3, crawl_minute: int = 0):
        self.crawler = LegalDatabaseCrawler()
        self.running = False
        self.crawl_hour = crawl_hour
        self.crawl_minute = crawl_minute
        self.last_run: Optional[datetime] = None
        self.run_count = 0
        
        # Create logs directory if it doesn't exist
        os.makedirs("logs", exist_ok=True)
    
    async def run_nightly_crawl(self, limit_per_source: int = None):
        """
        Run the crawler once with proper error handling and logging
        """
        start_time = datetime.now()
        logger.info(f"Starting nightly crawl at {start_time}")
        
        try:
            # Run the crawl
            await self.crawler.crawl_all_sources(limit_per_source=limit_per_source)
            
            # Update last run time
            self.last_run = datetime.now()
            self.run_count += 1
            
            end_time = datetime.now()
            duration = end_time - start_time
            logger.info(f"Nightly crawl completed successfully at {end_time}")
            logger.info(f"Crawl duration: {duration}")
            logger.info(f"Total runs: {self.run_count}")
            
            # Log success to file
            with open("logs/crawl_success.log", "a") as f:
                f.write(f"{start_time} - SUCCESS - Duration: {duration}\n")
                
            return True
            
        except Exception as e:
            logger.error(f"Error during nightly crawl: {e}")
            logger.exception(e)
            
            # Log error to file
            with open("logs/crawl_errors.log", "a") as f:
                f.write(f"{start_time} - ERROR - {str(e)}\n")
            
            return False
    
    def should_run_now(self) -> bool:
        """
        Check if it's time to run the nightly crawl
        """
        now = datetime.now()
        
        # Check if it's the configured time
        if now.hour == self.crawl_hour and now.minute == self.crawl_minute:
            # Check if we haven't run today already
            if self.last_run is None:
                return True
            
            # Check if last run was not today
            if self.last_run.date() != now.date():
                return True
        
        return False
    
    def get_next_run_time(self) -> datetime:
        """
        Calculate the next scheduled run time
        """
        now = datetime.now()
        next_run = now.replace(hour=self.crawl_hour, minute=self.crawl_minute, second=0, microsecond=0)
        
        # If it's already past the scheduled time today, schedule for tomorrow
        if now.time() >= next_run.time():
            from datetime import timedelta
            next_run += timedelta(days=1)
        
        return next_run
    
    async def start_scheduler(self, test_mode: bool = False):
        """
        Start the scheduler to run nightly crawls
        """
        self.running = True
        logger.info(f"Crawl scheduler started - configured for {self.crawl_hour:02d}:{self.crawl_minute:02d}")
        
        if test_mode:
            logger.info("Running in test mode - will execute one crawl immediately")
            await self.run_nightly_crawl(limit_per_source=2)
            self.running = False
            return
        
        # Log initial status
        next_run = self.get_next_run_time()
        logger.info(f"Next scheduled run: {next_run}")
        
        while self.running:
            try:
                if self.should_run_now():
                    logger.info("Scheduled time reached - starting crawl")
                    success = await self.run_nightly_crawl()
                    
                    if success:
                        logger.info("Crawl completed successfully")
                    else:
                        logger.error("Crawl failed - check error logs")
                    
                    # Wait until next day to avoid multiple runs in the same hour
                    await asyncio.sleep(3600)  # Wait 1 hour
                else:
                    # Check every minute
                    await asyncio.sleep(60)
                    
            except asyncio.CancelledError:
                logger.info("Scheduler was cancelled")
                break
            except Exception as e:
                logger.error(f"Unexpected error in scheduler: {e}")
                logger.exception(e)
                # Wait a bit before continuing
                await asyncio.sleep(60)
    
    def stop_scheduler(self):
        """
        Stop the scheduler
        """
        self.running = False
        logger.info("Crawl scheduler stopped")
    
    def get_status(self) -> dict:
        """
        Get current scheduler status
        """
        return {
            "running": self.running,
            "crawl_hour": self.crawl_hour,
            "crawl_minute": self.crawl_minute,
            "last_run": self.last_run.isoformat() if self.last_run else None,
            "run_count": self.run_count,
            "next_run": self.get_next_run_time().isoformat()
        }

# For testing purposes
async def main():
    """
    Main function for testing
    """
    import argparse
    
    parser = argparse.ArgumentParser(description='LexWolf Crawl Scheduler')
    parser.add_argument('--test', action='store_true', help='Run in test mode (immediate execution)')
    parser.add_argument('--hour', type=int, default=3, help='Hour to run crawl (default: 3)')
    parser.add_argument('--minute', type=int, default=0, help='Minute to run crawl (default: 0)')
    
    args = parser.parse_args()
    
    scheduler = CrawlScheduler(crawl_hour=args.hour, crawl_minute=args.minute)
    
    if args.test:
        logger.info("Running scheduler in test mode")
        await scheduler.start_scheduler(test_mode=True)
    else:
        logger.info("Starting scheduler in normal mode")
        try:
            await scheduler.start_scheduler()
        except KeyboardInterrupt:
            logger.info("Received interrupt signal")
            scheduler.stop_scheduler()

if __name__ == "__main__":
    asyncio.run(main())