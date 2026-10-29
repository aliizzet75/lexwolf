using System.Linq;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests;

public class ChatHtmlRendererTests
{
    [Fact]
    public void WrapSearchPanel_RendersCompactButton_NotDetailsPanel()
    {
        var html = ChatHtmlRenderer.WrapSearchPanel(
            "<div>step</div>",
            laufend: false,
            modus: "schnell",
            gefundenCount: 2,
            anonymisiertGesendet: "anonymisiert",
            panelIndex: 5);

        Assert.DoesNotContain("<details", html);
        Assert.DoesNotContain("class='such-panel'", html); // altes aufklappbares Panel entfernt
        Assert.Contains("such-panel-compact", html);
        Assert.Contains("data-action='recherche'", html);
        Assert.Contains("data-msg='5'", html);
        Assert.Contains("Recherche ansehen (2 Quelle(n))", html);
    }

    [Fact]
    public void WrapSearchPanel_RunningState_ShowsLoadingLabel()
    {
        var html = ChatHtmlRenderer.WrapSearchPanel(
            "<div>step</div>",
            laufend: true,
            modus: "tief",
            gefundenCount: 0,
            panelIndex: 1);

        Assert.Contains("Recherche l&#228;uft", html);
        Assert.Contains("Modus: tief", html);
    }

    [Fact]
    public void BuildRechercheDialogHtml_ContainsStepsAndAnonymizedSection()
    {
        var steps = ChatHtmlRenderer.SearchStepRowHtml("suchergebnis", "KSchG", "Treffer", 0.85, null);
        var html = ChatHtmlRenderer.BuildRechercheDialogHtml(
            steps,
            laufend: false,
            modus: "schnell",
            gefundenCount: 1,
            anonymisiertGesendet: "[MANDANT_1] fragt");

        Assert.Contains("recherche-dialog", html);
        Assert.Contains("✅ Suche abgeschlossen", html);
        Assert.Contains("1 Quelle(n) gefunden", html);
        Assert.Contains("such-liste", html);
        Assert.Contains("Anonymisiert gesendet", html);
        Assert.Contains("[MANDANT_1] fragt", html);
    }

    [Fact]
    public void BuildRechercheDialogHtml_RunningState_DoesNotShowAnonymized()
    {
        var html = ChatHtmlRenderer.BuildRechercheDialogHtml(
            "",
            laufend: true,
            modus: "schnell",
            gefundenCount: 0,
            anonymisiertGesendet: null);

        Assert.Contains("Live-Suche l&#228;uft", html);
        Assert.DoesNotContain("Anonymisiert gesendet", html);
    }

    [Fact]
    public void SearchStepRowHtml_RendersConfidencePercent()
    {
        var html = ChatHtmlRenderer.SearchStepRowHtml("suchergebnis", "BGB", "Text", 0.853, null);

        Assert.Contains("85%", html);
        Assert.Contains("such-schritt", html);
        Assert.Contains("such-gefunden", html);
    }
}
