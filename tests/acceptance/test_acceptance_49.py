import pytest, os, sys, socket, json
from unittest.mock import patch
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../../backend"))
CHUNKS = [{"id": "c1", "text": "Urlaub §1"}, {"id": "c2", "text": "24 Werktage §2"}]
MOCK_JSON = json.dumps({"aussagen": [{"text": "Urlaubsanspruch.", "quellen": ["c1"]}]})
def ollama_ok(): s=socket.socket(); r=s.connect_ex(("localhost",11434)); s.close(); return r==0
def test_generate_structured_response_exists():
    from services.react_engine import generate_structured_response
    assert callable(generate_structured_response)
def test_aussagen_schema_and_json_parsing():
    from services.react_engine import generate_structured_response
    with patch("services.react_engine.call_llm", return_value=MOCK_JSON):
        r = generate_structured_response("Urlaub?", CHUNKS)
    assert "aussagen" in r and isinstance(r["aussagen"], list) and len(r["aussagen"]) > 0
    for a in r["aussagen"]:
        assert "text" in a and "quellen" in a, f"Aussage unvollständig: {a}"
        assert len(a["quellen"]) >= 1, "Mind. 1 Quelle pro Aussage"
def test_quellen_ids_subset_of_chunk_ids():
    from services.react_engine import generate_structured_response
    with patch("services.react_engine.call_llm", return_value=MOCK_JSON):
        r = generate_structured_response("Urlaub?", CHUNKS)
    ids = {c["id"] for c in CHUNKS}
    for a in r["aussagen"]:
        for qid in a["quellen"]:
            assert qid in ids, f"quellen-ID '{qid}' nicht in übergebenen Chunks"
@pytest.mark.skipif(not ollama_ok(), reason="Ollama nicht erreichbar auf port 11434")
def test_live_structured_response():
    from services.react_engine import generate_structured_response
    r = generate_structured_response("Urlaubsanspruch?", CHUNKS)
    assert "aussagen" in r and isinstance(r["aussagen"], list)
