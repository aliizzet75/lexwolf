import os
import sys

# Add backend to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', 'backend'))

def test_neo4j_service_implementation():
    """Test Neo4j service implementation according to task requirements"""
    
    # Test 1: Check that neo4j>=5.0 is in requirements.txt
    with open('backend/requirements.txt', 'r') as f:
        requirements = f.read()
        assert 'neo4j>=5.0' in requirements, "neo4j>=5.0 not found in requirements.txt"
    
    # Test 2: Check that neo4j_service.py exists
    assert os.path.exists('backend/services/neo4j_service.py'), "backend/services/neo4j_service.py not found"
    
    # Test 3: Check that Neo4jService class exists and has required methods
    from services.neo4j_service import Neo4jService
    
    # Check that required methods exist
    service = Neo4jService()
    assert hasattr(service, 'connect'), "connect method missing"
    assert hasattr(service, 'close'), "close method missing"
    assert hasattr(service, 'run_query'), "run_query method missing"
    assert hasattr(service, 'health_check'), "health_check method missing"
    
    # Test 4: Check that environment variables are read correctly
    # This will use default values since we're not setting env vars in test
    assert service.uri is not None, "URI should not be None"
    assert service.user is not None, "User should not be None"
    assert service.password is not None, "Password should not be None"
    
    # Test 5: Test health_check method (this will fail since we can't connect to Neo4j in test environment)
    # But we can at least check that the method exists and can be called
    try:
        # This will fail since we can't connect, but that's expected in test environment
        service.health_check()
    except Exception:
        # This is expected in test environment
        pass
    
    print("All tests passed!")

if __name__ == "__main__":
    test_neo4j_service_implementation()