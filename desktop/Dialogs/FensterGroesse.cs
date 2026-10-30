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

        // Bei SizeToContent="Height"/"Width"/"WidthAndHeight" liest WPF die jeweils
        // automatisch berechnete Dimension als NaN zurück (so stellt WPF intern "Auto"
        // dar) — w.Width/w.Height * faktor wäre dann NaN, und NaN auf Width/Height zu
        // setzen wirft "'NaN' ist kein gültiger Wert" (beobachtet beim Umbenennen-Dialog,
        // der genau das hat). Diese Dimension bleibt dann unberührt, WPF berechnet sie
        // ohnehin selbst aus dem Inhalt.
        if (!double.IsNaN(w.Width))
        {
            w.Width = Math.Min(w.Width * faktor, bereich.Width * 0.92);
            w.MinWidth = Math.Min(w.MinWidth, w.Width);
        }
        if (!double.IsNaN(w.Height))
        {
            w.Height = Math.Min(w.Height * faktor, bereich.Height * 0.92);
            w.MinHeight = Math.Min(w.MinHeight, w.Height);
        }
    }
}
