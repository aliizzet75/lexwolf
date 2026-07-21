import pytest
from backend.api.unterhalt import UnterhaltRequest, berechne_unterhalt


def test_grundberechnung_gruppe4_stufe2():
    req = UnterhaltRequest(nettoeinkommen=3200.0, alter_kind=8)
    res = berechne_unterhalt(req)
    assert res.gruppe == 4, f"Erwartet Gruppe 4 (2.901-3.300€), erhalten: {res.gruppe}"
    assert res.altersstufe == 2, f"Erwartet Altersstufe 2 (6-11J), erhalten: {res.altersstufe}"
    assert res.betrag == 630.0, f"Erwartet 630€, erhalten: {res.betrag}"
    assert not res.mangelfall, "Kein Mangelfall erwartet bei 3200€ Einkommen"
    assert "630" in res.text_satz
    assert "Düsseldorfer Tabelle" in res.text_satz
    assert "2026" in res.text_satz


def test_mangelfall_erkannt():
    req = UnterhaltRequest(nettoeinkommen=1600.0, alter_kind=4)
    res = berechne_unterhalt(req)
    assert res.mangelfall, "Mangelfall erwartet: 1600€ < 1450€ Selbstbehalt + 426€ Unterhalt"
    assert res.selbstbehalt == 1450.0


def test_altersstufe_ab18():
    req = UnterhaltRequest(nettoeinkommen=2200.0, alter_kind=20)
    res = berechne_unterhalt(req)
    assert res.altersstufe == 4, f"Erwartet Altersstufe 4 (ab 18J), erhalten: {res.altersstufe}"
    assert res.betrag == 738.0, f"Erwartet 738€ (Gruppe 2, Stufe 4), erhalten: {res.betrag}"


def test_tabelle_2026_vollstaendig():
    req = UnterhaltRequest(nettoeinkommen=3000.0, alter_kind=10)
    res = berechne_unterhalt(req)
    assert isinstance(res.tabelle_2026, dict), "tabelle_2026 muss ein dict sein"
    assert len(res.tabelle_2026) >= 4, "Tabelle muss mindestens 4 Altersstufen enthalten"


def test_hoechste_gruppe_10():
    req = UnterhaltRequest(nettoeinkommen=7000.0, alter_kind=15)
    res = berechne_unterhalt(req)
    assert res.gruppe == 10, f"Erwartet Gruppe 10 (>6.500€), erhalten: {res.gruppe}"
    assert res.betrag == 1126.0, f"Erwartet 1126€, erhalten: {res.betrag}"
