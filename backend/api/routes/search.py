import asyncio
import json
from typing import AsyncGenerator, Literal, Optional

from fastapi import APIRouter
from fastapi.responses import StreamingResponse
from services.react_engine import CONFIDENCE_THRESHOLD, ReActEngine

router = APIRouter(tags=["search"])


def _sse(step: str, text: str, **extra) -> str:
    payload = json.dumps({"step": step, "text": text, **extra}, ensure_ascii=False)
    return f"data: {payload}\n\n"


def _quelle(chunk: dict) -> str:
    """Kurzes Label für die durchsuchte Quelle, fürs Live-Panel im Client (T#295)."""
    if chunk.get("source") == "graph":
        return "Zitier-Graph (Neo4j)"
    if chunk.get("court"):
        return f"Rechtsprechung: {chunk['court']}"
    if chunk.get("legal_field"):
        return f"Gesetzestext: {chunk['legal_field']}"
    return chunk.get("title") or "Datenbank"


async def _stream_search(query: str, mode: str = "schnell") -> AsyncGenerator[str, None]:
    yield _sse("suche_start", f"Suche nach: {query}")

    max_rounds = 1 if mode == "schnell" else ReActEngine.MAX_ROUNDS
    accumulated_chunks = []
    queries = [query]
    round_num = 0
    emitted_count = 0  # Index des zuletzt als SSE-Event ausgegebenen Chunks —
    # ohne das würden ab Runde 2 alle bisherigen Treffer erneut gesendet

    from services.search_service import HybridSearchService
    from services import react_engine as _m

    for round_num in range(1, max_rounds + 1):
        yield _sse("suche_start", f"Runde {round_num}: {queries}")

        # Echte Instanz (nicht __new__) — search()/parallel_search() braucht
        # self.embedding_service/self.database_service, sonst AttributeError
        # bei jeder echten Suche (siehe T#295-Testlauf gegen echte DB).
        search_service = HybridSearchService()
        results_per_query = await search_service.parallel_search(queries)
        for result_list in results_per_query:
            accumulated_chunks.extend(result_list)

        for chunk in accumulated_chunks[emitted_count:]:
            is_dict = isinstance(chunk, dict)
            text = chunk.get("text", str(chunk)) if is_dict else str(chunk)
            confidence = round(min(float(chunk.get("score", 0.0)), 1.0), 4) if is_dict else 0.0
            status = "gefunden" if confidence >= CONFIDENCE_THRESHOLD else "verworfen"
            quelle = _quelle(chunk) if is_dict else "Datenbank"
            yield _sse(
                "suchergebnis",
                text[:200],
                quelle=quelle,
                confidence=confidence,
                status=status,
            )
        emitted_count = len(accumulated_chunks)

        text_chunks = [
            c.get("text", str(c)) if isinstance(c, dict) else str(c)
            for c in accumulated_chunks
        ]
        completeness = _m.evaluate_completeness(query, text_chunks)
        vollstaendig = completeness.get("vollstaendig", False)
        fehlende = completeness.get("fehlende_aspekte", [])

        scores = [float(c.get("score", 0.0)) for c in accumulated_chunks if isinstance(c, dict)]
        round_confidence = round(min(sum(scores) / len(scores), 1.0), 4) if scores else 0.0

        yield _sse(
            "bewertung",
            f"vollstaendig={vollstaendig}, fehlende={fehlende}",
            confidence=round_confidence,
        )

        if vollstaendig:
            break
        if not fehlende:
            break
        queries = fehlende

    structured = _m.generate_structured_response(query, accumulated_chunks)
    aussagen = structured.get("aussagen", [])

    for aussage in aussagen:
        answer_text = aussage.get("text", "")
        chunk_size = max(1, len(answer_text) // 4) if answer_text else 1
        for i in range(0, max(len(answer_text), 1), chunk_size):
            yield _sse("antwort_chunk", answer_text[i : i + chunk_size])
            await asyncio.sleep(0)

    if not aussagen:
        yield _sse("antwort_chunk", "Keine Ergebnisse gefunden.")

    yield _sse("fertig", "Suche abgeschlossen")


@router.get("/search")
async def search(q: str, mode: Optional[Literal["schnell", "tief"]] = "schnell"):
    """Nicht-Streaming-Suche — gibt JSON zurück."""
    engine = ReActEngine()
    if mode == "schnell":
        engine.max_rounds = 1
        engine.use_graph = False
    else:
        engine.max_rounds = 5
        engine.use_graph = True
    result = await engine.run_loop(q)
    return {"q": q, "mode": mode, "aussagen": result.get("accumulated_chunks", [])}


@router.get("/api/search/stream")
async def stream_search(q: str, mode: Optional[Literal["schnell", "tief"]] = "schnell"):
    """Streaming-Suche via Server-Sent Events."""
    return StreamingResponse(
        _stream_search(q, mode or "schnell"),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",
        },
    )
