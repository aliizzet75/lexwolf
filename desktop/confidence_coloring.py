"""
Konfidenz-Farbkodierung für den Rich-Text-Editor.
Segmente werden nach Konfidenz-Score eingefärbt:
  > 0.8  → grün   (sicher)
  0.5–0.8 → gelb   (unsicher)
  < 0.5  → rot    (niedrige Konfidenz)
"""
from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class TextSegment:
    text: str
    confidence_score: float
    source: str
    color: Optional[str] = field(default=None, init=False)

    def __post_init__(self):
        self.color = ConfidenceColorizer.score_to_color(self.confidence_score)


class ConfidenceColorizer:
    COLOR_GREEN = "green"
    COLOR_YELLOW = "yellow"
    COLOR_RED = "red"

    @staticmethod
    def score_to_color(score: float) -> str:
        if score > 0.8:
            return ConfidenceColorizer.COLOR_GREEN
        elif score >= 0.5:
            return ConfidenceColorizer.COLOR_YELLOW
        else:
            return ConfidenceColorizer.COLOR_RED

    @staticmethod
    def get_tooltip(segment: "TextSegment") -> str:
        pct = round(segment.confidence_score * 100, 1)
        return f"Konfidenz: {pct}% | Quelle: {segment.source}"

    def colorize(self, segments: List[TextSegment]) -> List[TextSegment]:
        for seg in segments:
            seg.color = self.score_to_color(seg.confidence_score)
        return segments
