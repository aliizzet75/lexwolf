using System.Collections.Generic;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

// Task #311: Anonymisierten Text im Live-Panel sichtbar machen. Testet die reine
// Text-Zusammensetzung (ohne WebView2/WPF) für genau das, was MainWindow.PostChatAsync
// bzw. SchriftsatzDialog.OnErstellen tatsächlich an den Server schicken (Task #308).
public class AnonymisiertGesendetBuilderTests
{
    [Fact]
    public void BuildFuerChat_ZeigtAnonymisierteNachrichtNichtOriginal()
    {
        var mapping = new Dictionary<string, string>();
        var anonym = AnonymizerEngine.AnonymizeText("Mandant Hans Müller bittet um Einschätzung.", mapping);

        var text = AnonymisiertGesendetBuilder.BuildFuerChat(anonym, null);

        Assert.DoesNotContain("Hans Müller", text);
        Assert.Contains("[PERSON_1]", text);
    }

    [Fact]
    public void BuildFuerChat_OhneKontext_ZeigtNurNachricht()
    {
        var text = AnonymisiertGesendetBuilder.BuildFuerChat("Nachricht ohne PII", null);
        Assert.Equal("Nachricht ohne PII", text);
    }

    [Fact]
    public void BuildFuerChat_MitKontext_EnthaeltBeideAbschnitte()
    {
        var text = AnonymisiertGesendetBuilder.BuildFuerChat("Nachricht", "Mandant: [PERSON_1]");
        Assert.Contains("Nachricht", text);
        Assert.Contains("Kontext (anonymisiert)", text);
        Assert.Contains("[PERSON_1]", text);
    }

    [Fact]
    public void BuildFuerSchriftsatz_ZeigtAnonymisierteAnweisungUndDokumenteNichtOriginal()
    {
        var mapping = new Dictionary<string, string>();
        var anweisungAnonym = AnonymizerEngine.AnonymizeText("Verfasse eine Klage für Hans Müller.", mapping);
        var dokumentAnonym = AnonymizerEngine.AnonymizeText("Hans Müller wohnt Musterstraße 5.", mapping);

        var text = AnonymisiertGesendetBuilder.BuildFuerSchriftsatz(
            anweisungAnonym,
            new List<(string Titel, string Text)> { ("Schreiben.pdf", dokumentAnonym) });

        Assert.DoesNotContain("Hans Müller", text);
        Assert.DoesNotContain("Musterstraße", text);
        Assert.Contains("[PERSON_1]", text);
        Assert.Contains("Schreiben.pdf", text);
        Assert.Contains("Dokumente (anonymisiert)", text);
    }

    [Fact]
    public void BuildFuerSchriftsatz_OhneDokumente_ZeigtNurAnweisung()
    {
        var text = AnonymisiertGesendetBuilder.BuildFuerSchriftsatz("Anweisung ohne PII", new List<(string Titel, string Text)>());
        Assert.Equal("Anweisung ohne PII", text);
    }

    [Fact]
    public void BuildFuerChat_LeererText_LiefertLeerenString()
    {
        Assert.Equal("", AnonymisiertGesendetBuilder.BuildFuerChat("", null));
    }
}
