import os
import sys
import pytest

sys.path.insert(0, "/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality")

JUDGE_PATH = "/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/llm_judge.py"
HAS_API_KEY = bool(os.getenv("OPENAI_API_KEY") or os.getenv("ANTHROPIC_API_KEY"))


@pytest.mark.skipif(not os.path.exists(JUDGE_PATH), reason="llm_judge.py nicht vorhanden")
class TestLLMJudge56:
    def test_import(self):
        from llm_judge import judge_answer
        assert callable(judge_answer)

    @pytest.mark.skipif(not HAS_API_KEY, reason="Kein API-Key (OPENAI_API_KEY/ANTHROPIC_API_KEY) gesetzt")
    def test_judge_answer_struktur(self):
        from llm_judge import judge_answer
        result = judge_answer(
            frage="Darf der Vermieter einfach die Miete erhoehen?",
            antwort="Der Vermieter kann die Miete unter bestimmten Voraussetzungen erhoehen.",
            erwartete_paragraphen=["§558 BGB", "§559 BGB"]
        )
        assert isinstance(result, dict), "judge_answer muss dict zurueckgeben"
        assert "score" in result, "Kein score im Ergebnis"
        assert 1 <= result["score"] <= 5, f"Score {result['score']} ausserhalb 1-5"
        assert "begruendung" in result and result["begruendung"], "Keine Begruendung"
        assert "fehlende_aspekte" in result, "Kein fehlende_aspekte im Ergebnis"
        assert isinstance(result["fehlende_aspekte"], list), "fehlende_aspekte muss Liste sein"

    @pytest.mark.skipif(not HAS_API_KEY, reason="Kein API-Key gesetzt")
    def test_fehlende_paragraphen_erkannt(self):
        from llm_judge import judge_answer
        result = judge_answer(
            frage="Wann greift der Kuendigungsschutz?",
            antwort="Der Arbeitgeber kann jederzeit kuendigen.",
            erwartete_paragraphen=["§1 KSchG", "§622 BGB"]
        )
        assert isinstance(result["fehlende_aspekte"], list)
        assert len(result["fehlende_aspekte"]) > 0, "Fehlende §§ wurden nicht erkannt"
