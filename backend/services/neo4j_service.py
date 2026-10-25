import os
import re
from typing import Dict, List, Optional
from neo4j import GraphDatabase
import logging

from services.paragraph_parser import extract_references, _is_law_abbr

# Set up logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# --- Zitier-Graph-Extraktion (T#293) -----------------------------------------
#
# Erweitert den M1-Paragraphengraphen um Urteile und Gesetzesbegründungen (M19)
# als eigene Knotentypen (:Urteil, :Begruendung) und erkennt zusätzliche
# Zitier-Muster über die reinen §-Referenzen (paragraph_parser.extract_references)
# hinaus:
#   - "§ X i.V.m. § Y BGB"            -> beide §§ zitiert, Gesetz wird geteilt
#   - "im Anschluss an BGH vom ..."   -> Urteil folgt einem anderen Urteil
#   - "vgl. BT-Drs. 18/1234"          -> Verweis auf eine Bundestags-Drucksache
#     (identifiziert die zugehörige Gesetzesbegründung, siehe
#     crawlers.drucksachen_crawler._make_chunk, das Drucksachen in Chunk-Titeln
#     referenziert: "BT-Drs. <dokumentnummer>")

# "§ 823 i.V.m. § 31 BGB" — das Gesetz steht nur einmal am Ende der Kette,
# gilt aber für beide §§.
_IVM_RE = re.compile(
    r'§\s*(\d+[a-zA-Z]?(?:\s+Abs\.\s*\d+)?)\s+i\.?\s*[Vv]\.?\s*[Mm]\.?\s+'
    r'§\s*(\d+[a-zA-Z]?(?:\s+Abs\.\s*\d+)?)\s+'
    r'([A-ZÄÖÜ][A-Za-zäöüÄÖÜß]+)'
)

# "im Anschluss an BGH vom 12.3.2021" / "im Anschluss an das Urteil des BGH,
# Urteil vom 12.3.2021 - VI ZR 1/20"
_FOLGT_RE = re.compile(
    r'im\s+Anschluss\s+an\s+(?:das\s+Urteil\s+des\s+|den\s+|der\s+)?'
    r'(BGH|BVerfG|BAG|BFH|BSG|BVerwG|OLG\s+[A-ZÄÖÜ][a-zäöüß]+)'
    r',?\s+(?:Urteil\s+)?vom\s+(\d{1,2}\.\d{1,2}\.\d{2,4})'
    r'(?:\s*[-,]\s*([A-Za-z0-9/.\s]{2,30}?))?(?=[.,;)]|$)',
    re.IGNORECASE,
)

# "vgl. BT-Drs. 18/1234" / "BT-Drs. 04/171"
_BTDRS_RE = re.compile(r'BT-Drs\.?\s*(\d{1,2}/\d+)', re.IGNORECASE)


def _urteil_stub_id(gericht: str, datum: str, aktenzeichen: str = "") -> str:
    """
    Deterministische id für einen per FOLGT-Kante referenzierten :Urteil-Stub,
    damit spätere echte Importe (create_urteil_node mit derselben id) den Stub
    per MERGE anreichern statt einen Duplikat-Knoten anzulegen.
    """
    slug = re.sub(r'[^A-Za-z0-9]+', '_', f"{gericht}_{aktenzeichen or datum}").strip('_').lower()
    return f"urteil_{slug}"


def extract_citations(text: str) -> Dict[str, List[Dict]]:
    """
    Extrahiert Zitier-Muster aus Urteils- oder Begründungstext.

    Returns:
        dict mit:
          - "paragraphen": [{paragraph_nr, gesetz, kontext}, ...]
            (reine §-Referenzen + "i.V.m."-Ketten)
          - "urteile": [{gericht, datum, aktenzeichen, kontext}, ...]
            ("im Anschluss an"-Referenzen auf andere Urteile)
          - "drucksachen": [{drucksache, kontext}, ...]
            ("BT-Drs."-Referenzen auf Gesetzesbegründungen)
    """
    if not text:
        return {"paragraphen": [], "urteile": [], "drucksachen": []}

    paragraphen = extract_references(text)
    seen = {(p["paragraph_nr"], p["gesetz"]) for p in paragraphen}

    for m in _IVM_RE.finditer(text):
        gesetz = m.group(3)
        if not _is_law_abbr(gesetz):
            continue
        for nr in (m.group(1).strip(), m.group(2).strip()):
            key = (nr, gesetz)
            if key in seen:
                continue
            seen.add(key)
            paragraphen.append({
                "paragraph_nr": nr,
                "gesetz": gesetz,
                "kontext": m.group(0),
            })

    urteile = []
    for m in _FOLGT_RE.finditer(text):
        gericht = re.sub(r'\s+', ' ', m.group(1).strip())
        datum = m.group(2).strip()
        aktenzeichen = (m.group(3) or "").strip()
        urteile.append({
            "gericht": gericht,
            "datum": datum,
            "aktenzeichen": aktenzeichen,
            "kontext": m.group(0),
        })

    drucksachen = [
        {"drucksache": m.group(1), "kontext": m.group(0)}
        for m in _BTDRS_RE.finditer(text)
    ]

    return {"paragraphen": paragraphen, "urteile": urteile, "drucksachen": drucksachen}


