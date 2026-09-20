import os

_ROOT = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
if os.path.isdir("/data/.openclaw/workspace-codex/projects/lexwolf"):
    _ROOT = "/data/.openclaw/workspace-codex/projects/lexwolf"

APP_SETTINGS = os.path.join(_ROOT, "desktop/Services/AppSettings.cs")
SETTINGS_XAML = os.path.join(_ROOT, "desktop/Dialogs/SettingsDialog.xaml")
SETTINGS_CS = os.path.join(_ROOT, "desktop/Dialogs/SettingsDialog.xaml.cs")
MAIN_CS = os.path.join(_ROOT, "desktop/MainWindow.xaml.cs")
DIFF_CS = os.path.join(_ROOT, "desktop/Controls/DiffView.xaml.cs")


def test_opt_in_flags_default_false_in_appsettings():
    text = open(APP_SETTINGS, encoding="utf-8").read()
    assert "FeedbackOptIn" in text and "AbTestOptIn" in text
    assert "bool FeedbackOptIn { get; set; } = false" in text.replace("  ", " ")
    assert "bool AbTestOptIn { get; set; } = false" in text.replace("  ", " ")


def test_settings_dialog_hat_feedback_checkboxes_mit_erklaerung():
    xaml = open(SETTINGS_XAML, encoding="utf-8").read()
    assert "CheckBox" in xaml
    assert "Stil-Verbesserungen senden" in xaml
    assert "A/B-Tests teilnehmen" in xaml
    assert "satzlaengen" in xaml.lower() or "stil-metriken" in xaml.lower()


def test_deaktivierung_wirkt_sofort_vor_dem_senden():
    for path in (MAIN_CS, DIFF_CS):
        text = open(path, encoding="utf-8").read()
        assert "FeedbackOptIn" in text, f"{path} prueft OptIn-Flag nicht vor dem Senden"
