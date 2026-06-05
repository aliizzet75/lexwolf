import pytest
import os
import sys

# Add backend to path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))

def test_import_script_exists():
    """Test that the import script exists"""
    script_path = os.path.join(os.path.dirname(__file__), '../../backend/scripts/import_to_neo4j.py')
    assert os.path.exists(script_path), "import_to_neo4j.py should exist"

def test_import_script_is_executable():
    """Test that the import script is executable"""
    script_path = os.path.join(os.path.dirname(__file__), '../../backend/scripts/import_to_neo4j.py')
    assert os.path.exists(script_path), "import_to_neo4j.py should exist"
    # Check if file has execute permissions
    assert os.access(script_path, os.X_OK), "import_to_neo4j.py should be executable"

def test_neo4j_service_import():
    """Test that Neo4jService can be imported"""
    try:
        from services.neo4j_service import Neo4jService
        assert Neo4jService is not None
    except ImportError:
        pytest.fail("Failed to import Neo4jService")

def test_paragraph_parser_import():
    """Test that paragraph_parser can be imported"""
    try:
        from services.paragraph_parser import extract_references
        assert extract_references is not None
    except ImportError:
        pytest.fail("Failed to import extract_references from paragraph_parser")

def test_script_uses_paragraph_parser():
    """Test that the script uses paragraph_parser.extract_references"""
    script_path = os.path.join(os.path.dirname(__file__), '../../backend/scripts/import_to_neo4j.py')
    
    # Read the script content
    with open(script_path, 'r') as f:
        content = f.read()
    
    # Check that it imports extract_references
    assert 'from services.paragraph_parser import extract_references' in content, "Script should import extract_references"
    
    # Check that it calls extract_references
    assert 'extract_references(' in content, "Script should call extract_references"

def test_script_creates_paragraph_nodes():
    """Test that the script creates Paragraph nodes in Neo4j"""
    script_path = os.path.join(os.path.dirname(__file__), '../../backend/scripts/import_to_neo4j.py')
    
    # Read the script content
    with open(script_path, 'r') as f:
        content = f.read()
    
    # Check that it creates Paragraph nodes
    assert 'MERGE (p:Paragraph' in content, "Script should create Paragraph nodes with MERGE"
    assert 'paragraph_nr' in content, "Script should include paragraph_nr in node properties"
    assert 'gesetz' in content, "Script should include gesetz in node properties"

def test_script_prevents_duplicates():
    """Test that the script uses MERGE to prevent duplicates"""
    script_path = os.path.join(os.path.dirname(__file__), '../../backend/scripts/import_to_neo4j.py')
    
    # Read the script content
    with open(script_path, 'r') as f:
        content = f.read()
    
    # Check that it uses MERGE
    assert 'MERGE (p:Paragraph' in content, "Script should use MERGE to prevent duplicates"

def test_script_returns_node_count():
    """Test that the script can count nodes in Neo4j"""
    script_path = os.path.join(os.path.dirname(__file__), '../../backend/scripts/import_to_neo4j.py')
    
    # Read the script content
    with open(script_path, 'r') as f:
        content = f.read()
    
    # Check that it counts nodes
    assert 'MATCH (p:Paragraph) RETURN count(p)' in content, "Script should count Paragraph nodes"

if __name__ == "__main__":
    pytest.main([__file__])