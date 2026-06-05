import pytest
import os
import sys
import subprocess

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))

SCRIPT_PATH = os.path.join(os.path.dirname(__file__), '../../backend/scripts/import_to_neo4j.py')
PROJECT_ROOT = os.path.join(os.path.dirname(__file__), '../..')


def neo4j_ok():
    import socket
    s = socket.socket()
    r = s.connect_ex(('localhost', 7687))
    s.close()
    return r == 0


def test_script_creates_relationships_with_merge():
    with open(SCRIPT_PATH) as f:
        content = f.read()
    assert 'VERWEIST_AUF' in content, 'Script muss Relationship-Typ VERWEIST_AUF enthalten'
    assert 'MERGE' in content and '-[' in content, 'Script muss Relationships per MERGE anlegen'


@pytest.mark.skipif(not neo4j_ok(), reason='Neo4j nicht erreichbar auf port 7687')
def test_relationships_exist_after_import():
    from services.neo4j_service import Neo4jService
    subprocess.run(
        ['python3', 'backend/scripts/import_to_neo4j.py'],
        cwd=PROJECT_ROOT, check=True
    )
    svc = Neo4jService(uri='bolt://localhost:7687', user='neo4j', password='lexwolf123')
    assert svc.connect(), 'Neo4j Verbindung fehlgeschlagen'
    try:
        result = svc.run_query('MATCH ()-[r]->() RETURN count(r) AS cnt')
        assert result[0]['cnt'] > 0, 'Keine Relationships nach Import gefunden'
    finally:
        svc.close()
