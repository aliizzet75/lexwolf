import os
from neo4j import GraphDatabase
import logging

# Set up logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

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

# Example usage:
# neo4j_service = Neo4jService()
# if neo4j_service.connect():
#     if neo4j_service.health_check():
#         print("Neo4j connection is healthy")
#     neo4j_service.close()