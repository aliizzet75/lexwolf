import pytest
import os
import sys

# Add backend to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))

def test_query_router_exists():
    """Test that the query router module exists"""
    try:
        from services.query_router import route_query
        assert route_query is not None
    except ImportError:
        pytest.fail("Failed to import route_query from services.query_router")

def test_route_query_returns_simple_or_complex():
    """Test that route_query returns 'simple' or 'complex'"""
    from services.query_router import route_query
    
    # Test simple query
    result = route_query("Was besagt § 1 BGB?")
    assert result in ['simple', 'complex'], f"Expected 'simple' or 'complex', got {result}"
    
    # Test complex query
    result = route_query("Welchen Zusammenhang gibt es zwischen § 1 BGB und § 23 KSchG?")
    assert result in ['simple', 'complex'], f"Expected 'simple' or 'complex', got {result}"

def test_multiple_references_complex():
    """Test that queries with multiple references are classified as complex"""
    from services.query_router import route_query
    
    # Query with multiple references should be complex
    result = route_query("Wie verhält sich § 1 BGB zu § 23 KSchG?")
    assert result == 'complex', f"Expected 'complex' for multiple references, got {result}"
    
    # Query with three references should be complex
    result = route_query("Welche Beziehung besteht zwischen § 1 BGB, § 23 KSchG und § 622 BGB?")
    assert result == 'complex', f"Expected 'complex' for multiple references, got {result}"

def test_complex_keywords():
    """Test that queries with complex keywords are classified as complex"""
    from services.query_router import route_query
    
    # Query with 'verhältnis' should be complex
    result = route_query("Welches Verhältnis besteht zwischen diesen Paragraphen?")
    assert result == 'complex', f"Expected 'complex' for keyword 'verhältnis', got {result}"
    
    # Query with 'zusammenhang' should be complex
    result = route_query("Gibt es einen Zusammenhang zwischen diesen Gesetzen?")
    assert result == 'complex', f"Expected 'complex' for keyword 'zusammenhang', got {result}"
    
    # Query with 'verweist' should be complex
    result = route_query("Auf welche Paragraphen verweist § 1 BGB?")
    assert result == 'complex', f"Expected 'complex' for keyword 'verweist', got {result}"

def test_simple_facts_simple():
    """Test that simple factual questions are classified as simple"""
    from services.query_router import route_query
    
    # Simple factual question should be simple
    result = route_query("Was besagt § 1 BGB?")
    assert result == 'simple', f"Expected 'simple' for factual question, got {result}"
    
    # Simple question about one paragraph should be simple
    result = route_query("Kündigungsfrist nach § 622 BGB?")
    assert result == 'simple', f"Expected 'simple' for simple question, got {result}"

if __name__ == "__main__":
    pytest.main([__file__])