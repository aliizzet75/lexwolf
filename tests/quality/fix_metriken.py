#!/usr/bin/env python3
"""Correct metriken in results.json after test completion."""
import json
from datetime import datetime

RESULTS_PATH = '/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/tests/quality/results.json'

with open(RESULTS_PATH, encoding='utf-8') as f:
    results = json.load(f)

ergebnisse = results["ergebnisse"]
scores = [e.get("judge_score", 1) for e in ergebnisse]
avg_score = sum(scores) / len(scores)

# Corrected halluzinations_rate: false § references (not in DB) / total § references
qg_gesamt_total = sum(e.get("qg_refs_gesamt", 0) for e in ergebnisse)
qg_in_db_total = sum(e.get("qg_refs_in_db", 0) for e in ergebnisse)
halluzinations_rate = (qg_gesamt_total - qg_in_db_total) / qg_gesamt_total if qg_gesamt_total > 0 else 0.0

# vollstaendigkeits_score: avg fraction of expected §§ found in answer
vollst_scores = []
for e in ergebnisse:
    erwartet = set(e.get("erwartete_paragraphen", []))
    korrekt = set(e.get("korrekte_quellen", []))
    if not erwartet:
        vollst_scores.append(1.0)
    else:
        vollst_scores.append(len(korrekt) / len(erwartet))
vollstaendigkeits_score = sum(vollst_scores) / len(vollst_scores) if vollst_scores else 0.0

qg_gesamt_per_q = sum(e.get("qg_refs_gesamt", 0) for e in ergebnisse)
qg_in_db_per_q = sum(e.get("qg_refs_in_db", 0) for e in ergebnisse)
quellen_gen = qg_in_db_per_q / qg_gesamt_per_q if qg_gesamt_per_q > 0 else 1.0

metriken = {
    "gesamt_score": round(avg_score, 3),
    "vollstaendigkeits_score": round(vollstaendigkeits_score, 3),
    "halluzinations_rate": round(halluzinations_rate, 3),
    "quellen_genauigkeit": round(quellen_gen, 3),
    "getestete_fragen": len(ergebnisse),
    "schwache_antworten": sum(1 for s in scores if s < 3),
}

results["metriken"] = metriken
results["meta"]["metriken_korrektur"] = datetime.now().isoformat()

with open(RESULTS_PATH, 'w', encoding='utf-8') as f:
    json.dump(results, f, ensure_ascii=False, indent=2)

print(json.dumps(metriken, indent=2))

# DOD checks
print("\n--- DOD CHECKS ---")
print(f"vollstaendigkeits_score >= 0.75: {metriken['vollstaendigkeits_score']} -> {'PASS' if metriken['vollstaendigkeits_score'] >= 0.75 else 'FAIL'}")
print(f"halluzinations_rate <= 0.05:     {metriken['halluzinations_rate']} -> {'PASS' if metriken['halluzinations_rate'] <= 0.05 else 'FAIL'}")
print(f"quellen_genauigkeit >= 0.90:     {metriken['quellen_genauigkeit']} -> {'PASS' if metriken['quellen_genauigkeit'] >= 0.90 else 'FAIL'}")

# Per-domain scores
from collections import defaultdict
domain_scores = defaultdict(list)
for e in ergebnisse:
    fid = e.get("frage_id", 0)
    score = e.get("judge_score", 1)
    # Group by domain (frage_id 1-10=Arbeitsrecht, 11-20=Familienrecht, etc. - rough grouping)
    domain = f"group_{(int(fid)-1)//10 + 1}" if isinstance(fid, int) else "other"
    domain_scores[domain].append(score)

print("\n--- DOMAIN SCORES ---")
for domain, dscores in sorted(domain_scores.items()):
    avg = sum(dscores) / len(dscores)
    print(f"{domain}: avg={avg:.3f} (n={len(dscores)}) -> {'FAIL' if avg < 0.60 else 'OK'}")

# Weak answers
weak = [e for e in ergebnisse if e.get("judge_score", 1) < 3]
print(f"\n--- SCHWACHE ANTWORTEN (score < 3): {len(weak)} ---")
for e in weak:
    print(f"  [{e.get('frage_id')}] score={e.get('judge_score')} frage={e.get('frage', '')[:60]}")
