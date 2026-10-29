namespace LexWolf.Services;

/// <summary>
/// Die vier Kernfarben der Oberfläche für Hell/Dunkel. Landen als Ressourcen
/// LwFensterHintergrund/LwVordergrund/LwOberflaeche/LwAkzent in App.xaml und
/// werden per DynamicResource verwendet (App.SetzeTheme setzt sie um).
/// Bewusst nur Hex-Strings statt System.Windows.Media.Color, damit diese Datei
/// ohne WPF-Referenz testbar bleibt (wie Schriftgroessen.cs).
/// </summary>
public readonly record struct Theme(string Hintergrund, string Vordergrund, string Oberflaeche, string Akzent)
{
    public static Theme Aus(bool dunkel) => dunkel
        ? new Theme(Hintergrund: "#0d1117", Vordergrund: "#c9d1d9", Oberflaeche: "#161b22", Akzent: "#e8a838")
        : new Theme(Hintergrund: "#ffffff", Vordergrund: "#1b1f24", Oberflaeche: "#f0f2f5", Akzent: "#b36b0f");
}
