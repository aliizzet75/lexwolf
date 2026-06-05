import re
from typing import List, Dict

# Bekannte deutsche Gesetzes-Abkürzungen (erweiterbar)
_KNOWN_LAWS = {
    "GG","BGB","StGB","ZPO","StPO","HGB","InsO","AO","UStG","KSchG","ArbGG",
    "MaBV","BauGB","BauNVO","VOB","GmbHG","AktG","UmwG","WpHG","KWG","VAG",
    "SGB","SGBI","SGBII","SGBIII","SGBIV","SGBV","SGBVI","SGBVII","SGBVIII",
    "SGBIX","SGBX","SGBXI","SGBXII","AsylG","AufenthG","FreizügG","WEG","ErbbauRG",
    "HOAI","PartGG","GenG","UrhG","MarkenG","PatG","GebrMG","DesignG","UWG",
    "GWB","EnWG","TKG","TMG","DSGVO","BDSG","AGG","BetrVG","TVG","ArbSchG",
    "JArbSchG","MuSchG","BEEG","EntgFG","ArbZG","NachwG","TzBfG","BBiG",
    "HAG","AüG","BUrlG","PflegeZG","FPflZG","EFZG","LStDV","EStG","KStG",
    "GewStG","GrESt","ErbStG","BewG","GrStG","UmwStG","InvStG","ZVG","RPflG",
    "FamFG","FGG","VwGO","VwVfG","BayVwVfG","GVG","RVG","JVEG","GKG","FamGKG",
    "RVG","BRAO","BORA","PAO","StBerG","WPO","BNotO","BRRG","BBG","BDG",
    "GG","WRV","EGV","AEUV","EUV","EMRK","IPbpR",
}

# Regex: Gesetze sind i.d.R. 2–10 Buchstaben, oft Großbuchstaben oder gemischt
_LAW_ABBR_RE = re.compile(r'^[A-ZÄÖÜ][A-Za-zäöüÄÖÜß]{1,15}$')


def _is_law_abbr(token: str) -> bool:
    """True wenn token wie eine Gesetzes-Abkürzung aussieht."""
    if token in _KNOWN_LAWS:
        return True
    # Heuristik: 2-10 Zeichen, überwiegend Großbuchstaben
    if not _LAW_ABBR_RE.match(token):
        return False
    if len(token) < 2 or len(token) > 12:
        return False
    upper_ratio = sum(1 for c in token if c.isupper()) / len(token)
    return upper_ratio >= 0.4


def extract_references(text: str) -> List[Dict]:
    """
    Extrahiert §-Referenzen aus deutschem Gesetzestext.
    Filtert Abschnittstitel heraus (z.B. '§ 1 Wesen' → kein Treffer).

    Erkennt:
      §23 KSchG  |  § 23 KSchG  |  Art. 5 GG  |  §1 Abs. 1 BGB
    """
    references = []
    
    # Pattern 1: §-Referenzen (auch mit Absatz)
    # § 1 Abs. 1 BGB, §23 KSchG
    pattern_para = re.compile(
        r'§\s*(\d+[a-zA-Z]?(?:\s+Abs\.\s*\d+)?)\s+'
        r'([A-ZÄÖÜ][A-Za-zäöüÄÖÜß]+)'
    )
    
    # Pattern 2: Artikel-Referenzen (auch mit Absatz)
    # Art. 1 Abs. 1 GG, Art. 5 GG
    pattern_art = re.compile(
        r'Art\.\s*(\d+)\s+(Abs\.\s*\d+\s+)?([A-ZÄÖÜ][A-Za-zäöüÄÖÜß]+)'
    )
    
    # Finde alle §-Referenzen
    for m in pattern_para.finditer(text):
        paragraph_nr = m.group(1).strip()
        gesetz = m.group(2)
        if not _is_law_abbr(gesetz):
            continue
        references.append({
            'paragraph_nr': paragraph_nr,
            'gesetz': gesetz,
            'kontext': m.group(0),
        })
    
    # Finde alle Artikel-Referenzen
    for m in pattern_art.finditer(text):
        para_num = m.group(1)
        absatz = m.group(2)
        gesetz = m.group(3)
        if not _is_law_abbr(gesetz):
            continue
        if absatz:
            abs_num = re.search(r'\d+', absatz).group()
            paragraph_nr = f"Art. {para_num} Abs. {abs_num}"
        else:
            paragraph_nr = f"Art. {para_num}"
        references.append({
            'paragraph_nr': paragraph_nr,
            'gesetz': gesetz,
            'kontext': m.group(0),
        })
    
    return references
