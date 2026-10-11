using System;
using System.IO;
using LexWolf.Database;
using Xunit;

namespace LexWolf.Tests
{
    public class LocalDbFeatureWunschTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LocalDb _db;

        public LocalDbFeatureWunschTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"lexwolf_fw_test_{Guid.NewGuid():N}.db");
            _db = new LocalDb(_dbPath);
        }

        public void Dispose()
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Fact]
        public void ClearFeatureWunschHistory_EntferntAlleEintraege()
        {
            _db.AddFeatureWunschMessage("user", "Ich haette gerne einen Dark Mode.");
            _db.AddFeatureWunschMessage("assistant", "Klingt gut, was genau meinst du?");
            Assert.Equal(2, _db.GetFeatureWunschHistory().Count);

            _db.ClearFeatureWunschHistory();

            Assert.Empty(_db.GetFeatureWunschHistory());
        }
    }
}
