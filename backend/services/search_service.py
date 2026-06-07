import asyncio
from typing import List, Dict
from services.embedding_service import EmbeddingService
from services.database_service import DatabaseService
from services.neo4j_service import Neo4jService
from services.query_router import route_query
from sqlalchemy import text
import os
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

class HybridSearchService:
    """
    Hybrid search service combining dense vector search and sparse keyword search
    """
    
    def __init__(self):
        self.embedding_service = EmbeddingService()
        self.database_service = DatabaseService()

    def _call_ollama(self, prompt: str, max_tokens: int = 200) -> str:
        """Call Ollama LLM für HyDE — nutzt deepseek-v3.2:cloud (schnell, ~3s).
        Fallback auf HYDE_MODEL env var falls gesetzt."""
        import urllib.request as _urlreq
        import json as _json
        model = os.environ.get('HYDE_MODEL', 'deepseek-v3.2:cloud')
        # Try configured URL first, then localhost fallback
        urls_to_try = []
        configured = os.environ.get('OLLAMA_URL', '')
        if configured:
            urls_to_try.append(configured)
        if 'localhost' not in configured:
            urls_to_try.append('http://localhost:11434')
        payload = _json.dumps({
            'model': model,
            'messages': [{'role': 'user', 'content': prompt}],
            'stream': False,
            'max_tokens': max_tokens,
            'temperature': 0.1,
        }).encode()
        for url_base in urls_to_try:
            try:
                req = _urlreq.Request(
                    f'{url_base}/v1/chat/completions',
                    data=payload,
                    headers={'Content-Type': 'application/json'},
                )
                with _urlreq.urlopen(req, timeout=20) as resp:
                    data = _json.loads(resp.read())
                msg = data.get('choices', [{}])[0].get('message', {})
                content = msg.get('content', '')
                # kimi thinking models may have empty content but answer in reasoning
                if not content:
                    content = msg.get('reasoning', '')
                if content:
                    return content
            except Exception as e:
                logger.debug(f"Ollama call failed at {url_base}: {e}")
        return ''

    def hyde_embed(self, query: str) -> list:
        """HyDE: Kimi via Ollama generiert hypothetische juristische Antwort → besseres Embedding.
        Fallback auf direkte Query-Embedding bei Fehler."""
        try:
            hyde_prompt = (
                f"Du bist ein deutsches Rechtslexikon. Beantworte diese Mandantenfrage mit einem "
                f"juristischen Fachtext (ca. 80 Wörter). Nenne konkrete Paragraphen und Gesetze.\n\n"
                f"Frage: {query}\n\nJuristische Antwort:"
            )
            hypo_text = self._call_ollama(hyde_prompt, max_tokens=600)
            if hypo_text and len(hypo_text) > 20:
                logger.info(f"HyDE generated {len(hypo_text)} chars, embedding hypothetical answer")
                return self.embedding_service.generate_embedding(hypo_text)
            else:
                logger.warning("HyDE returned empty content, falling back to direct query embedding")
        except Exception as e:
            logger.warning(f"HyDE failed: {e}")
        return self.embedding_service.generate_embedding(query)

    def _rewrite_query_for_legal_search(self, query: str) -> str:
        """Rewrite colloquial client query to precise legal terminology via Kimi."""
        try:
            rewrite_prompt = (
                f"Übersetze diese Mandantenfrage in präzise juristische Suchbegriffe "
                f"(max 15 Wörter, nur Substantive und Rechtsgebiete):\n\n"
                f"Frage: {query}\n\nJuristische Suchbegriffe:"
            )
            rewritten = self._call_ollama(rewrite_prompt, max_tokens=200).strip()
            if rewritten and len(rewritten) > 5:
                logger.info(f"Query rewritten to: {rewritten[:100]}")
                return rewritten
        except Exception as e:
            logger.debug(f"Query rewrite failed: {e}")
        return query

    def hybrid_search_with_graph(self, query: str, limit: int = 10, fast_mode: bool = False) -> List[Dict]:
        """
        Perform hybrid search with optional graph traversal based on query complexity.
        Simple queries use only pgvector; complex queries add Neo4j graph traversal.
        """
        query_type = route_query(query)
        logger.info(f"Query routed as: {query_type}")

        base_results = self.search(query, limit, fast_mode=fast_mode)

        if query_type != 'complex':
            return base_results

        # For complex queries: enrich with Neo4j graph-connected paragraphs
        graph_results = self._fetch_graph_neighbors(base_results)

        # Merge and deduplicate by id
        seen_ids = set()
        merged = []
        for result in base_results + graph_results:
            rid = result.get("id")
            if rid not in seen_ids:
                seen_ids.add(rid)
                merged.append(result)

        return merged[:limit * 2]

    def _fetch_graph_neighbors(self, base_results: List[Dict]) -> List[Dict]:
        """Fetch Neo4j-connected paragraphs for the given base results."""
        # PostgreSQL IDs → Neo4j node IDs (format: "chunk_<id>")
        neo4j_ids = [f"chunk_{r['id']}" for r in base_results if "id" in r]
        if not neo4j_ids:
            return []

        neo4j_svc = Neo4jService()
        if not neo4j_svc.connect():
            logger.warning("Could not connect to Neo4j; skipping graph traversal")
            return []

        try:
            cypher = """
                MATCH (p:Paragraph)-[:VERWEIST_AUF]->(q:Paragraph)
                WHERE p.id IN $ids AND q.id IS NOT NULL AND NOT q.id IN $ids
                RETURN DISTINCT q
                LIMIT 20
            """
            records = neo4j_svc.run_query(cypher, {"ids": neo4j_ids})
            results = []
            for record in records:
                node = record.get("q", {})
                if not node:
                    continue
                results.append({
                    "id": node.get("id"),
                    "text": node.get("text", ""),
                    "title": node.get("title", ""),
                    "legal_field": node.get("legal_field"),
                    "court": node.get("court"),
                    "case_number": node.get("case_number"),
                    "date": node.get("created_at"),
                    "tags": node.get("tags"),
                    "chunk_hash": node.get("chunk_hash"),
                    "source": "graph",
                })
            logger.info(f"Graph traversal returned {len(results)} additional results")
            return results
        except Exception as e:
            logger.error(f"Error in graph traversal: {e}")
            return []
        finally:
            neo4j_svc.close()

    # Mapping from law abbreviations in Kimi output to DB tag values
    _LAW_TAG_MAP = {
        'bgb': 'bgb', 'bürgerliches': 'bgb',
        'stgb': 'stgb', 'strafgesetzbuch': 'stgb',
        'kschg': 'kschg', 'kündigungsschutz': 'kschg',
        'zpo': 'zpo', 'zivilprozess': 'zpo',
        'stpo': 'stpo', 'strafprozess': 'stpo',
        'hgb': 'hgb', 'handelsgesetzbuch': 'hgb',
        'stvg': 'stvg', 'straßenverkehrsgesetz': 'stvg',
        'betrvg': 'betrvg', 'betriebsverfassung': 'betrvg',
        'inso': 'inso', 'insolvenzordnung': 'inso',
        'gewo': 'gewo', 'gewerbeordnung': 'gewo',
        'sgb': 'sgb_5',
        'ao': 'ao_1977', 'abgabenordnung': 'ao_1977',
        'aktg': 'aktg', 'aktiengesetz': 'aktg',
        'gmbhg': 'gmbhg',
        'urhg': 'urhg', 'urheberrechtsgesetz': 'urhg',
        'tkg': 'tkg_2021',
        'muschg': 'muschg_2018',
        'beeg': 'beeg',
        'tzbfg': 'tzbfg',
    }

    def _kimi_direct_lookup(self, query: str) -> List[Dict]:
        """Ask Kimi which §§ are relevant, then do direct DB lookup for those chunks.
        Returns list of chunks with the expected paragraphs, injected at top of results."""
        import re as _re
        try:
            prompt = (
                "Du bist ein deutsches Rechtslexikon. Beantworte diese Mandantenfrage mit einem "
                "juristischen Fachtext (80-120 Wörter). Nenne die wichtigsten Paragraphen im Format "
                "'§Nummer GESETZ' (z.B. §823 BGB, §7 StVG, §1 KSchG).\n\n"
                f"Frage: {query}\n\nAntwort:"
            )
            kimi_response = self._call_ollama(prompt, max_tokens=2000)
            if not kimi_response:
                return []

            # Extract all § number + optional law abbreviation pairs
            # Pattern: §<nr> optionally followed by law name
            para_with_law = _re.findall(
                r'§\s*(\d+[a-zA-Z]?)\s+([A-ZÜÖÄ][A-Za-züöäÜÖÄß]{1,15})',
                kimi_response
            )
            para_no_law = _re.findall(r'§\s*(\d+[a-zA-Z]?)', kimi_response)

            # Build list of (para_nr, tag) pairs
            seen_nrs = set()
            lookup_targets = []
            for nr, law in para_with_law:
                law_tag = self._LAW_TAG_MAP.get(law.lower())
                if nr not in seen_nrs:
                    seen_nrs.add(nr)
                    lookup_targets.append((nr, law_tag))
            for nr in para_no_law:
                if nr not in seen_nrs:
                    seen_nrs.add(nr)
                    lookup_targets.append((nr, None))

            if not lookup_targets:
                return []

            logger.info(f"Kimi direct lookup targets: {lookup_targets[:8]}")
            db = self.database_service.SessionLocal()
            direct_chunks = []
            seen_ids = set()

            for para_nr, tag in lookup_targets[:8]:
                try:
                    if tag:
                        q = text("""
                            SELECT * FROM legal_chunks
                            WHERE title ILIKE :pattern AND tags = :tag
                            LIMIT 2
                        """)
                        rows = db.execute(q, {"pattern": f"§ {para_nr}%", "tag": tag}).fetchall()
                    else:
                        q = text("""
                            SELECT * FROM legal_chunks
                            WHERE title ILIKE :pattern
                            LIMIT 2
                        """)
                        rows = db.execute(q, {"pattern": f"§ {para_nr}%"}).fetchall()

                    for row in rows:
                        if row.id not in seen_ids:
                            seen_ids.add(row.id)
                            direct_chunks.append({
                                "id": row.id,
                                "document_id": row.document_id,
                                "text": row.text,
                                "title": row.title,
                                "court": row.court,
                                "case_number": row.case_number,
                                "date": row.date.isoformat() if row.date else None,
                                "legal_field": row.legal_field,
                                "tags": row.tags,
                                "chunk_hash": row.chunk_hash,
                                "parent_id": row.parent_id,
                                "is_parent": row.is_parent,
                                "dense_rank": 1,
                                "direct_hit": True,
                                "score": 999.0,
                            })
                except Exception as e_inner:
                    logger.debug(f"Direct lookup failed for §{para_nr}: {e_inner}")

            db.close()
            logger.info(f"Kimi direct lookup returned {len(direct_chunks)} chunks")
            return direct_chunks

        except Exception as e:
            logger.warning(f"_kimi_direct_lookup failed: {e}")
            return []

    def search(self, query: str, limit: int = 10, fast_mode: bool = False) -> List[Dict]:
        """
        Perform hybrid search combining dense vector search, sparse keyword search, and tag-boosted search.
        fast_mode=True: überspringt alle LLM-Calls (kein HyDE, kein Kimi-Lookup) für <5s Antwortzeit.
        fast_mode=False (default): vollständige Pipeline mit HyDE + Kimi-Lookup für maximale Qualität.
        """
        try:
            logger.info(f"Performing hybrid search for query: {query} (fast_mode={fast_mode})")

            # Kimi direct lookup — nur im vollständigen Modus
            direct_chunks = [] if fast_mode else self._kimi_direct_lookup(query)

            # Embedding: HyDE im vollen Modus, direktes Query-Embedding im Fast-Modus
            if fast_mode:
                query_embedding = self.embedding_service.generate_embedding(query)
                logger.info("Fast-mode: direktes Query-Embedding (kein HyDE)")
            else:
                query_embedding = self.hyde_embed(query)
                logger.info("Generated HyDE query embedding")

            dense_results = self._dense_search(query_embedding, limit * 2)
            logger.info(f"Dense search returned {len(dense_results)} results")

            sparse_results = self._sparse_search(query, limit * 2)
            logger.info(f"Sparse search returned {len(sparse_results)} results")

            # Tag-Boosted Search: extrahiere Gesetzes-Tags aus Query und suche danach
            tag_boost_results = self._tag_boost_search(query, query_embedding, limit)
            logger.info(f"Tag-boost search returned {len(tag_boost_results)} results")

            # Merge all results and apply RRF
            all_sparse = sparse_results + tag_boost_results
            fused_results = self.reciprocal_rank_fusion(dense_results, all_sparse)

            # Prepend direct hits (from Kimi) before fused results
            direct_ids = {c.get("id") for c in direct_chunks}
            merged = direct_chunks + [r for r in fused_results if r.get("id") not in direct_ids]
            logger.info(f"Final merged: {len(direct_chunks)} direct + {len(fused_results)} fused = {len(merged)}")

            return merged[:limit]

        except Exception as e:
            logger.error(f"Error in hybrid search: {e}")
            return self.database_service.search_chunks_hybrid(query, limit)
    
    def _dense_search(self, query_vector: List[float], limit: int = 10) -> List[Dict]:
        """
        Perform dense vector search using pgvector cosine similarity with <-> operator
        """
        try:
            logger.info(f"Performing dense search with pgvector cosine similarity")
            
            db = self.database_service.SessionLocal()
            
            query = text("""
                SELECT *, vector <-> CAST(:vec AS vector) AS score
                FROM legal_chunks
                ORDER BY score
                LIMIT :k
            """)

            result = db.execute(query, {"vec": str(query_vector), "k": limit})
            rows = result.fetchall()
            
            results = []
            for i, row in enumerate(rows):
                result_dict = {
                    "id": row.id,
                    "document_id": row.document_id,
                    "text": row.text,
                    "title": row.title,
                    "court": row.court,
                    "case_number": row.case_number,
                    "date": row.date.isoformat() if row.date else None,
                    "legal_field": row.legal_field,
                    "tags": row.tags,
                    "chunk_hash": row.chunk_hash,
                    "parent_id": row.parent_id,
                    "is_parent": row.is_parent,
                    "dense_score": float(row.score),
                    "dense_rank": i + 1,
                    "created_at": row.created_at.isoformat() if row.created_at else None
                }
                results.append(result_dict)
            
            logger.info(f"Dense search completed with {len(results)} results")
            return results
            
        except Exception as e:
            logger.error(f"Error in dense search: {e}")
            return []
        finally:
            db.close()
    
    def _sparse_search(self, query: str, limit: int = 10) -> List[Dict]:
        """
        Perform sparse keyword search using PostgreSQL Full-Text Search
        with websearch_to_tsquery and OR-fallback for compound terms.
        """
        try:
            logger.info(f"Performing sparse search with PostgreSQL FTS for query: {query}")
            
            db = self.database_service.SessionLocal()
            
            # Step 1: Try websearch_to_tsquery with original query
            query_sql = text("""
                SELECT *, 
                       ts_rank(ts_vector, websearch_to_tsquery('german', :query)) AS sparse_score
                FROM legal_chunks 
                WHERE ts_vector @@ websearch_to_tsquery('german', :query)
                ORDER BY ts_rank(ts_vector, websearch_to_tsquery('german', :query)) DESC
                LIMIT :k
            """)
            
            result = db.execute(query_sql, {"query": query, "k": limit})
            rows = result.fetchall()
            
            if rows:
                logger.info(f"Sparse search (websearch_to_tsquery) returned {len(rows)} results")
            else:
                logger.info(f"Sparse search (websearch_to_tsquery) returned 0 results — trying OR-fallback")
                # Step 2: OR-fallback — split query into words and combine with OR
                words = query.split()
                if len(words) > 1:
                    or_query = " OR ".join(words)  # websearch_to_tsquery: OR keyword for union
                    or_query_sql = text("""
                        SELECT *, 
                               ts_rank(ts_vector, websearch_to_tsquery('german', :query)) AS sparse_score
                        FROM legal_chunks 
                        WHERE ts_vector @@ websearch_to_tsquery('german', :query)
                        ORDER BY ts_rank(ts_vector, websearch_to_tsquery('german', :query)) DESC
                        LIMIT :k
                    """)
                    result = db.execute(or_query_sql, {"query": or_query, "k": limit})
                    rows = result.fetchall()
                    logger.info(f"OR-fallback returned {len(rows)} results")
            
            results = []
            for i, row in enumerate(rows):
                result_dict = {
                    "id": row.id,
                    "document_id": row.document_id,
                    "text": row.text,
                    "title": row.title,
                    "court": row.court,
                    "case_number": row.case_number,
                    "date": row.date.isoformat() if row.date else None,
                    "legal_field": row.legal_field,
                    "tags": row.tags,
                    "chunk_hash": row.chunk_hash,
                    "parent_id": row.parent_id,
                    "is_parent": row.is_parent,
                    "sparse_score": float(row.sparse_score),
                    "sparse_rank": i + 1,
                    "created_at": row.created_at.isoformat() if row.created_at else None
                }
                results.append(result_dict)
            
            logger.info(f"Sparse search completed with {len(results)} results")
            return results
            
        except Exception as e:
            logger.error(f"Error in sparse search: {e}")
            return []
        finally:
            db.close()
    
    def _tag_boost_search(self, query: str, query_vector: List[float], limit: int = 5) -> List[Dict]:
        """
        Tag-Boosted Search: Extrahiere Gesetzes-Tags aus Query (z.B. KSchG, BGB) 
        und führe direkte Tag-Suche mit RRF-Boost durch.
        
        Erkennt Tags wie kschg, bgb, tzfbg, burlg, stgb, gwpo, lvwg, vg, ... via Regex.
        Tag-Treffer erhalten dense_rank=1 für RRF-Boost.
        """
        import re
        
        # Registrierte Gesetzes-Tags (Kurzbezeichnungen für SQL tags-Column)
        TAGS = {
            'kschg': 'KSchG',
            'bgb': 'BGB',
            'tzfbg': 'TZfG',
            'burlg': 'BUrlG',
            'stgb': 'StGB',
            'gwpo': 'GWpo',
            'lvwg': 'LVWG',
            'vg': 'VG',
            'ao': 'AO',
            'stPO': 'StPO',
            'kStG': 'KStG',
            'eStG': 'EStG',
            'aG': 'AG',
            'gG': 'gG',
            'gGK': 'gGK',
            'SGB I': 'SGB I',
            'SGB II': 'SGB II',
            'SGB III': 'SGB III',
            'SGB IV': 'SGB IV',
            'SGB V': 'SGB V',
            'SGB VIII': 'SGB VIII',
            'SGB IX': 'SGB IX',
            'SGB X': 'SGB X',
            'SGB XI': 'SGB XI',
            'SGB XII': 'SGB XII',
            'SGB XIII': 'SGB XIII',
            'SGB XIV': 'SGB XIV',
        }
        
        # Finde alle vorkommenden Gesetzes-Abkürzungen im Query (Regex für §\d+ [A-Z][a-z]+)
        # Z.B. "§23 KSchG" >> "kschg"
        tag_pattern = r'§\d+\s*([A-Za-z]{2,5})'
        found_tags = set()
        
        for match in re.finditer(tag_pattern, query, re.IGNORECASE):
            tag_abbr = match.group(1).lower()
            if tag_abbr in TAGS:
                found_tags.add(tag_abbr)
        
        # Fallback: Suche auch nach Gesetzesnamen
        for tag_key, tag_val in TAGS.items():
            if tag_val.lower() in query.lower():
                found_tags.add(tag_key)
        
        # SECONDARY: Extrahiere Rechtsgebiet aus Query-Text basierend auf Stichworten
        # (wenn Regex-basierte Suche kein Ergebnis liefert)
        if not found_tags:
            try:
                from services.search_service_keywords import extract_legal_fields
                keyword_tags = extract_legal_fields(query)
                if keyword_tags:
                    found_tags.update(keyword_tags)
                    logger.info(f"Rechtsgebietserkennung via Keywords: {found_tags}")
            except Exception as e:
                logger.debug(f"Keyword extraction fehlgeschlagen: {e}")
        
        if not found_tags:
            logger.info("Keine Gesetzes-Tags im Query erkannt - Überspringe Tag-Boost")
            return []
        
        logger.info(f"Erkannte Gesetzes-Tags: {found_tags}")
        
        try:
            db = self.database_service.SessionLocal()
            results = []
            
            for tag in found_tags:
                # Tag-basierte Vektorsuche mit Boost (dense_rank=1)
                query_sql = text("""
                    SELECT *, 
                           vector <-> CAST(:vec AS vector) AS score
                    FROM legal_chunks 
                    WHERE tags = :tag
                    ORDER BY score
                    LIMIT :k
                """)
                
                result = db.execute(query_sql, {"vec": str(query_vector), "tag": tag, "k": limit})
                rows = result.fetchall()
                
                for i, row in enumerate(rows):
                    result_dict = {
                        "id": row.id,
                        "document_id": row.document_id,
                        "text": row.text,
                        "title": row.title,
                        "court": row.court,
                        "case_number": row.case_number,
                        "date": row.date.isoformat() if row.date else None,
                        "legal_field": row.legal_field,
                        "tags": row.tags,
                        "chunk_hash": row.chunk_hash,
                        "parent_id": row.parent_id,
                        "is_parent": row.is_parent,
                        "dense_score": float(row.score),
                        "dense_rank": 1,  # Boost: Tag-Treffer immer auf Platz 1
                        "created_at": row.created_at.isoformat() if row.created_at else None
                    }
                    results.append(result_dict)
            
            logger.info(f"Tag-boost search found {len(results)} results for tags {found_tags}")
            return results
            
        except Exception as e:
            logger.error(f"Error in tag-boost search: {e}")
            return []
        finally:
            db.close()
    
    def reciprocal_rank_fusion(self, dense: list, sparse: list, k: int = 60) -> list[dict]:
        """Combine dense and sparse results using RRF. Formula: score = sum(1/(k+rank))."""
        fused_scores = {}
        result_lookup = {}

        for i, result in enumerate(dense):
            chunk_id = result.get("id", hash(result.get("text", str(i))))
            rank = result.get("dense_rank", i + 1)
            fused_scores[chunk_id] = 1.0 / (k + rank)
            result_lookup[chunk_id] = result

        for i, result in enumerate(sparse):
            chunk_id = result.get("id", hash(result.get("text", str(i))))
            rank = result.get("sparse_rank", i + 1)
            rrf_score = 1.0 / (k + rank)
            if chunk_id in fused_scores:
                fused_scores[chunk_id] += rrf_score
            else:
                fused_scores[chunk_id] = rrf_score
                result_lookup[chunk_id] = result

        sorted_results = sorted(fused_scores.items(), key=lambda x: x[1], reverse=True)
        output = []
        for chunk_id, score in sorted_results:
            if chunk_id in result_lookup:
                result = result_lookup[chunk_id].copy()
                result["score"] = score
                output.append(result)
        return output

    def _reciprocal_rank_fusion(self, dense_results: List[Dict], sparse_results: List[Dict], limit: int = 10) -> List[Dict]:
        """
        Combine dense and sparse search results using Reciprocal Rank Fusion (RRF)
        """
        try:
            fused_scores = {}
            result_lookup = {}
            
            for result in dense_results:
                chunk_id = result.get("id", hash(result.get("text", "")))
                dense_rank = result.get("dense_rank", 1)
                fused_scores[chunk_id] = 1 / (dense_rank + 60)
                result_lookup[chunk_id] = result
            
            for result in sparse_results:
                chunk_id = result.get("id", hash(result.get("text", "")))
                sparse_rank = result.get("sparse_rank", 1)
                if chunk_id in fused_scores:
                    fused_scores[chunk_id] += 1 / (sparse_rank + 60)
                else:
                    fused_scores[chunk_id] = 1 / (sparse_rank + 60)
                    result_lookup[chunk_id] = result
            
            sorted_results = sorted(fused_scores.items(), key=lambda x: x[1], reverse=True)
            
            top_results = []
            for chunk_id, score in sorted_results[:limit]:
                if chunk_id in result_lookup:
                    result = result_lookup[chunk_id].copy()
                    result["score"] = score
                    if "legal_field" not in result:
                        result["legal_field"] = None
                    if "court" not in result:
                        result["court"] = None
                    if "case_number" not in result:
                        result["case_number"] = None
                    if "date" not in result:
                        result["date"] = None
                    top_results.append(result)
            
            return top_results
        except Exception as e:
            logger.error(f"Error in RRF fusion: {e}")
            return []

    async def parallel_search(self, queries: List[str]) -> List[List[Dict]]:
        """
        Execute multiple search queries in parallel using asyncio.gather().
        
        Args:
            queries: List of query strings to search for
            
        Returns:
            List of lists containing search results for each query, with duplicates
            deduplicated by chunk_id across all results, keeping the highest relevance score.
        """
        # Create async tasks for each query
        tasks = [asyncio.create_task(self._async_search(query)) for query in queries]
        
        # Execute all searches in parallel
        results = await asyncio.gather(*tasks)
        
        # Global deduplication across all results by chunk_id, keeping highest score
        global_chunk_scores = {}
        
        # First pass: collect all items and track highest scores
        for result_list in results:
            for item in result_list:
                chunk_id = item.get("chunk_id", item.get("id", hash(str(item))))
                score = item.get("score", item.get("dense_score", item.get("sparse_score", 0)))
                
                # If we haven't seen this chunk_id or this score is higher, keep it
                if chunk_id not in global_chunk_scores or score > global_chunk_scores[chunk_id]["score"]:
                    global_chunk_scores[chunk_id] = {"item": item, "score": score}
        
        # Second pass: rebuild results with deduplicated items
        deduplicated_results = []
        for result_list in results:
            deduped_list = []
            local_chunk_ids = set()  # Track chunk_ids in this specific result list
            
            for item in result_list:
                chunk_id = item.get("chunk_id", item.get("id", hash(str(item))))
                
                # Only include this item if it's the highest scoring version globally
                if chunk_id in global_chunk_scores and chunk_id not in local_chunk_ids:
                    # Check if this is the highest scoring version
                    if item == global_chunk_scores[chunk_id]["item"]:
                        deduped_list.append(item)
                        local_chunk_ids.add(chunk_id)
            
            deduplicated_results.append(deduped_list)
        
        return deduplicated_results
    
    async def _async_search(self, query: str) -> List[Dict]:
        """
        Async wrapper for the search method.
        """
        # In a real implementation, this would be truly async
        # For now, we'll run the synchronous search in a thread pool
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, self.search, query)

    def rerank_results(self, query: str, chunks: list[dict], top_k: int = 5) -> list[dict]:
        """Rerank chunks using Claude — only for Tief-Modus. Filters out score < 7."""
        import anthropic
        import re
        api_key = os.environ.get('CLAUDE_API_KEY') or os.environ.get('ANTHROPIC_API_KEY')
        client = anthropic.Anthropic(api_key=api_key)
        scored = []
        for chunk in chunks:
            text = chunk.get('text', '')
            try:
                message = client.messages.create(
                    model='claude-3-haiku-20240307',
                    max_tokens=50,
                    messages=[{
                        'role': 'user',
                        'content': f"Bewertet diesen Gesetzestext auf Relevanz für: '{query}'. Antwort: RELEVANZ: [1-10]\n\n{text}"
                    }]
                )
                response_text = message.content[0].text
                match = re.search(r'RELEVANZ:\s*(\d+)', response_text)
                score = int(match.group(1)) if match else 0
            except Exception as e:
                logger.warning(f'Reranking failed for chunk: {e}')
                score = 0
            if score >= 7:
                scored.append({**chunk, 'rerank_score': score})
        scored.sort(key=lambda x: x.get('rerank_score', 0), reverse=True)
        return scored[:top_k]
