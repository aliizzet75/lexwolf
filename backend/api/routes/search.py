import asyncio
import json
from typing import AsyncGenerator, Literal, Optional

from fastapi import APIRouter
from fastapi.responses import StreamingResponse
from services.react_engine import ReActEngine

router = APIRouter(tags=["search"])


def _sse(step: str, text: str) -> str:
    payload = json.dumps({"step": step, "text": text}, ensure_ascii=False)
    return f"data: {payload}\n\n"


async def _stream_search(query: str) -> AsyncGenerator[str, None]:
    yield _sse("suche_start", f"Suche nach: {query}")

    engine = ReActEngine()
    accumulated_chunks = []
    queries = [query]
    round_num = 0

    from services.search_service import HybridSearchService
    from services import react_engine as _m

    for round_num in range(1, engine.MAX_ROUNDS + 1):
        yield _sse("suche_start", f"Runde {round_num}: {queries}")

        search_service = HybridSearchService.__new__(HybridSearchService)
        parallel_search_func = getattr(HybridSearchService, "parallel_search")

        async def _call(q_list):
            try:
                return await parallel_search_func(search_service, q_list)
            except TypeError as e:
                if "takes 1 positional argument but 2 were given" in str(e):
                    return await parallel_search_func(q_list)
                raise

        results_per_query = await _call(queries)
        for result_list in results_per_query:
            accumulated_chunks.extend(result_list)

        for chunk in accumulated_chunks:
            text = chunk.get("text", str(chunk)) if isinstance(chunk, dict) else str(chunk)
            src = chunk.get("source", "") if isinstance(chunk, dict) else ""
            yield _sse("suchergebnis", f"{src}: {text[:200]}" if src else text[:200])

        text_chunks = [
            c.get("text", str(c)) if isinstance(c, dict) else str(c)
            for c in accumulated_chunks
        ]
        completeness = _m.evaluate_completeness(query, text_chunks)
        vollstaendig = completeness.get("vollstaendig", False)
        fehlende = completeness.get("fehlende_aspekte", [])

        yield _sse("bewertung", f"vollstaendig={vollstaendig}, fehlende={fehlende}")

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
    engine = ReActEngine()
    if mode == "schnell":
        engine.max_rounds = 1
        engine.use_graph = False
    else:
        engine.max_rounds = 5
        engine.use_graph = True

    return StreamingResponse(
        _stream_search(q),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",
        },
    )
