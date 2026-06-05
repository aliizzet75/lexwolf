import os
import sys
import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', 'backend'))

from services.paragraph_parser import extract_references

def test_paragraph_parser_exists_and_callable():
    result = extract_references("§23 KSchG")
    assert isinstance(result, list)

def test_erkennt_paragraph_kschg():
    result = extract_references("§23 KSchG")
    assert len(result) == 1
    assert result[0]['paragraph_nr'] == '23'
    assert result[0]['gesetz'] == 'KSchG'

def test_erkennt_artikel_gg():
    result = extract_references("Art. 5 GG")
    assert len(result) == 1
    assert 'paragraph_nr' in result[0]
    assert result[0]['gesetz'] == 'GG'

def test_erkennt_abs_bgb():
    result = extract_references("§1 Abs. 1 BGB")
    assert len(result) >= 1
    assert any(r['gesetz'] == 'BGB' for r in result)

def test_zwei_referenzen_in_einem_satz():
    result = extract_references("§ 1 KSchG verweist auf § 23 KSchG")
    assert len(result) == 2
    nummern = {r['paragraph_nr'] for r in result}
    assert '1' in nummern
    assert '23' in nummern

def test_dict_hat_pflichtfelder():
    result = extract_references("§5 BGB")
    assert len(result) >= 1
    for r in result:
        assert 'paragraph_nr' in r
        assert 'gesetz' in r
        assert 'kontext' in r
