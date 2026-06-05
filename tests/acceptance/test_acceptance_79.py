import pytest
import sys
from pathlib import Path

DESKTOP = Path("/data/.openclaw/workspace-codex/projects/lexwolf/desktop")
sys.path.insert(0, str(DESKTOP))


def test_modul_existiert():
    assert (DESKTOP / "confidence_coloring.py").exists(), "confidence_coloring.py fehlt im desktop-Ordner"


def test_klassen_importierbar():
    from confidence_coloring import ConfidenceColorizer, TextSegment
    assert ConfidenceColorizer is not None
    assert TextSegment is not None


def test_farbe_gruen_bei_hoher_konfidenz():
    from confidence_coloring import ConfidenceColorizer
    assert ConfidenceColorizer.score_to_color(0.9) == "green"
    assert ConfidenceColorizer.score_to_color(0.81) == "green"


def test_farbe_gelb_bei_mittlerer_konfidenz():
    from confidence_coloring import ConfidenceColorizer
    assert ConfidenceColorizer.score_to_color(0.7) == "yellow"
    assert ConfidenceColorizer.score_to_color(0.5) == "yellow"


def test_farbe_rot_bei_niedriger_konfidenz():
    from confidence_coloring import ConfidenceColorizer
    assert ConfidenceColorizer.score_to_color(0.3) == "red"
    assert ConfidenceColorizer.score_to_color(0.0) == "red"


def test_segment_bekommt_farbe():
    from confidence_coloring import TextSegment
    seg = TextSegment(text="Der Beklagte", confidence_score=0.85, source="GPT-4")
    assert seg.color == "green"


def test_hover_tooltip_enthaelt_konfidenz_und_quelle():
    from confidence_coloring import ConfidenceColorizer, TextSegment
    seg = TextSegment(text="§ 823 BGB", confidence_score=0.6, source="Rechtsdatenbank")
    tooltip = ConfidenceColorizer.get_tooltip(seg)
    assert "60.0%" in tooltip or "60%" in tooltip, f"Konfidenz fehlt im Tooltip: {tooltip}"
    assert "Rechtsdatenbank" in tooltip, f"Quelle fehlt im Tooltip: {tooltip}"


def test_colorize_mehrere_segmente():
    from confidence_coloring import ConfidenceColorizer, TextSegment
    colorizer = ConfidenceColorizer()
    segments = [
        TextSegment("hoch", 0.9, "S1"),
        TextSegment("mittel", 0.65, "S2"),
        TextSegment("niedrig", 0.2, "S3"),
    ]
    result = colorizer.colorize(segments)
    assert result[0].color == "green"
    assert result[1].color == "yellow"
    assert result[2].color == "red"


def test_text_editierbar_moeglich():
    from confidence_coloring import TextSegment
    seg = TextSegment(text="Ursprungstext", confidence_score=0.7, source="AI")
    seg.text = "Geänderter Text durch Anwalt"
    assert seg.text == "Geänderter Text durch Anwalt"


def test_xaml_hat_richtextbox():
    xaml = (DESKTOP / "MainWindow.xaml").read_text()
    assert "RichTextBox" in xaml, "RichTextBox fehlt in MainWindow.xaml"
    assert 'Name="EditorBox"' in xaml, "EditorBox RichTextBox fehlt in MainWindow.xaml"
