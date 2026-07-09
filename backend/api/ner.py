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


def _is_whitespace_only(text: str) -> bool:
    """Check if text contains only whitespace characters."""
    return all(c.isspace() for c in text)


def _extract_gap_text(text: str, start: int, end: int) -> str:
    """Extract the substring between start and end positions."""
    return text[start:end]


def _merge_consecutive_person_entities(req_text: str, entities: list[NerEntity]) -> list[NerEntity]:
    """
    Merge consecutive PER entities if the gap between end[i] and start[i+1]
    is <= 2 characters and contains only whitespace.
    Example: 'Ali Izzet' (end=10) + 'Erolk' (start=11) -> 'Ali Izzet Erolk'
    """
    if len(entities) < 2:
        return entities

    merged: list[NerEntity] = []
    current = entities[0]

    for i in range(1, len(entities)):
        next_ent = entities[i]
        gap_start = current.end
        gap_end = next_ent.start
        gap_length = gap_end - gap_start

        # Extract the actual gap text from the original text
        if gap_length > 0:
            gap_text = _extract_gap_text(req_text, gap_start, gap_end)
        else:
            gap_text = ""

        # Check if gap is <= 2 chars and contains only whitespace
        if gap_length <= 2 and _is_whitespace_only(gap_text):
            # Merge: extend current entity to include next
            current = NerEntity(
                text=current.text + gap_text + next_ent.text,
                label="PER",
                start=current.start,
                end=next_ent.end,
            )
            continue

        # No merge - add current and start new
        merged.append(current)
        current = next_ent

    merged.append(current)
    return merged


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

    # Merge consecutive PER entities with gap <= 2 whitespace characters
    entities = _merge_consecutive_person_entities(req.text, entities)

    return NerResponse(entities=entities)
