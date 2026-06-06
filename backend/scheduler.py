import asyncio
from datetime import datetime
import logging
from typing import Optional
import os
from crawlers.main_crawler import LegalDatabaseCrawler, update_neo4j
from services.neo4j_service import Neo4jService

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)

NEO4J_URI = os.getenv("NEO4J_URI", "bolt://localhost:7687")
NEO4J_USER = os.getenv("NEO4J_USER", "neo4j")
NEO4J_PASSWORD = os.getenv("NEO4J_PASSWORD", "lexwolf123")


class CrawlScheduler:
    """Scheduler for nightly legal database crawling with parallel Neo4j update."""

    def __init__(self, crawl_hour: int = 3, crawl_minute: int = 0):
        self.crawler = LegalDatabaseCrawler()
        self.running = False
        self.crawl_hour = crawl_hour
        self.crawl_minute = crawl_minute
        self.last_run: Optional[datetime] = None
        self.run_count = 0
        os.makedirs("logs", exist_ok=True)

    async def run_nightly_crawl(self, limit_per_source: int = None):
        """Run crawler: pgvector update followed by Neo4j update."""
        start_time = datetime.now()
        logger.info(f"Starting nightly crawl at {start_time}")

        try:
            await self.crawler.crawl_all_sources(limit_per_source=limit_per_source)

            self.last_run = datetime.now()
            self.run_count += 1
            duration = datetime.now() - start_time
            logger.info(f"Nightly crawl (incl. Neo4j update via {NEO4J_URI}) completed in {duration}")

            with open("logs/crawl_success.log", "a") as f:
                f.write(f"{start_time} - SUCCESS - Duration: {duration}\n")

            return True

        except Exception as e:
            logger.error(f"Error during nightly crawl: {e}")
            logger.exception(e)
            with open("logs/crawl_errors.log", "a") as f:
                f.write(f"{start_time} - ERROR - {str(e)}\n")
            return False

    def should_run_now(self) -> bool:
        now = datetime.now()
        if now.hour == self.crawl_hour and now.minute == self.crawl_minute:
            if self.last_run is None:
                return True
            if self.last_run.date() != now.date():
                return True
        return False

    def get_next_run_time(self) -> datetime:
        from datetime import timedelta
        now = datetime.now()
        next_run = now.replace(
            hour=self.crawl_hour, minute=self.crawl_minute, second=0, microsecond=0
        )
        if now.time() >= next_run.time():
            next_run += timedelta(days=1)
        return next_run

    async def start_scheduler(self, test_mode: bool = False):
        self.running = True
        logger.info(
            f"Scheduler started — {self.crawl_hour:02d}:{self.crawl_minute:02d}, "
            f"Neo4j={NEO4J_URI}"
        )

        if test_mode:
            logger.info("Test mode: running one crawl immediately")
            await self.run_nightly_crawl(limit_per_source=2)
            self.running = False
            return

        logger.info(f"Next scheduled run: {self.get_next_run_time()}")

        while self.running:
            try:
                if self.should_run_now():
                    logger.info("Scheduled time — starting crawl + Neo4j update")
                    await self.run_nightly_crawl()
                    await asyncio.sleep(3600)
                else:
                    await asyncio.sleep(60)
            except asyncio.CancelledError:
                logger.info("Scheduler cancelled")
                break
            except Exception as e:
                logger.error(f"Unexpected error: {e}")
                await asyncio.sleep(60)

    def stop_scheduler(self):
        self.running = False
        logger.info("Crawl scheduler stopped")

    def get_status(self) -> dict:
        return {
            "running": self.running,
            "crawl_hour": self.crawl_hour,
            "crawl_minute": self.crawl_minute,
            "last_run": self.last_run.isoformat() if self.last_run else None,
            "run_count": self.run_count,
            "next_run": self.get_next_run_time().isoformat(),
            "neo4j_uri": NEO4J_URI,
        }


async def main():
    import argparse

    parser = argparse.ArgumentParser(description="LexWolf Crawl Scheduler")
    parser.add_argument("--test", action="store_true")
    parser.add_argument("--hour", type=int, default=3)
    parser.add_argument("--minute", type=int, default=0)
    args = parser.parse_args()

    scheduler = CrawlScheduler(crawl_hour=args.hour, crawl_minute=args.minute)

    if args.test:
        await scheduler.start_scheduler(test_mode=True)
    else:
        try:
            await scheduler.start_scheduler()
        except KeyboardInterrupt:
            scheduler.stop_scheduler()


if __name__ == "__main__":
    asyncio.run(main())
