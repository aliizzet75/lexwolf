import pytest
import os
import sys
import socket

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../backend"))


def neo4j_ok():
    s = socket.socket()
    r = s.connect_ex(("localhost", 7687))
    s.close()
    return r == 0


def test_traverse_graph_exists():
    from services.react_engine import traverse_graph
    assert callable(traverse_graph)


def test_traverse_graph_returns_list():
    from services.react_engine import traverse_graph
    result = traverse_graph("par1_BGB")
    assert isinstance(result, list)


def test_traverse_graph_items_are_dicts():
    from services.react_engine import traverse_graph
    result = traverse_graph("par1_BGB")
    for item in result:
        assert isinstance(item, dict), f"Ergebnis-Item ist kein dict: {type(item)}"


@pytest.mark.skipif(not neo4j_ok(), reason="Neo4j nicht erreichbar auf port 7687")
def test_traverse_graph_neo4j_connected():
    from services.react_engine import traverse_graph
    result = traverse_graph("par23_KSchG")
    assert isinstance(result, list)


@pytest.mark.skipif(not neo4j_ok(), reason="Neo4j nicht erreichbar auf port 7687")
def test_traverse_graph_max_depth_no_loop():
    from services.react_engine import traverse_graph
    # depth=2 muss Traversal-Loops verhindern — Ergebnis muss endlich und < 500 sein
    result = traverse_graph("par23_KSchG")
    assert len(result) < 500, f"Zu viele Ergebnisse — Traversal-Loop? ({len(result)})"
