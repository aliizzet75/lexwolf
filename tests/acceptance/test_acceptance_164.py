"""T#164: Rate-Limit — warten statt Precheck/Verifikation überspringen"""
import importlib.util
import sys
from datetime import datetime, timezone, timedelta
from unittest.mock import patch

# Add workspace to path for import
_workspace_root = "/data/.openclaw/workspace-codex/projects/lexwolf"
if _workspace_root not in sys.path:
    sys.path.insert(0, _workspace_root)


def _aligator():
    spec = importlib.util.spec_from_file_location("aligator164", "/data/.openclaw/workspace-codex/projects/lexwolf/aligator.py")
    mod = importlib.util.module_from_spec(spec)
    with patch("subprocess.run"), patch("requests.get"), patch("requests.post"), patch("time.sleep"):
        spec.loader.exec_module(mod)
    return mod


def test_rate_limit_pause_h_is_0_5():
    assert _aligator().RATE_LIMIT_PAUSE_H == 0.5, f"RATE_LIMIT_PAUSE_H={_aligator().RATE_LIMIT_PAUSE_H}, erwartet 0.5"


def test_precheck_waits_not_skips_under_90min():
    mod = _aligator()
    mod._state.setdefault("rate_limits", {})["claude"] = (datetime.now(timezone.utc) + timedelta(minutes=30)).isoformat()
    slept, tg = [], []
    fake = lambda p, l, timeout=1800: (0, "ALIGATOR_STATUS: SUCCESS\nPRECHECK_TEST: none")
    with patch.object(mod, "tg_send", tg.append), patch("time.sleep", slept.append), patch.object(mod, "_run_claude", fake):
        result = mod.run_claude_precheck(164, 1, allow_blocker=False)
    assert slept, "Precheck hat nicht gewartet (time.sleep nie aufgerufen) — nicht implementiert"
    assert any("⏳" in m or "Rate-Limit" in m for m in tg), f"Keine TG-Nachricht beim Warten. {tg}"
    assert result.get("rate_limited") is not True, "Precheck hat übersprungen statt gewartet"


def test_precheck_skips_with_warning_after_90min():
    mod = _aligator()
    mod._state.setdefault("rate_limits", {})["claude"] = (datetime.now(timezone.utc) + timedelta(minutes=100)).isoformat()
    tg = []
    with patch.object(mod, "tg_send", tg.append), patch("time.sleep", lambda s: None):
        result = mod.run_claude_precheck(164, 1, allow_blocker=False)
    assert result.get("rate_limited") is True, "Soll bei >90 Min überspringen"
    assert any("90" in m or "übersprungen" in m.lower() for m in tg), f"Keine TG-Warnung >90 Min. {tg}"
