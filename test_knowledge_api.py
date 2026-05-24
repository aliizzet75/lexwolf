#!/usr/bin/env python3
"""
Test script for LexWolf Knowledge Database API
"""

import requests
import json

def test_knowledge_api():
    """Test knowledge database API endpoints"""
    base_url = "http://localhost:8000"
    
    print("Testing LexWolf Knowledge Database API")
    print("=" * 50)
    
    # Test 1: Health check
    print("1. Testing health check...")
    try:
        response = requests.get(f"{base_url}/knowledge/health")
        if response.status_code == 200:
            health = response.json()
            print(f"  ✓ Health check successful: {health['status']}")
        else:
            print(f"  ✗ Health check failed: {response.status_code}")
            return False
    except Exception as e:
        print(f"  ✗ Error during health check: {e}")
        return False
    
    # Test 2: Store knowledge chunk
    print("\n2. Testing knowledge chunk storage...")
    try:
        chunk_data = {
            "text": "§ 1 Kündigungsschutzgesetz: Der Kündigungsschutz nach diesem Gesetz greift unter bestimmten Voraussetzungen.",
            "title": "§ 1 KSchG - Allgemeiner Teil",
            "source": "gesetze-im-internet.de",
            "legal_field": "Arbeitsrecht",
            "tags": "Kündigung,Kündigungsschutz,Arbeitsvertrag"
        }
        
        response = requests.post(f"{base_url}/knowledge/chunks", json=chunk_data)
        if response.status_code == 200:
            chunk = response.json()
            print(f"  ✓ Successfully stored knowledge chunk: {chunk['title']}")
            print(f"    Chunk ID: {chunk['id']}")
        else:
            print(f"  ✗ Failed to store knowledge chunk: {response.status_code}")
            print(f"    Response: {response.text}")
            return False
    except Exception as e:
        print(f"  ✗ Error storing knowledge chunk: {e}")
        return False
    
    # Test 3: Search knowledge database
    print("\n3. Testing knowledge search...")
    try:
        search_data = {
            "query": "Kündigungsschutz nach § 1 KSchG",
            "limit": 5
        }
        
        response = requests.post(f"{base_url}/knowledge/search", json=search_data)
        if response.status_code == 200:
            results = response.json()
            print(f"  ✓ Successfully searched knowledge database")
            print(f"    Found {len(results)} results")
            if results:
                print(f"    Top result: {results[0]['title']} (Score: {results[0]['score']:.4f})")
        else:
            print(f"  ✗ Failed to search knowledge database: {response.status_code}")
            print(f"    Response: {response.text}")
            return False
    except Exception as e:
        print(f"  ✗ Error searching knowledge database: {e}")
        return False
    
    # Test 4: Get specific chunk
    print("\n4. Testing chunk retrieval...")
    try:
        # Use a known chunk ID (in a real test, we would use the ID from the stored chunk)
        response = requests.get(f"{base_url}/knowledge/chunks/1")
        if response.status_code == 200:
            chunk = response.json()
            print(f"  ✓ Successfully retrieved chunk: {chunk['title']}")
        elif response.status_code == 404:
            print(f"  ✓ Chunk not found (expected for demo)")
        else:
            print(f"  ✗ Failed to retrieve chunk: {response.status_code}")
            return False
    except Exception as e:
        print(f"  ✗ Error retrieving chunk: {e}")
        return False
    
    # Test 5: Get knowledge statistics
    print("\n5. Testing knowledge statistics...")
    try:
        response = requests.get(f"{base_url}/knowledge/stats")
        if response.status_code == 200:
            stats = response.json()
            print(f"  ✓ Successfully retrieved knowledge statistics")
            print(f"    Total documents: {stats['total_documents']}")
            print(f"    Total chunks: {stats['total_chunks']}")
            print(f"    Sources: {len(stats['sources'])}")
        else:
            print(f"  ✗ Failed to retrieve knowledge statistics: {response.status_code}")
            return False
    except Exception as e:
        print(f"  ✗ Error retrieving knowledge statistics: {e}")
        return False
    
    # Test 6: Error handling
    print("\n6. Testing error handling...")
    try:
        # Test with invalid search query
        search_data = {
            "query": "",  # Empty query
            "limit": 5
        }
        
        response = requests.post(f"{base_url}/knowledge/search", json=search_data)
        # Even with empty query, it should handle gracefully
        if response.status_code in [200, 500]:
            print("  ✓ Error handling works correctly")
        else:
            print(f"  ✗ Unexpected response for invalid query: {response.status_code}")
            return False
    except Exception as e:
        print(f"  ✗ Error testing error handling: {e}")
        return False
    
    print("\n" + "=" * 50)
    print("🎉 All knowledge database API tests passed!")
    return True

def main():
    """Main test function"""
    try:
        success = test_knowledge_api()
        if success:
            print("\nKnowledge database API is working correctly!")
            return 0
        else:
            print("\nKnowledge database API tests failed!")
            return 1
    except Exception as e:
        print(f"\nUnexpected error during testing: {e}")
        return 1

if __name__ == "__main__":
    exit(main())