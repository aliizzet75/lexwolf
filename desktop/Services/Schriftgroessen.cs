using System;

namespace LexWolf.Services;

/// <summary>
/// Abgestufte Schriftgrößen der Oberfläche, abgeleitet aus der eingestellten Grundgröße
/// (Einstellungen bzw. A+/A− im Chat). Die Stufen landen als Ressourcen
/// LwSchriftKlein/Normal/Gross/Titel in App.xaml und werden per DynamicResource verwendet.
/// </summary>
public readonly record struct Schriftgroessen(double Klein, double Normal, double Gross, double Titel)
{
    public const double Minimum = 10;
    public const double Maximum = 24;
    public const double Standard = 13;

    public static Schriftgroessen Aus(double grundgroesse)
    {
        var g = double.IsNaN(grundgroesse) ? Standard : Math.Clamp(grundgroesse, Minimum, Maximum);
        // "Klein" nur einen Punkt kleiner: Hinweise müssen lesbar bleiben, nicht nur dekorativ.
        return new Schriftgroessen(Klein: g - 1, Normal: g, Gross: g + 2, Titel: g + 6);
    }
}
