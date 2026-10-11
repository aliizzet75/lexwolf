"""
Keyword-based legal field extraction from queries.
Used as a fallback when regex-based tag extraction fails.
"""

# Mapping of legal field keywords to tag abbreviations
LEGAL_FIELD_KEYWORDS = {
    'kschg': [
        'kündigung', 'arbeitgeber', 'arbeitnehmer', 'arbeitsvertrag', 'arbeitsplatz',
        'arbeitszeit', 'urlaub', 'Abmahnung', 'Freistellung', 'Weiterbeschäftigung',
        'Sozialauswahl', 'betriebsbedingt', 'ordentlicher', 'fristlos',
        'außerordentliche', 'Probezeit', 'Kündigungsschutz', 'Änderungskündigung',
        'Aufhebungsvertrag', 'Mindestlohn', 'Lohnfortzahlung', 'Krankheit',
        'Schwangerschaft', 'Mutterschutz', 'Elternzeit', 'Elterngeld',
        'Arbeitnehmerüberwachung', 'Homeoffice', 'Gehalt', 'Lohnzahlung',
        'Überstunden', 'Vergütung', 'Arbeitszeugnis', 'Zeugnisanspruch',
        'Abfindung', 'Kündigungsarten', 'Kündigungsfrist', 'Kündigungsfristen',
        'Kündigungsschutzklage', 'Scheinselbstständigkeit',
        'Betriebsrat', 'Sozialplan'
    ],
    'bgb': [
        'miete', 'mieter', 'vermieter', 'kauf', 'vertrag', 'unterhalt', 'eltern',
        'kind', 'scheidung', 'erbe', 'erbschaft', 'schenkung', 'testament',
        'pflichtteil', 'erbschaftsausschlagung', 'nachlass', 'nachbar', 'grundstück',
        'eigentum', 'besitz', 'garantie', 'gewährleistung', 'widerruf',
        'anfechtung', 'verjährungsfrist', 'treu und glauben', 'höhere gewalt',
        'zahlt nicht', 'zahlungsverzug', 'darlehen', 'mietvertrag', 'mietkündigung',
        'mietminderung', 'mängel', 'mieterhöhung', 'Eigenbedarfskündigung',
        'Kündigungsschutz', 'Rückgabe', 'Kaution', 'Mietrückstand', 'Hausrecht',
        'Untervermietung', 'Schönheitsreparaturen', 'Instandhaltung', 'Tierhaltung',
        'Verbraucher', 'Preisbestimmung', 'Anwartschaftsrecht', 'Eigentumsvorbehalt',
        'Abstraktionsprinzip', 'Übereignung', 'Besitzrecht', 'Sachenrecht',
        'Leistungsverweigerung', 'Zurückbehaltungsrecht', 'Schenkungswiderruf',
        'Verarmung', 'GmbH', 'Geschäftsführerhaftung', 'GbR', 'Gesellschaftsrecht',
        'Erbrecht',
        # AGB (Allgemeine Geschäftsbedingungen) - §305ff BGB
        'agb', 'agb-kontrolle', 'allgemeine geschäftsbedingungen', 'klausel', 'unangemessen',
        'unwirksam', 'inhaltskontrolle', 'verbraucher', 'gewerbe',
        # Deliktsrecht (§§823ff BGB) - civil liability, not criminal
        'unfall', 'haftung', 'haftet', 'schadensersatz', 'schadenersatz',
        'deliktsrecht', 'delikt', 'verletzung', 'fahrlässig', 'vorsätzlich',
        # Arbeitsvertrag BGB (§611a, §614, §616, §630 BGB)
        'lohn', 'gehalt', 'vergütung', 'arbeitszeugnis', 'zeugnis', 'überstunden',
        'entgelt', 'fälligkeit',
        # Schuldrecht (§§280, §§305ff, §§433ff BGB)
        'kaufvertrag', 'rücktritt', 'minderung', 'liefert nicht', ' bürgschaft',
        'arglistig', 'täuschung', 'drohung', 'verjährung', 'ausschlagung',
        'arbeitsverhältnis',
    ],
    'stgb': [
        'bestohlen', 'beleidigung', 'straftat', 'strafrecht',
        'verbrechen', 'mord', 'totschlag', 'diebstahl', 'ausrauben', 'erpressung',
        'coercion', 'einbruch', 'einbruchsdiebstahl', 'vandalismus',
        'strafanzeige', 'verurteilung', 'freispruch', 'revisionsgrund', 'verfahrensfehler',
        'unfall', 'haftung', 'haftet', 'schadensersatz', 'schadenersatz',
        'deliktsrecht', 'delikt', 'verletzung', 'fahrlässig', 'vorsätzlich',
        'unfallverursacher', 'unfallflucht', 'fahrerflucht',
    ],
    'zpo': [
        'klage', 'verfahren', 'prozess', 'berufung', 'revision', 'zuständigkeit',
        'anwalt', 'anwaltskosten', 'prozesskosten', 'kostenrecht', 'prozessbevollmächtigter',
        'prozessführung', 'prozesswert', 'prozesskostenhilfe', 'prozesshindernisse',
        'prozessverzögerung', 'prozessverhinderung'
    ],
    'stpo': [
        'verdächtiger', 'angeklagter', 'verfahren', 'beschuldigter', 'verdächtig',
        'ermittlung', 'fahndung', 'durchsuchung', 'haft', 'untersuchungshaft',
        'prozesskosten', 'anwalt', 'verteidiger', 'staatsanwalt', 'richter',
        'verurteilung', 'freispruch', 'revisionsgrund', 'verfahrensfehler'
    ],
    'vg': [
        'verwaltung', 'behörde', 'antrag', 'genehmigung', 'förmigkeit',
        'verwaltungsakt', 'verwaltungsverfahren', 'verwaltungsprozess',
        'verwaltungsrecht', 'öffentliches recht', 'öffentlich-rechtlich'
    ],
    'sgb_5': [
        'krankenkasse', 'krankenversicherung', 'krankengeld', 'beipackzettel',
        'arzneimittel', 'versichertenstatus', 'kostenübernahme', 'medizinische',
        'heilbehandlung', 'rehabilitation', 'medizinische', 'pflege'
    ],
    'sgb_8': [
        'kindergarten', 'kinderbetreuung', 'kindertagespflege', 'jugendamt',
        'jugendhilfe', 'familienleistungen', 'kindergeld', 'unterhalt', 'elterngeld'
    ],
    'sgb_9': [
        'rehabilitation', 'teilhabe', 'behinderten', 'schwerbehindert',
        'behinderung', ' rehabilitation', 'hilfe', 'pflege'
    ],
    'sgb_10': [
        'sozialhilfe', 'alg', 'arbeitslosengeld', 'bürgergeld', 'wohnung', 'hilfen',
        'einstiegsgeld', 'bergbau', 'sozialausgleich', 'sozialleistungen'
    ],
    'sgb_11': [
        'kinder', 'jugendliche', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe',
        'erziehungsdienst', 'erziehungshilfe'
    ],
    'sgb_12': [
        'arbeitsmarkt', 'arbeitsvermittlung', 'arbeitslosigkeit', 'arbeitsförderung',
        'arbeitsuchend', 'arbeitsfähigkeit', 'arbeitsvermittlung', 'arbeitslosengeld'
    ],
    'sgb_13': [
        'rente', 'alter', 'alterrente', 'Altersrente', 'erwerbsminderung',
        'erwerbsunfähigkeit', 'altersteilzeit', 'altersteilzeitanteil'
    ],
    'sgb_14': [
        'leistungen', 'kinder', 'familie', 'familienleistungen', 'familienurlaub',
        'familienpflege'
    ],
    'sgb_15': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_18': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_20': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_22': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_23': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_24': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_25': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_26': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_27': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_28': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_29': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_30': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_31': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_32': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_33': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_34': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_35': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_36': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_37': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_38': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_39': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_40': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_41': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_42': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_43': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_44': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_45': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_46': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_47': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_48': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_49': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
    'sgb_50': [
        'kinder', 'erziehung', 'hilfe', 'hilfen', 'erziehungshelfe', 'erziehungsdienst'
    ],
}

def extract_legal_fields(query: str) -> set:
    """Extract legal field tags from query using keyword matching."""
    found_tags = set()
    query_lower = query.lower()
    
    for tag_key, keywords in LEGAL_FIELD_KEYWORDS.items():
        for keyword in keywords:
            if keyword.lower() in query_lower:
                found_tags.add(tag_key)
                break
    
    return found_tags
