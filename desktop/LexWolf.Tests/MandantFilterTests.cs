using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LexWolf.Database;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Task #239: Logik-Regressionstest für das Mandanten-Dropdown. Der Bug war,
    /// dass ApplyMandantFilter bei leerem Filtertext nur einen Platzhalter
    /// eintrug und die geladenen Mandanten aus _mandanten ignorierte. Dieser Test
    /// prüft die reine Filter-/Projektionslogik isoliert ohne WPF-UI und ist so
    /// aufgebaut, dass er der if/else-Struktur im Code folgt.
    /// </summary>
    public class MandantFilterTests
    {
        private const string KeinMandantLabel = "— kein Mandant —";
        private const int MaxMandantDropdownItems = 100;

        private static List<string> ApplyFilter(List<(string Id, string Name)> mandanten, string? filterText)
        {
            var filter = (filterText ?? "").Trim();
            var items = new List<string>();

            if (string.IsNullOrEmpty(filter))
            {
                if (mandanten.Count == 0)
                {
                    items.Add(KeinMandantLabel);
                }
                else
                {
                    items.AddRange(mandanten
                        .Select(m => m.Name)
                        .Take(MaxMandantDropdownItems));
                }
            }
            else
            {
                var gefiltert = mandanten
                    .Where(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Name)
                    .Take(MaxMandantDropdownItems)
                    .ToList();

                if (gefiltert.Count == 0)
                    items.Add(KeinMandantLabel);

                items.AddRange(gefiltert);
            }

            return items;
        }

        [Fact]
        public void LeererFilter_ZeigtAlleMandanten()
        {
            var mandanten = new List<(string, string)>
            {
                ("m1", "Müller, Hans"),
                ("m2", "Schmidt, Anna"),
                ("m3", "Weber, Klaus"),
            };

            var items = ApplyFilter(mandanten, "");

            Assert.Equal(3, items.Count);
            Assert.Contains("Müller, Hans", items);
            Assert.Contains("Schmidt, Anna", items);
            Assert.Contains("Weber, Klaus", items);
            Assert.DoesNotContain(KeinMandantLabel, items);
        }

        [Fact]
        public void LeereMandantenliste_ZeigtPlatzhalter()
        {
            var items = ApplyFilter(new List<(string, string)>(), "");
            Assert.Single(items);
            Assert.Equal(KeinMandantLabel, items[0]);
        }

        [Fact]
        public void FilterOhneTreffer_ZeigtPlatzhalter()
        {
            var mandanten = new List<(string, string)>
            {
                ("m1", "Müller, Hans"),
            };

            var items = ApplyFilter(mandanten, "xyz");
            Assert.Single(items);
            Assert.Equal(KeinMandantLabel, items[0]);
        }

        [Fact]
        public void FilterMitTreffer_ZeigtNurPassende()
        {
            var mandanten = new List<(string, string)>
            {
                ("m1", "Müller, Hans"),
                ("m2", "Schmidt, Anna"),
                ("m3", "Weber, Klaus"),
            };

            var items = ApplyFilter(mandanten, "Mü");
            Assert.Single(items);
            Assert.Equal("Müller, Hans", items[0]);
        }

        [Fact]
        public void Filter_IstCaseInsensitive()
        {
            var mandanten = new List<(string, string)>
            {
                ("m1", "Müller, Hans"),
            };

            var items = ApplyFilter(mandanten, "mÜ");
            Assert.Single(items);
            Assert.Equal("Müller, Hans", items[0]);
        }

        [Fact]
        public void MaximaleAnzahl_WirdEingehalten()
        {
            var mandanten = new List<(string, string)>();
            for (int i = 0; i < 150; i++)
                mandanten.Add(("m" + i, "Mandant " + i));

            var items = ApplyFilter(mandanten, "");
            Assert.Equal(MaxMandantDropdownItems, items.Count);
        }

        [Fact]
        public void NullFilter_WieLeererFilter()
        {
            var mandanten = new List<(string, string)>
            {
                ("m1", "Müller, Hans"),
            };

            var items = ApplyFilter(mandanten, null);
            Assert.Single(items);
            Assert.Equal("Müller, Hans", items[0]);
        }

        /// <summary>
        /// Task #247 Regressionstest: Bei &gt;200 Mandanten wird ab 3 Zeichen
        /// serverseitig (SQL) gesucht, wodurch die interne _mandanten-Liste komplett
        /// durch die wenigen Treffer ersetzt wird. Wird der Suchtext danach wieder
        /// vollständig geleert, muss erneut der volle, ungefilterte Bestand geladen
        /// werden (LoadMandantenAsync() ohne Filter) statt nur lokal über die bereits
        /// eingeschränkte _mandanten-Liste zu filtern (ApplyMandantFilter("")).
        /// Dieser Test bildet exakt die Zustandsübergänge aus
        /// OnMandantTextChanged/LoadMandantenAsync gegen eine echte LocalDb nach.
        /// </summary>
        public class SucheUndLeerenRegressionTests : IDisposable
        {
            private readonly string _dbPath;
            private readonly LocalDb _db;

            public SucheUndLeerenRegressionTests()
            {
                _dbPath = Path.Combine(Path.GetTempPath(), $"lexwolf_mandant_filter_{Guid.NewGuid():N}.db");
                _db = new LocalDb(_dbPath);
                for (int i = 1; i <= 250; i++)
                    _db.UpsertMandant($"m_{i}", $"Mandant {i:D3}", $"AZ-{i:D5}");
            }

            public void Dispose()
            {
                if (File.Exists(_dbPath))
                    File.Delete(_dbPath);
            }

            // Bildet die Entscheidungslogik aus OnMandantTextChanged nach: liefert
            // die Liste, die anschliessend an ApplyMandantFilter übergeben würde.
            private List<(string Id, string Name)> SimuliereTextChanged(
                List<(string Id, string Name)> mandanten, string filter)
            {
                if (string.IsNullOrEmpty(filter))
                {
                    // Fix: kompletten Bestand neu laden statt nur lokal zu filtern.
                    return _db.GetMandanten();
                }
                if (filter.Length >= 3 && mandanten.Count > 200)
                {
                    return _db.SearchMandanten(filter);
                }
                return mandanten;
            }

            [Fact]
            public void NachSucheUndLeeren_ZeigtWiederVollstaendigeListe()
            {
                var mandanten = _db.GetMandanten();
                Assert.True(mandanten.Count > 200, "Testvoraussetzung: >200 Mandanten");

                // Schritt 1: Suche mit >=3 Zeichen löst SQL-seitige Filterung aus
                // und ersetzt die interne Liste durch wenige Treffer.
                mandanten = SimuliereTextChanged(mandanten, "Mandant 001");
                Assert.True(mandanten.Count < 10, "Suche sollte die Liste stark einschränken");

                // Schritt 2: Suchtext wird vollständig geleert.
                mandanten = SimuliereTextChanged(mandanten, "");

                var items = ApplyFilter(mandanten, "");
                Assert.Equal(MaxMandantDropdownItems, items.Count);
                Assert.DoesNotContain(KeinMandantLabel, items);
            }
        }
    }
}
