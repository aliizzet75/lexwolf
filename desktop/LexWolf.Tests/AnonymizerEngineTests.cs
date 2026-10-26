using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

// Task #308: Anonymisierung vor Versand real verdrahten. Diese Tests prüfen genau den
// Baustein, der von MainWindow.PostChatAsync und SchriftsatzDialog.OnErstellen genutzt wird:
// AnonymizeText(text, aliasToOriginal) sammelt eine gemeinsame Alias-Zuordnung über mehrere
// Textfelder hinweg, DeAnonymizeText macht sie rückgängig, bevor der Anwalt die Antwort sieht.
public class AnonymizerEngineTests
{
    [Fact]
    public void AnonymizeText_ErsetztEchtenNamenDurchAlias()
    {
        var mapping = new Dictionary<string, string>();
        var anonymisiert = AnonymizerEngine.AnonymizeText("Bitte prüfen Sie den Fall von Hans Müller.", mapping);

        Assert.DoesNotContain("Hans Müller", anonymisiert);
        Assert.Contains("[PERSON_1]", anonymisiert);
        Assert.Equal("Hans Müller", mapping["PERSON_1"]);
    }

    [Fact]
    public void DeAnonymizeText_MachtAnonymisierungWiederRueckgaengig()
    {
        var mapping = new Dictionary<string, string>();
        var anfrage = AnonymizerEngine.AnonymizeText("Mandant Hans Müller bittet um Einschätzung.", mapping);

        // Die "Antwort" vom Server enthält denselben Alias, den auch der Server gesehen hat.
        var antwortVomServer = $"Für {ExtrahiereAlias(anfrage)} empfehlen wir folgendes Vorgehen.";
        var deAnonymisiert = AnonymizerEngine.DeAnonymizeText(antwortVomServer, mapping);

        Assert.Contains("Hans Müller", deAnonymisiert);
        Assert.DoesNotContain("[PERSON_1]", deAnonymisiert);
    }

    [Fact]
    public void AnonymizeText_NutztDenselbenAliasFuerDenselbenNamenUeberMehrereFelderHinweg()
    {
        // Simuliert Schriftsatz-Szenario: Anweisung + mehrere Akte-Dokumente, eine gemeinsame
        // Zuordnung (wie in SchriftsatzDialog.OnErstellen) — derselbe Mandant muss überall
        // denselben Alias bekommen, sonst verliert die KI den Zusammenhang.
        var mapping = new Dictionary<string, string>();
        var anweisung = AnonymizerEngine.AnonymizeText("Verfasse eine Klage für Hans Müller.", mapping);
        var dokument1 = AnonymizerEngine.AnonymizeText("Hans Müller wohnt in Musterstadt.", mapping);
        var dokument2 = AnonymizerEngine.AnonymizeText("Az. 1234 betrifft Hans Müller und Betrag €1.000,00.", mapping);

        Assert.Contains("[PERSON_1]", anweisung);
        Assert.Contains("[PERSON_1]", dokument1);
        Assert.Contains("[PERSON_1]", dokument2);
        Assert.Contains("[AKTENZEICHEN_1]", dokument2);
        Assert.Contains("[BETRAG_1]", dokument2);
        Assert.DoesNotContain("Hans Müller", anweisung + dokument1 + dokument2);

        var rueckuebersetzt = AnonymizerEngine.DeAnonymizeText(dokument2, mapping);
        Assert.Contains("Hans Müller", rueckuebersetzt);
        Assert.Contains("Az. 1234", rueckuebersetzt);
        Assert.Contains("€1.000,00", rueckuebersetzt);
    }

    [Fact]
    public void AnonymizeText_UnterschiedlicheNamenBekommenUnterschiedlicheAliase()
    {
        var mapping = new Dictionary<string, string>();
        var text = AnonymizerEngine.AnonymizeText("Hans Müller gegen Peter Schmidt.", mapping);

        Assert.Contains("[PERSON_1]", text);
        Assert.Contains("[PERSON_2]", text);
        Assert.Equal("Hans Müller", mapping["PERSON_1"]);
        Assert.Equal("Peter Schmidt", mapping["PERSON_2"]);
    }

    [Fact]
    public void AnonymizeText_LeererTextBleibtUnveraendert()
    {
        var mapping = new Dictionary<string, string>();
        Assert.Equal("", AnonymizerEngine.AnonymizeText("", mapping));
        Assert.Empty(mapping);
    }

    [Fact]
    public void ChatPayload_WieAnMainWindowPostChatAsync_EnthaeltImNetzwerkRequestKeinenEchtenNamen()
    {
        // Baut exakt die Payload-Form nach, die MainWindow.PostChatAsync über HttpClient
        // an den Server schickt ("Netzwerk-Mitschnitt" lässt sich hier als der tatsächlich
        // serialisierte JSON-Request-Body prüfen): nur anonymisierter Text geht raus.
        var history = new[]
        {
            ("user", "Mein Mandant Hans Müller wohnt Musterstraße 5, Az. 1234, Streitwert €1.000,00."),
        };
        var aliasToOriginal = new Dictionary<string, string>();
        var anonymizedMessages = history
            .Select(m => new { role = m.Item1, content = AnonymizerEngine.AnonymizeText(m.Item2, aliasToOriginal) })
            .ToArray();
        var anonymizedMandantContext = AnonymizerEngine.AnonymizeText(
            "Mandant: Hans Müller (ID: 42)", aliasToOriginal);

        var requestBody = JsonSerializer.Serialize(new
        {
            messages = anonymizedMessages,
            mandant_context = anonymizedMandantContext,
        });

        Assert.DoesNotContain("Hans Müller", requestBody);
        Assert.DoesNotContain("Musterstraße", requestBody);
        Assert.Contains("PERSON_1", requestBody);

        // Server/KI antwortet mit demselben Alias — Anwalt darf nur den echten Namen sehen.
        var serverResponseContent = $"Für [PERSON_1] empfehlen wir, die Klage einzureichen.";
        var angezeigterText = AnonymizerEngine.DeAnonymizeText(serverResponseContent, aliasToOriginal);

        Assert.Contains("Hans Müller", angezeigterText);
        Assert.DoesNotContain("PERSON_1", angezeigterText);
    }

    private static string ExtrahiereAlias(string anonymisierterText)
    {
        var start = anonymisierterText.IndexOf('[');
        var ende = anonymisierterText.IndexOf(']', start);
        return anonymisierterText[start..(ende + 1)];
    }
}
