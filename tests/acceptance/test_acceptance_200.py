import os
import re
import socket
import subprocess
import sys

import pytest

BASE = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf/tools/Anonymisierer/Anonymisierer.Tests"
SCRIPT = os.path.join(BASE, "run_acceptance_test.py")
REPORT = os.path.join(BASE, "anon_report.md")


def ner_ok():
    s = socket.socket()
    s.settimeout(2)
    r = s.connect_ex(("localhost", 8000))
    s.close()
    return r == 0


@pytest.mark.skipif(not ner_ok(), reason="NER-Service nicht erreichbar auf Port 8000 (backend/api/ner.py)")
def test_run_acceptance_test_produces_valid_report():
    assert os.path.isfile(SCRIPT), f"run_acceptance_test.py fehlt: {SCRIPT}"
    subprocess.run([sys.executable, SCRIPT], cwd=BASE, check=True, timeout=300)
    assert os.path.isfile(REPORT), f"anon_report.md wurde nicht erzeugt: {REPORT}"

    content = open(REPORT, encoding="utf-8").read()
    m_pii = re.search(r"PII-Treffer[:\s]+(\d+)", content)
    assert m_pii and int(m_pii.group(1)) == 0, "Report zeigt PII-Treffer > 0"

    m_ctx = re.search(r"Kontexterhaltung[^\d]*([\d.]+)\s*%", content)
    assert m_ctx and float(m_ctx.group(1)) >= 90.0, "Kontexterhaltungs-Score unter 90%"
