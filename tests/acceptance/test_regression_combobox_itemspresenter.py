import os

_APP_XAML = os.path.join(os.path.dirname(__file__), "..", "..", "desktop", "App.xaml")


def test_darkcomboboxstyle_popup_verwendet_itemspresenter():
    """Regressionstest fuer Commit 17d5c4e (2026-09-22): <ItemsPresenter/> im
    Popup von DarkComboBoxStyle wurde durch <VirtualizingStackPanel
    IsItemsHost="True"/> ersetzt. DarkComboBoxStyle ist global (App.xaml) und
    wird u.a. von MainWindow (MandantBox) und SettingsDialog (Schriftart-
    ComboBoxen) verwendet - die Aenderung liess betroffene Dropdowns leer
    bleiben bzw. den Dialog beim Oeffnen abstuerzen. Ueber 20 automatisch
    verifizierte "Dropdown leer"-Bugfix-Versuche haben die eigentliche Ursache
    nie gefunden, weil kein automatischer Test das echte WPF-Rendering pruefen
    kann - dieser Test soll zumindest ein erneutes Einfuehren derselben
    Aenderung verhindern."""
    xaml = open(_APP_XAML, encoding="utf-8").read()
    assert "<ItemsPresenter/>" in xaml, (
        "DarkComboBoxStyle-Popup muss <ItemsPresenter/> verwenden, nicht ein "
        "direkt eingesetztes Panel mit IsItemsHost=True (siehe Commit 17d5c4e "
        "und dessen Revert) - das brach jede ComboBox mit diesem globalen Style."
    )
    assert 'VirtualizingStackPanel IsItemsHost="True"' not in xaml
