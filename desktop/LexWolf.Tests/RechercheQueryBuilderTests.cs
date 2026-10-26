using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// T#310: Schriftsatz-Erstellung an Agentic-RAG-Suche anschließen. RechercheQueryBuilder
    /// baut aus Art + Anweisung die Suchanfrage, mit der der SchriftsatzDialog vor dem
    /// Entwurf die ReAct-Suche (GET /api/search/stream) anstößt.
    /// </summary>
    public class RechercheQueryBuilderTests
    {
        [Fact]
        public void Build_ArtUndAnweisung_EnthaeltBeide()
        {
            var query = RechercheQueryBuilder.Build("Kündigungsschutzklage", "fristlose Kündigung wegen Diebstahls");

            Assert.Contains("Kündigungsschutzklage", query);
            Assert.Contains("fristlose Kündigung wegen Diebstahls", query);
        }

        [Fact]
        public void Build_NurArt_OhneAnweisung_LiefertNurArt()
        {
            var query = RechercheQueryBuilder.Build("Klageschrift", "");

            Assert.Equal("Klageschrift", query);
        }

        [Fact]
        public void Build_NurAnweisung_OhneArt_LiefertNurAnweisung()
        {
            var query = RechercheQueryBuilder.Build("", "Unterhaltsanspruch nach Trennung");

            Assert.Equal("Unterhaltsanspruch nach Trennung", query);
        }

        [Fact]
        public void Build_BeideLeer_LiefertLeerenString()
        {
            Assert.Equal("", RechercheQueryBuilder.Build("", "  "));
        }

        [Fact]
        public void Build_SehrLangeAnweisung_WirdGekuerzt()
        {
            var lang = new string('a', 500);
            var query = RechercheQueryBuilder.Build("Klageschrift", lang);

            Assert.True(query.Length <= 300);
        }
    }
}
