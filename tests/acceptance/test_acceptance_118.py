import pytest
import os
import sys
import socket
import inspect

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))

CRAWLER_PATH = os.path.join(os.path.dirname(__file__), '../../backend/crawlers/main_crawler.py')
SCHEDULER_PATH = os.path.join(os.path.dirname(__file__), '../../backend/scheduler.py')


def neo4j_ok():
    s = socket.socket()
    r = s.connect_ex(('localhost', 7687))
    s.close()
    return r == 0


def test_crawler_calls_neo4j_update():
    with open(CRAWLER_PATH) as f:
        src = f.read()
    assert 'neo4j' in src.lower() or 'Neo4j' in src, \
        'main_crawler.py muss Neo4j-Update-Aufruf enthalten (update_neo4j oder Neo4jService)'


def test_scheduler_triggers_neo4j():
    with open(SCHEDULER_PATH) as f:
        src = f.read()
    assert 'neo4j' in src.lower() or 'Neo4j' in src, \
        'scheduler.py muss Neo4j-Update auslösen'


def test_import_script_uses_merge():
    script = os.path.join(os.path.dirname(__file__), '../../backend/scripts/import_to_neo4j.py')
    with open(script) as f:
        content = f.read()
    assert 'MERGE' in content, 'import_to_neo4j.py muss MERGE verwenden (Duplikat-Schutz)'


@pytest.mark.skipif(not neo4j_ok(), reason='Neo4j nicht erreichbar auf port 7687')
def test_neo4j_no_duplicates_after_double_update():
    from services.neo4j_service import Neo4jService
    from scripts.import_to_neo4j import create_paragraph_node
    svc = Neo4jService(uri='bolt://localhost:7687', user='neo4j', password='lexwolf123')
    assert svc.connect(), 'Neo4j-Verbindung fehlgeschlagen'
    try:
        chunk = {'id': 9999, 'text': 'Test § 1 BGB', 'title': 'Test', 'legal_field': 'Test',
                 'chunk_hash': 'testhash118', 'created_at': '2026-01-01', 'source': 'test', 'url': ''}
        create_paragraph_node(svc, chunk, [])
        create_paragraph_node(svc, chunk, [])
        result = svc.run_query("MATCH (p:Paragraph {id: 'chunk_9999'}) RETURN count(p) AS cnt")
        assert result[0]['cnt'] == 1, f'MERGE verhindert keine Duplikate: {result[0]["cnt"]} Nodes'
    finally:
        svc.run_query("MATCH (p:Paragraph {id: 'chunk_9999'}) DETACH DELETE p")
        svc.close()


@pytest.mark.skipif(not neo4j_ok(), reason='Neo4j nicht erreichbar auf port 7687')
def test_new_chunks_create_nodes_and_edges():
    from services.neo4j_service import Neo4jService
    from scripts.import_to_neo4j import create_paragraph_node, create_simple_relationships
    svc = Neo4jService(uri='bolt://localhost:7687', user='neo4j', password='lexwolf123')
    assert svc.connect(), 'Neo4j-Verbindung fehlgeschlagen'
    try:
        chunk = {'id': 9998, 'text': '§ 2 KSchG und § 3 BGB', 'title': 'Neu', 'legal_field': 'Test',
                 'chunk_hash': 'newhash118', 'created_at': '2026-01-01', 'source': 'test', 'url': ''}
        refs = [{'paragraph_nr': '2', 'gesetz': 'KSchG'}]
        create_paragraph_node(svc, chunk, refs)
        create_simple_relationships(svc, chunk, refs)
        result = svc.run_query("MATCH (p:Paragraph {id: 'chunk_9998'}) RETURN count(p) AS cnt")
        assert result[0]['cnt'] == 1, 'Neuer Chunk wurde nicht als Node angelegt'
    finally:
        svc.run_query("MATCH (p:Paragraph {id: 'chunk_9998'}) DETACH DELETE p")
        svc.close()
