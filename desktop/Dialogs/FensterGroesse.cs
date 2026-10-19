using System;
using System.Windows;
using LexWolf.Services;

namespace LexWolf.Dialogs;

/// <summary>Dialoge wachsen mit der eingestellten Schriftgröße, damit bei großer Schrift
/// nichts gequetscht wird — höchstens bis 92 % des Bildschirms.</summary>
public static class FensterGroesse
{
    public static void AnSchriftAnpassen(Window w)
    {
        var normal = Application.Current?.TryFindResource("LwSchriftNormal") is double d ? d : Schriftgroessen.Standard;
        var faktor = Math.Max(1, normal / Schriftgroessen.Standard);
        var bereich = SystemParameters.WorkArea;
        w.Width = Math.Min(w.Width * faktor, bereich.Width * 0.92);
        w.Height = Math.Min(w.Height * faktor, bereich.Height * 0.92);
        w.MinWidth = Math.Min(w.MinWidth, w.Width);
        w.MinHeight = Math.Min(w.MinHeight, w.Height);
    }
}
