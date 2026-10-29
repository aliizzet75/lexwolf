using System;
using System.Drawing;

namespace LexWolf.Services;

/// <summary>
/// Farbwerte der Oberfläche, abgeleitet aus dem gewählten Helligkeitsmodus.
/// Die Werte landen als Ressourcen LwFensterHintergrund/LwVordergrund/LwOberflaeche/LwAkzent
/// in App.xaml und werden per DynamicResource verwendet.
/// </summary>
public readonly record struct Theme(Color Hintergrund, Color Vordergrund, Color Oberflaeche, Color Akzent)
{
    public static Theme Aus(bool dunkel)
    {
        return dunkel
            ? new Theme(
                Hintergrund: ColorTranslator.FromHtml("#0d1117"),
                Vordergrund: ColorTranslator.FromHtml("#c9d1d9"),
                Oberflaeche: ColorTranslator.FromHtml("#161b22"),
                Akzent: ColorTranslator.FromHtml("#e8a838"))
            : new Theme(
                Hintergrund: ColorTranslator.FromHtml("#ffffff"),
                Vordergrund: ColorTranslator.FromHtml("#1b1f24"),
                Oberflaeche: ColorTranslator.FromHtml("#f0f2f5"),
                Akzent: ColorTranslator.FromHtml("#b36b0f"));
    }
}
