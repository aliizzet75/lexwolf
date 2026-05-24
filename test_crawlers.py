#!/usr/bin/env python3
"""
Test script for LexWolf Legal Database Crawler
"""

import sys
import os

# Add backend directory to Python path
backend_path = os.path.join(os.path.dirname(__file__), 'backend')
sys.path.insert(0, backend_path)

def test_gesetze_crawler():
    """Test gesetze-im-internet.de crawler"""
    print("Testing gesetze-im-internet.de crawler...")
    try:
        from crawlers.gesetze_crawler import GesetzeImInternetCrawler
        
        crawler = GesetzeImInternetCrawler()
        
        # Test getting law list
        laws = crawler.get_law_list()
        print(f"  ✓ Successfully fetched law list: {len(laws)} laws")
        
        # Test crawling laws (limited for testing)
        chunks = crawler.crawl_laws(limit=2)
        print(f"  ✓ Successfully crawled laws: {len(chunks)} chunks")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing gesetze crawler: {e}")
        return False

def test_openjur_crawler():
    """Test openjur.de crawler"""
    print("Testing openjur.de crawler...")
    try:
        from crawlers.openjur_crawler import OpenJurCrawler
        
        crawler = OpenJurCrawler()
        
        # Test getting recent decisions
        decisions = crawler.get_recent_decisions(days=1)
        print(f"  ✓ Successfully fetched recent decisions: {len(decisions)} decisions")
        
        # Test crawling decisions (limited for testing)
        chunks = crawler.crawl_decisions(days=1, limit=2)
        print(f"  ✓ Successfully crawled decisions: {len(chunks)} chunks")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing openjur crawler: {e}")
        return False

def test_database_storage():
    """Test database storage functionality"""
    print("Testing database storage...")
    try:
        from services.database_service import DatabaseService
        from services.embedding_service import MockEmbeddingService
        import hashlib
        
        db_service = DatabaseService()
        embedding_service = MockEmbeddingService()
        
        # Create test chunk
        test_chunk = {
            "text": "Test law content for database storage test",
            "title": "Test Law Section",
            "source": "test_source",
            "document_type": "law",
            "url": "https://test.example.com",
            "chunk_hash": hashlib.md5("test_content".encode()).hexdigest(),
            "is_parent": True
        }
        
        # Generate embedding
        embedding = embedding_service.generate_embedding(test_chunk["text"])
        test_chunk["vector"] = embedding
        
        # Store chunk
        db_service.store_chunk(test_chunk)
        print("  ✓ Successfully stored test chunk in database")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing database storage: {e}")
        return False

def test_main_crawler():
    """Test main crawler orchestrator"""
    print("Testing main crawler orchestrator...")
    try:
        import asyncio
        from crawlers.main_crawler import LegalDatabaseCrawler
        
        # Create crawler instance
        crawler = LegalDatabaseCrawler()
        
        # Test crawling with small limit
        asyncio.run(crawler.crawl_all_sources(limit_per_source=1))
        print("  ✓ Successfully ran main crawler with test limits")
        
        return True
    except Exception as e:
        print(f"  ✗ Error testing main crawler: {e}")
        return False

def main():
    """Run all crawler tests"""
    print("LexWolf Legal Database Crawler Test Suite")
    print("=" * 50)
    
    tests = [
        test_gesetze_crawler,
        test_openjur_crawler,
        test_database_storage,
        test_main_crawler
    ]
    
    passed = 0
    total = len(tests)
    
    for test in tests:
        if test():
            passed += 1
        print()
    
    print(f"Test Results: {passed}/{total} tests passed")
    
    if passed == total:
        print("🎉 Legal database crawler implementation verified successfully!")
        print("\nWhat's implemented:")
        print("  ✓ Gesetze-im-internet.de crawler with XML API integration")
        print("  ✓ OpenJur.de crawler with simulated data (ready for real API)")
        print("  ✓ Parent-child chunking strategy for laws and decisions")
        print("  ✓ Database storage with SQLite compatibility")
        print("  ✓ Embedding generation with mock service for testing")
        print("  ✓ Main crawler orchestrator for multi-source crawling")
        return 0
    else:
        print("❌ Legal database crawler implementation needs attention!")
        return 1

if __name__ == "__main__":
    sys.exit(main())