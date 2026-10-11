using System;
using System.IO;
using LexWolf.Database;
using LexWolf.Services;
using Xunit;

namespace LexWolf.Tests
{
    /// <summary>
    /// Deckt genau den Bug ab, der in Verifikationsversuch 2 von Task #236 gefunden wurde:
    /// "Später erinnern" darf ein Feature nicht dauerhaft unterdrücken, nur "restart" darf das.
    /// </summary>
    public class FeatureNotificationStoreTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LocalDb _db;
        private readonly FeatureNotificationStore _store;

        public FeatureNotificationStoreTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"lexwolf_fns_test_{Guid.NewGuid():N}.db");
            _db = new LocalDb(_dbPath);
            _store = new FeatureNotificationStore(_db);
        }

        public void Dispose()
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }

        [Fact]
        public void UnbekanntesFeature_IstNichtGesehen()
        {
            Assert.False(_store.IsAlreadySeen(42));
        }

        [Fact]
        public void SpaeterErinnern_UnterdrueicktToastNichtDauerhaft()
        {
            _store.MarkAsSeen(42, "later");

            Assert.False(_store.IsAlreadySeen(42));
        }

        [Fact]
        public void Neustart_UnterdrueicktToastDauerhaft()
        {
            _store.MarkAsSeen(42, "restart");

            Assert.True(_store.IsAlreadySeen(42));
        }

        [Fact]
        public void SpaeterErinnernGefolgtVonNeustart_MarkiertAlsGesehen()
        {
            _store.MarkAsSeen(42, "later");
            Assert.False(_store.IsAlreadySeen(42));

            _store.MarkAsSeen(42, "restart");
            Assert.True(_store.IsAlreadySeen(42));
        }

        [Fact]
        public void MarkAsSeen_BeeinflusstAndereMilestonesNicht()
        {
            _store.MarkAsSeen(1, "restart");

            Assert.True(_store.IsAlreadySeen(1));
            Assert.False(_store.IsAlreadySeen(2));
        }
    }
}
