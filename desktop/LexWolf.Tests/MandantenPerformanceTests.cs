using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LexWolf.Database;
using Xunit;

namespace LexWolf.Tests
{
    public class MandantenPerformanceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LocalDb _db;

        public MandantenPerformanceTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"lexwolf_mandanten_perf_{Guid.NewGuid():N}.db");
            _db = new LocalDb(_dbPath);
            SeedMandanten(250);
        }

        public void Dispose()
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        private void SeedMandanten(int count)
        {
            for (int i = 1; i <= count; i++)
                _db.UpsertMandant($"m_{i}", $"Mandant {i:D3}", $"AZ-{i:D5}");
        }

        [Fact]
        public void GetMandanten_LaedeNurIdUndName_ProjektionSchnell()
        {
            var sw = Stopwatch.StartNew();
            var mandanten = _db.GetMandanten();
            sw.Stop();

            Assert.True(mandanten.Count >= 250, "Mindestens 250 Mandanten erwartet");
            Assert.All(mandanten, m =>
            {
                Assert.False(string.IsNullOrEmpty(m.Id));
                Assert.False(string.IsNullOrEmpty(m.Name));
            });
            Assert.True(sw.ElapsedMilliseconds < 200, $"Laden der Mandantenliste dauerte {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void SearchMandanten_LiefertGefilterteErgebnisse()
        {
            var ergebnis = _db.SearchMandanten("Mandant 001");
            Assert.Single(ergebnis);
            Assert.Equal("Mandant 001", ergebnis[0].Name);
        }

        [Fact]
        public void SearchMandanten_MitLeeremFilterLiefertAlle()
        {
            var ergebnis = _db.SearchMandanten(null);
            Assert.True(ergebnis.Count >= 250);
        }
    }
}