class Neo4jService:
    """
    Service for Neo4j database operations
    """
    
    def __init__(self, uri=None, user=None, password=None):
        """
        Initialize Neo4j service with connection parameters
        
        Args:
            uri (str, optional): Neo4j URI. Defaults to None (reads from env).
            user (str, optional): Neo4j username. Defaults to None (reads from env).
            password (str, optional): Neo4j password. Defaults to None (reads from env).
        """
        # Read connection parameters from environment variables if not provided
        self.uri = uri or os.getenv("NEO4J_URI", "bolt://localhost:7687")
        self.user = user or os.getenv("NEO4J_USER", "neo4j")
        self.password = password or os.getenv("NEO4J_PASSWORD", "password")
        
        self.driver = None
    
    def connect(self):
        """
        Establish connection to Neo4j database
        
        Returns:
            bool: True if connection successful, False otherwise
        """
        try:
            self.driver = GraphDatabase.driver(self.uri, auth=(self.user, self.password))
            logger.info("Connected to Neo4j database")
            return True
        except Exception as e:
            logger.error(f"Failed to connect to Neo4j database: {e}")
            return False
    
    def close(self):
        """
        Close connection to Neo4j database
        """
        if self.driver:
            self.driver.close()
            logger.info("Closed connection to Neo4j database")
    
    def run_query(self, cypher, params=None):
        """
        Run a Cypher query against the Neo4j database
        
        Args:
            cypher (str): Cypher query to execute
            params (dict, optional): Parameters for the query
            
        Returns:
            list: Query results
        """
        if not self.driver:
            raise Exception("No connection to Neo4j database. Call connect() first.")
        
        try:
            with self.driver.session() as session:
                result = session.run(cypher, parameters=params)
                return [record.data() for record in result]
        except Exception as e:
            logger.error(f"Error running query: {e}")
            raise
    
    def health_check(self):
        """
        Perform a health check on the Neo4j database connection
        
        Returns:
            bool: True if health check passes, False otherwise
        """
        try:
            result = self.run_query("RETURN 1 AS result")
            return len(result) > 0 and result[0]["result"] == 1
        except Exception as e:
            logger.error(f"Health check failed: {e}")
            return False
    
    def setup_schema(self):
        """
        Set up the Neo4j graph schema for legal paragraphs and relationships.
        Creates constraints and indexes for Paragraph nodes and defines relationship types.
        """
        if not self.driver:
            raise Exception("No connection to Neo4j database. Call connect() first.")
        
        try:
            # Create unique constraint on Paragraph.id
            self.run_query("CREATE CONSTRAINT paragraph_id IF NOT EXISTS FOR (p:Paragraph) REQUIRE p.id IS UNIQUE")
            logger.info("Created unique constraint on Paragraph.id")
            
            # Create index on gesetz field
            self.run_query("CREATE INDEX paragraph_gesetz IF NOT EXISTS FOR (p:Paragraph) ON (p.gesetz)")
            logger.info("Created index on Paragraph.gesetz")
            
            # Create index on paragraph_nr field
            self.run_query("CREATE INDEX paragraph_nr IF NOT EXISTS FOR (p:Paragraph) ON (p.paragraph_nr)")
            logger.info("Created index on Paragraph.paragraph_nr")
            
            # Note: Relationship types (VERWEIST_AUF, ERGAENZT, WIDERSPRICHT, KONKRETISIERT) 
            # are defined in the data model and will be created when relationships are added
            logger.info("Schema setup completed successfully")
            return True
            
        except Exception as e:
            logger.error(f"Schema setup failed: {e}")
            return False

    def setup_citation_schema(self):
        """
        Erweitert das Schema um Urteil- und Begruendung-Knoten (T#293).
        Creates constraints and indexes for :Urteil and :Begruendung nodes.
        """
        if not self.driver:
            raise Exception("No connection to Neo4j database. Call connect() first.")

        try:
            self.run_query("CREATE CONSTRAINT urteil_id IF NOT EXISTS FOR (u:Urteil) REQUIRE u.id IS UNIQUE")
            self.run_query("CREATE CONSTRAINT begruendung_id IF NOT EXISTS FOR (b:Begruendung) REQUIRE b.id IS UNIQUE")
            self.run_query("CREATE INDEX urteil_gericht IF NOT EXISTS FOR (u:Urteil) ON (u.gericht)")
            self.run_query("CREATE INDEX urteil_datum IF NOT EXISTS FOR (u:Urteil) ON (u.datum)")
            logger.info("Citation schema (Urteil/Begruendung) setup completed")
            return True
        except Exception as e:
            logger.error(f"Citation schema setup failed: {e}")
            return False

    def create_urteil_node(self, urteil_id: str, properties: Optional[Dict] = None):
        """
        Legt einen :Urteil-Knoten an (oder aktualisiert ihn), MERGE über id.
        """
        properties = dict(properties or {})
        properties["id"] = urteil_id
        query = """
        MERGE (u:Urteil {id: $id})
        SET u += $properties
        RETURN u
        """
        self.run_query(query, {"id": urteil_id, "properties": properties})
        return True

    def create_begruendung_node(self, begruendung_id: str, properties: Optional[Dict] = None):
        """
        Legt einen :Begruendung-Knoten an (oder aktualisiert ihn), MERGE über id.
        """
        properties = dict(properties or {})
        properties["id"] = begruendung_id
        query = """
        MERGE (b:Begruendung {id: $id})
        SET b += $properties
        RETURN b
        """
        self.run_query(query, {"id": begruendung_id, "properties": properties})
        return True

    def link_urteil_citations(self, urteil_id: str, text: str) -> Dict[str, List[Dict]]:
        """
        Extrahiert Zitier-Muster aus einem Urteilstext und speichert sie als Kanten:
          (Urteil)-[ZITIERT]->(Paragraph)
          (Urteil)-[FOLGT]->(Urteil)

        Der Ziel-:Urteil-Knoten einer FOLGT-Kante wird per MERGE als Stub angelegt
        (gericht/datum/aktenzeichen), falls das zitierte Urteil noch nicht separat
        importiert wurde — er wird automatisch angereichert, sobald es später über
        create_urteil_node importiert wird (MERGE auf derselben id).
        """
        citations = extract_citations(text)

        for ref in citations["paragraphen"]:
            self.run_query(
                """
                MATCH (u:Urteil {id: $uid})
                MERGE (p:Paragraph {paragraph_nr: $nr, gesetz: $gesetz})
                MERGE (u)-[r:ZITIERT]->(p)
                SET r.created_at = timestamp()
                """,
                {"uid": urteil_id, "nr": ref["paragraph_nr"], "gesetz": ref["gesetz"]},
            )

        for ref in citations["urteile"]:
            target_id = _urteil_stub_id(ref["gericht"], ref["datum"], ref["aktenzeichen"])
            self.run_query(
                """
                MATCH (u:Urteil {id: $uid})
                MERGE (target:Urteil {id: $target_id})
                ON CREATE SET target.gericht = $gericht, target.datum = $datum,
                              target.aktenzeichen = $aktenzeichen
                MERGE (u)-[r:FOLGT]->(target)
                SET r.created_at = timestamp()
                """,
                {
                    "uid": urteil_id,
                    "target_id": target_id,
                    "gericht": ref["gericht"],
                    "datum": ref["datum"],
                    "aktenzeichen": ref["aktenzeichen"],
                },
            )

        return citations

    def link_begruendung_citations(self, begruendung_id: str, text: str) -> Dict[str, List[Dict]]:
        """
        Extrahiert §-Bezüge aus einem Begründungstext und speichert sie als Kante:
          (Begruendung)-[ERLAEUTERT]->(Paragraph)
        """
        citations = extract_citations(text)

        for ref in citations["paragraphen"]:
            self.run_query(
                """
                MATCH (b:Begruendung {id: $bid})
                MERGE (p:Paragraph {paragraph_nr: $nr, gesetz: $gesetz})
                MERGE (b)-[r:ERLAEUTERT]->(p)
                SET r.created_at = timestamp()
                """,
                {"bid": begruendung_id, "nr": ref["paragraph_nr"], "gesetz": ref["gesetz"]},
            )

        return citations

    def get_urteile_fuer_paragraph(self, paragraph_nr: str, gesetz: str) -> List[Dict]:
        """
        DoD T#293: 'alle Urteile zu §X mit Begründung'.

        Liefert pro §X die zitierenden Urteile sowie die erläuternden
        Gesetzesbegründungen (sofern vorhanden).
        """
        query = """
        MATCH (p:Paragraph {paragraph_nr: $nr, gesetz: $gesetz})
        OPTIONAL MATCH (u:Urteil)-[:ZITIERT]->(p)
        OPTIONAL MATCH (b:Begruendung)-[:ERLAEUTERT]->(p)
        RETURN p AS paragraph,
               collect(DISTINCT u) AS urteile,
               collect(DISTINCT b) AS begruendungen
        """
        return self.run_query(query, {"nr": paragraph_nr, "gesetz": gesetz})

# Example usage:
# neo4j_service = Neo4jService()
# if neo4j_service.connect():
#     if neo4j_service.health_check():
#         print("Neo4j connection is healthy")
#     neo4j_service.close()