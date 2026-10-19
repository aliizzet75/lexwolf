using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class SchriftgroessenTests
{
    [Fact]
    public void Stufen_folgen_der_Grundgroesse()
    {
        var s = Schriftgroessen.Aus(15);
        Assert.Equal(14, s.Klein);
        Assert.Equal(15, s.Normal);
        Assert.Equal(17, s.Gross);
        Assert.Equal(21, s.Titel);
    }

    [Theory]
    [InlineData(4, Schriftgroessen.Minimum)]
    [InlineData(40, Schriftgroessen.Maximum)]
    [InlineData(double.NaN, Schriftgroessen.Standard)]
    public void Grundgroesse_wird_begrenzt(double eingabe, double erwartet) =>
        Assert.Equal(erwartet, Schriftgroessen.Aus(eingabe).Normal);

    // Diese Dialoge sind auf die einstellbare Schriftgröße umgestellt. Neue feste Größen
    // würden die Einstellung dort wieder aushebeln (siehe ROADMAP P1-0).
    [Theory]
    [InlineData("Dialogs/SachstandDialog.xaml")]
    [InlineData("Dialogs/AkteDialog.xaml")]
    [InlineData("Dialogs/SchriftsatzDialog.xaml")]
    [InlineData("Dialogs/RechnerDialog.xaml")]
    [InlineData("Dialogs/FragebogenDialog.xaml")]
    public void Ueberarbeitete_Dialoge_haben_keine_festen_Schriftgroessen(string datei)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LexWolf.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, datei));
        var fest = Regex.Matches(xaml, @"FontSize=""\d").Select(m => m.Value).ToList();
        Assert.Empty(fest);
        Assert.DoesNotMatch(@"Property=""FontSize""\s+Value=""\d", xaml);
    }
}
