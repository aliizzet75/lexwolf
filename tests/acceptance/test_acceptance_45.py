import pytest
import os
import sys
import socket

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../../backend'))


def ollama_ok():
    s = socket.socket()
    r = s.connect_ex(('ollama', 11434))
    s.close()
    return r == 0


def test_evaluate_completeness_exists():
    from services.react_engine import evaluate_completeness
    assert callable(evaluate_completeness)


def test_evaluate_completeness_returns_correct_keys():
    from services.react_engine import evaluate_completeness
    result = evaluate_completeness('Was ist § 242 BGB?', ['Treu und Glauben...'])
    assert 'vollstaendig' in result, 'Key vollstaendig fehlt'
    assert 'fehlende_aspekte' in result, 'Key fehlende_aspekte fehlt'
    assert isinstance(result['vollstaendig'], bool)
    assert isinstance(result['fehlende_aspekte'], list)


def test_vollstaendig_returns_empty_aspekte():
    from services.react_engine import evaluate_completeness
    result = evaluate_completeness('Was ist § 242 BGB?', ['Treu und Glauben...'])
    if result['vollstaendig']:
        assert result['fehlende_aspekte'] == [], 'VOLLSTÄNDIG muss leere fehlende_aspekte haben'


def test_unvollstaendig_returns_aspekte():
    from services.react_engine import evaluate_completeness
    result = evaluate_completeness('Was ist § 242 BGB?', [])
    if not result['vollstaendig']:
        assert len(result['fehlende_aspekte']) >= 1, 'UNVOLLSTÄNDIG braucht mind. 1 fehlenden Aspekt'
