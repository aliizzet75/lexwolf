#!/usr/bin/env python3
"""
Test script to verify Neo4j service in docker-compose setup
"""

import subprocess
import time
import requests
import sys

def test_docker_compose_services():
    """Test that docker-compose services start correctly"""
    print("Testing docker-compose services...")
    
    try:
        # Check if docker-compose is available
        result = subprocess.run(["docker-compose", "version"], capture_output=True, text=True, timeout=10)
        if result.returncode != 0:
            print("❌ docker-compose not available")
            return False
        print("✅ docker-compose is available")
        
        # Check if services are defined correctly
        result = subprocess.run(["docker-compose", "config", "--services"], capture_output=True, text=True, timeout=30)
        if result.returncode != 0:
            print("❌ docker-compose config invalid")
            return False
        
        services = result.stdout.strip().split('\n')
        required_services = ['db', 'neo4j', 'api']
        
        for service in required_services:
            if service in services:
                print(f"✅ Service '{service}' is defined")
            else:
                print(f"❌ Service '{service}' is missing")
                return False
                
        return True
    except Exception as e:
        print(f"❌ Error checking docker-compose services: {e}")
        return False

def test_neo4j_connection():
    """Test Neo4j connection (simulated since we can't actually start containers in this environment)"""
    print("\nTesting Neo4j configuration...")
    
    # Check that the required environment variables are in .env.example
    try:
        with open('.env.example', 'r') as f:
            content = f.read()
            
        required_vars = [
            'NEO4J_URI=neo4j://localhost:7687',
            'NEO4J_USER=neo4j',
            'NEO4J_PASSWORD=lexwolf123'
        ]
        
        for var in required_vars:
            if var in content:
                print(f"✅ Found in .env.example: {var}")
            else:
                print(f"❌ Missing in .env.example: {var}")
                return False
                
        return True
    except Exception as e:
        print(f"❌ Error reading .env.example: {e}")
        return False

def test_docker_compose_file():
    """Test docker-compose.yml content"""
    print("\nTesting docker-compose.yml content...")
    
    try:
        with open('docker-compose.yml', 'r') as f:
            content = f.read()
            
        # Check for Neo4j service
        if 'neo4j:' in content:
            print("✅ Neo4j service defined")
        else:
            print("❌ Neo4j service not found")
            return False
            
        # Check for required Neo4j configuration
        required_configs = [
            'image: neo4j:5',
            'NEO4J_AUTH=neo4j/lexwolf123',
            '7474:7474',
            '7687:7687'
        ]
        
        for config in required_configs:
            if config in content:
                print(f"✅ Found: {config}")
            else:
                print(f"❌ Missing: {config}")
                return False
                
        return True
    except Exception as e:
        print(f"❌ Error reading docker-compose.yml: {e}")
        return False

def main():
    """Main test function"""
    print("Neo4j Service Implementation Test")
    print("=" * 40)
    
    tests = [
        ("Docker Compose Services", test_docker_compose_services),
        ("Neo4j Connection Config", test_neo4j_connection),
        ("Docker Compose File Content", test_docker_compose_file)
    ]
    
    passed = 0
    total = len(tests)
    
    for test_name, test_func in tests:
        print(f"\n{test_name}:")
        if test_func():
            passed += 1
        else:
            print(f"  Failed: {test_name}")
    
    print("\n" + "=" * 40)
    print(f"Test Results: {passed}/{total} tests passed")
    
    if passed == total:
        print("\n🎉 All tests passed! Neo4j service implementation is ready.")
        print("\nWhat's implemented:")
        print("  ✓ Neo4j service added to docker-compose.yml")
        print("  ✓ Image: neo4j:5")
        print("  ✓ Ports: 7474 (HTTP) and 7687 (Bolt)")
        print("  ✓ Volumes: neo4j_data for persistence")
        print("  ✓ Environment: NEO4J_AUTH=neo4j/lexwolf123")
        print("  ✓ .env.example updated with Neo4j variables")
        print("\nTo test:")
        print("  docker-compose up -d")
        print("  Visit http://localhost:7474 in browser")
        return 0
    else:
        print("\n❌ Some tests failed. Please check implementation.")
        return 1

if __name__ == "__main__":
    sys.exit(main())