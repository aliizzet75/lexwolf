using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// T#321: Akzeptanztests für die chat-seitige Erkennung bekannter Schriftsatz-Arten.
    /// Es müssen mindestens 3 Beispielsätze pro erkannter Art erkannt werden und
    /// mindestens 2 Sätze ohne erkannte Art zurückgeben null.
    /// </summary>
    public class SchriftsatzArtErkennungTests
    {
        // ── Erkennungs-Sätze ───────────────────────────────────────────────────

        [Theory]
        [InlineData("Klageerwiderung", "Schreib mir eine Klageerwiderung zum Fall.")]
        [InlineData("Klageerwiderung", "Ich brauche eine Klageerwiderung zum Fall.")]
        [InlineData("Klageerwiderung", "Bitte die Klageerwiderung für den Mandanten vorbereiten.")]
        [InlineData("Klageschrift", "Erstelle eine Klageschrift wegen Kündigungsschutz.")]
        [InlineData("Klageschrift", "Die Klageschrift muss bis Montag fertig sein.")]
        [InlineData("Klageschrift", "Kannst du die Klageschrift entwerfen?")]
        [InlineData("Antragsschrift (Familiensache)", "Bitte die familiensachliche Antragsschrift vorbereiten.")]
        [InlineData("Antragsschrift (Familiensache)", "Ich brauche eine familienrechtliche Antragsschrift.")]
        [InlineData("Antragsschrift (Familiensache)", "Bitte die Antragsschrift in der Familiensache verfassen.")]
        [InlineData("Antragserwiderung (Familiensache)", "Antragserwiderung im Familienverfahren vorbereiten.")]
        [InlineData("Antragserwiderung (Familiensache)", "Antragserwiderung im Sorgerechtsverfahren vorbereiten.")]
        [InlineData("Antragserwiderung (Familiensache)", "Bitte die familiensachliche Antragserwiderung entwerfen.")]
        [InlineData("Stellungnahme an das Gericht", "Verfasse eine Stellungnahme an das Gericht.")]
        [InlineData("Stellungnahme an das Gericht", "Wir sollten uns gegenüber dem Gericht äußern.")]
        [InlineData("Stellungnahme an das Gericht", "Stellungnahme einreichen, bitte vorbereiten.")]
        [InlineData("Berufungsbegründung", "Ich benötige eine Berufungsbegründung.")]
        [InlineData("Berufungsbegründung", "Die Berufungsbegründung muss vorbereitet werden.")]
        [InlineData("Berufungsbegründung", "Formuliere die Berufungsbegründung zum Fall.")]
        [InlineData("Beschwerdebegründung", "Schreib eine Beschwerdebegründung.")]
        [InlineData("Beschwerdebegründung", "Beschwerdebegründung zum Fall verfassen.")]
        [InlineData("Beschwerdebegründung", "Ich brauche die Beschwerdebegründung bis Freitag.")]
        [InlineData("Außergerichtliches Schreiben an die Gegenseite", "Entwurf für ein Schreiben an die Gegenseite.")]
        [InlineData("Außergerichtliches Schreiben an die Gegenseite", "Außergerichtliches Schreiben an die Gegenseite verfassen.")]
        [InlineData("Außergerichtliches Schreiben an die Gegenseite", "Bitte ein außergerichtliches Schreiben an die Gegenseite erstellen.")]
        [InlineData("Gutachten / Erfolgsaussichten", "Erstelle ein Gutachten zu den Erfolgsaussichten.")]
        [InlineData("Gutachten / Erfolgsaussichten", "Ich benötige eine Einschätzung der Erfolgsaussichten.")]
        [InlineData("Gutachten / Erfolgsaussichten", "Bitte das Gutachten / Erfolgsaussichten zum Verfahren.")]
        [InlineData("Freier Entwurf (siehe Anweisung)", "Freier Entwurf: bitte einen Mietvertrag prüfen.")]
        [InlineData("Freier Entwurf (siehe Anweisung)", "Ich brauche einen freien Entwurf.")]
        [InlineData("Freier Entwurf (siehe Anweisung)", "Bitte als freien Entwurf eine Anweisung umsetzen.")]
        public void ErkenneArt_BekannteArten_WirdErkannt(string erwartet, string satz)
        {
            var art = SchriftsatzArtErkennung.ErkenneArt(satz);
            Assert.Equal(erwartet, art);
        }

        // ── Nicht-Erkennungs-Sätze ───────────────────────────────────────────────

        [Theory]
        [InlineData("Guten Morgen, was steht heute an?")]
        [InlineData("Bitte fasse die letzten E-Mails zusammen.")]
        [InlineData("Wie ist die Rechtslage zu Kündigungen?")]
        [InlineData("Danke für die Hilfe gestern.")]
        public void ErkenneArt_KeineArt_Null(string satz)
        {
            Assert.Null(SchriftsatzArtErkennung.ErkenneArt(satz));
        }
    }
}
