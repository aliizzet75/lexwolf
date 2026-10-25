"""
Query Router Service

This service determines whether a query should be routed to pgvector (simple)
or Neo4j (complex) based on the content of the query.
"""

from typing import List
import sys
import os

# Add backend to path so we can import our modules
sys.path.append(os.path.join(os.path.dirname(__file__), '..'))

from services.paragraph_parser import extract_references

# Keywords that indicate a complex query requiring graph traversal
COMPLEX_KEYWORDS = [
    # explizite Beziehungsanfragen
    'verhältnis', 'zusammenhang', 'verweist', 'bezieht sich',
    'beziehung', 'verknüpft', 'referenziert', 'abhängigkeit',
    'kette', 'verfolgen', 'netzwerk',
    # juristische Querverweise
    'im zusammenhang mit', 'in verbindung mit', 'gemäß', 'nach maßgabe',
    'voraussetzung', 'anwendungsbereich', 'ausnahme von',
    # Paragraphenketten (z.B. "§1 KSchG und §23 KSchG")
    'und §', 'sowie §', 'i.v.m.', 'i. v. m.',
]

def route_query(query_text: str) -> str:
    """
    Determine if a query should be routed to pgvector (simple) or Neo4j (complex).
    
    Args:
        query_text (str): The user's query text
        
    Returns:
        str: 'simple' or 'complex'
    """
    # Extract paragraph references from the query
    references = extract_references(query_text)
    
    # If more than one reference is found, it's complex
    if len(references) > 1:
        return 'complex'
    
    # Check for complex keywords
    query_lower = query_text.lower()
    for keyword in COMPLEX_KEYWORDS:
        if keyword in query_lower:
            return 'complex'
    
    # Default to simple for everything else
    return 'simple'


def needs_citation_traversal(query_text: str) -> bool:
    """
    Entscheidet, ob eine Anfrage den um Urteile/Begründungen erweiterten
    Zitier-Graphen durchsuchen soll (T#294: ZITIERT/FOLGT/ERLAEUTERT-Kanten,
    siehe services.neo4j_service und services.react_engine.traverse_citation_graph).

    Unabhängig von route_query(), das nur entscheidet ob pgvector allein
    reicht oder zusätzlich §-zu-§-Traversal (VERWEIST_AUF) via Neo4j nötig ist:
    jede Anfrage mit mindestens einer konkreten §-Referenz profitiert vom
    Zitier-Graphen (automatisch verknüpfte Urteile + Begründung finden) —
    auch wenn route_query() sie als 'simple' einstuft (z.B. "Was besagt § 1
    BGB?" bleibt für pgvector 'simple', soll aber trotzdem Urteile zu § 1 BGB
    nachladen).

    Args:
        query_text (str): The user's query text

    Returns:
        bool: True wenn mindestens eine §-Referenz gefunden wurde
    """
    return len(extract_references(query_text)) > 0