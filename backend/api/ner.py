from fastapi import APIRouter
from pydantic import BaseModel
from typing import List
import spacy

router = APIRouter(prefix="/ner", tags=["ner"])

_nlp = None

def _get_nlp():
    global _nlp
    if _nlp is None:
        _nlp = spacy.load("de_core_news_lg")
    return _nlp


class NerRequest(BaseModel):
    text: str


class NerEntity(BaseModel):
    text: str
    label: str
    start: int
    end: int


class NerResponse(BaseModel):
    entities: List[NerEntity]


def _clean_name(text: str) -> str:
    """Trim at first newline (spaCy sometimes groups name+address as one entity)."""
    if "\n" in text:
        text = text[: text.index("\n")]
    return text.strip()


@router.post("", response_model=NerResponse)
async def extract_entities(req: NerRequest):
    nlp = _get_nlp()
    doc = nlp(req.text)
    seen: set[str] = set()
    entities: list[NerEntity] = []
    for ent in doc.ents:
        if ent.label_ != "PER":
            continue
        name = _clean_name(ent.text)
        if len(name) < 2 or name in seen:
            continue
        seen.add(name)
        entities.append(NerEntity(text=name, label="PER", start=ent.start_char, end=ent.start_char + len(name)))
    return NerResponse(entities=entities)
