using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class ReviewIntelligenceServiceTests
    {
        private InMemoryTestMovieRepo _movies;
        private InMemoryTestReviewRepo _reviews;
        private ReviewIntelligenceService _service;
        private DateTime _now;

        [TestInitialize]
        public void SetUp()
        {
            _movies = new InMemoryTestMovieRepo();
            _reviews = new InMemoryTestReviewRepo();
            _service = new ReviewIntelligenceService(_reviews, _movies);
            _now = new DateTime(2026, 5, 1);

            _movies.Add(new Movie { Id = 1, Name = "Action Hero", Genre = Genre.Action });
            _movies.Add(new Movie { Id = 2, Name = "Bad Sequel", Genre = Genre.Action });
            _movies.Add(new Movie { Id = 3, Name = "Quiet Drama", Genre = Genre.Drama });
            _movies.Add(new Movie { Id = 4, Name = "Mystery Hit", Genre = Genre.Thriller });
        }

        private Review R(int id, int movieId, int stars, string text, DateTime date,
                         int customerId = 100)
        {
            return new Review
            {
                Id = id,
                MovieId = movieId,
                CustomerId = customerId + id,
                Stars = stars,
                ReviewText = text,
                CreatedDate = date,
                CustomerName = "Cust " + (customerId + id),
                MovieName = "M" + movieId
            };
        }

        // ── Classify ───────────────────────────────────────────────

        [TestMethod]
        public void Classify_PositiveTextAndHighStars_IsAligned()
        {
            var s = _service.Classify(R(1, 1, 5, "amazing and brilliant, loved it", _now));
            Assert.AreEqual("positive", s.TextLabel);
            Assert.AreEqual("positive", s.StarLabel);
            Assert.AreEqual("aligned", s.Alignment);
            Assert.IsTrue(s.TextScore > 0);
            Assert.IsTrue(s.Confidence > 0);
        }

        [TestMethod]
        public void Classify_NegativeTextWithFiveStars_IsMismatch()
        {
            var s = _service.Classify(R(1, 1, 5, "absolutely terrible and boring", _now));
            Assert.AreEqual("negative", s.TextLabel);
            Assert.AreEqual("positive", s.StarLabel);
            Assert.AreEqual("mismatch", s.Alignment);
        }

        [TestMethod]
        public void Classify_NegationFlipsSentiment()
        {
            // "not bad" should not register as negative
            var s = _service.Classify(R(1, 1, 4, "not bad", _now));
            Assert.AreEqual("positive", s.TextLabel);
        }

        [TestMethod]
        public void Classify_EmptyText_IsNeutralAmbiguous()
        {
            var s = _service.Classify(R(1, 1, 3, "", _now));
            Assert.AreEqual("neutral", s.TextLabel);
            Assert.AreEqual("ambiguous", s.Alignment);
            Assert.AreEqual(0.0, s.Confidence);
        }

        [TestMethod]
        public void Classify_NullReview_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.Classify(null));
        }

        // ── Single-movie analysis ─────────────────────────────────

        [TestMethod]
        public void AnalyzeMovie_NoReviews_ReturnsUnknownTier()
        {
            var r = _service.AnalyzeMovie(1, _now);
            Assert.AreEqual(0, r.TotalReviews);
            Assert.AreEqual(ReputationTier.Unknown, r.HealthTier);
            Assert.IsTrue(r.Actions.Any(a => a.Action.Contains("Solicit first")));
        }

        [TestMethod]
        public void AnalyzeMovie_AllPositive_IsHealthy()
        {
            _reviews.Add(R(1, 1, 5, "amazing love it", _now.AddDays(-1)));
            _reviews.Add(R(2, 1, 5, "wonderful and fun", _now.AddDays(-2)));
            _reviews.Add(R(3, 1, 4, "great recommend", _now.AddDays(-3)));
            _reviews.Add(R(4, 1, 5, "fantastic", _now.AddDays(-4)));
            _reviews.Add(R(5, 1, 4, "excellent", _now.AddDays(-5)));

            var r = _service.AnalyzeMovie(1, _now);
            Assert.AreEqual(ReputationTier.Healthy, r.HealthTier);
            Assert.IsTrue(r.HealthScore >= 80);
            Assert.IsTrue(r.Actions.Any(a => a.Action.Contains("crowd favourites")));
        }

        [TestMethod]
        public void AnalyzeMovie_AllNegative_IsCrisisAndDemoted()
        {
            for (int i = 0; i < 6; i++)
                _reviews.Add(R(i + 1, 2, 1, "awful boring waste", _now.AddDays(-i - 1)));

            var r = _service.AnalyzeMovie(2, _now);
            Assert.IsTrue(r.HealthTier == ReputationTier.Crisis
                          || r.HealthTier == ReputationTier.AtRisk);
            Assert.IsTrue(r.Actions.Any(a => a.Priority == "P0" || a.Priority == "P1"));
        }

        [TestMethod]
        public void AnalyzeMovie_SuspiciousHighStarNegativeText_FlagsForModeration()
        {
            _reviews.Add(R(1, 3, 5, "absolutely terrible and boring", _now.AddDays(-1)));
            _reviews.Add(R(2, 3, 5, "awful waste of time", _now.AddDays(-2)));
            _reviews.Add(R(3, 3, 4, "horrible", _now.AddDays(-3)));

            var r = _service.AnalyzeMovie(3, _now);
            Assert.IsTrue(r.SuspiciousReviewIds.Count >= 2);
            Assert.IsTrue(r.Actions.Any(a => a.Priority == "P0"
                                              && a.Action.Contains("suspicious")));
        }

        [TestMethod]
        public void AnalyzeMovie_DecliningTrend_GetsInvestigateAction()
        {
            // Baseline: older positive
            for (int i = 0; i < 4; i++)
                _reviews.Add(R(i + 1, 4, 5, "amazing love", _now.AddDays(-60 - i)));
            // Recent: bad
            for (int i = 0; i < 4; i++)
                _reviews.Add(R(i + 100, 4, 1, "terrible awful", _now.AddDays(-i - 1)));

            var r = _service.AnalyzeMovie(4, _now);
            Assert.AreEqual(ReputationTrend.Declining, r.Trend);
        }

        [TestMethod]
        public void AnalyzeMovie_LowVolume_GetsSolicitAction()
        {
            _reviews.Add(R(1, 1, 5, "great", _now.AddDays(-1)));
            var r = _service.AnalyzeMovie(1, _now);
            Assert.IsTrue(r.Actions.Any(a => a.Action.Contains("additional reviews")));
        }

        [TestMethod]
        public void AnalyzeMovie_UnknownMovie_Throws()
        {
            Assert.ThrowsException<ArgumentException>(
                () => _service.AnalyzeMovie(999, _now));
        }

        // ── Catalogue / playbook ──────────────────────────────────

        [TestMethod]
        public void AnalyzeAll_OrdersWorstFirst()
        {
            // Healthy movie 1
            for (int i = 0; i < 5; i++)
                _reviews.Add(R(i + 1, 1, 5, "amazing", _now.AddDays(-i - 1)));
            // Crisis movie 2
            for (int i = 0; i < 5; i++)
                _reviews.Add(R(i + 100, 2, 1, "terrible waste", _now.AddDays(-i - 1)));

            var all = _service.AnalyzeAll(_now);
            Assert.AreEqual(2, all.Count);
            Assert.IsTrue(all[0].HealthScore <= all[1].HealthScore);
            Assert.AreEqual(2, all[0].MovieId); // worst is movie 2
        }

        [TestMethod]
        public void AnalyzeAll_NoReviews_ReturnsEmpty()
        {
            var all = _service.AnalyzeAll(_now);
            Assert.AreEqual(0, all.Count);
        }

        [TestMethod]
        public void GeneratePlaybook_OrdersP0BeforeP1BeforeP2()
        {
            // Crisis movie -> P0
            for (int i = 0; i < 6; i++)
                _reviews.Add(R(i + 1, 2, 1, "terrible awful waste", _now.AddDays(-i - 1)));
            // Healthy movie -> P2 only
            for (int i = 0; i < 5; i++)
                _reviews.Add(R(i + 100, 1, 5, "amazing fantastic", _now.AddDays(-i - 1)));

            var p = _service.GeneratePlaybook(_now);
            Assert.IsTrue(p.Actions.Count > 0);

            var ranks = p.Actions.Select(a => a.Priority).ToList();
            for (int i = 1; i < ranks.Count; i++)
            {
                int prev = Rank(ranks[i - 1]);
                int cur = Rank(ranks[i]);
                Assert.IsTrue(prev <= cur, $"Actions not sorted by priority: {ranks[i - 1]} then {ranks[i]}");
            }
            Assert.IsTrue(p.CatalogueHealthScore >= 0 && p.CatalogueHealthScore <= 100);
            Assert.IsTrue(p.AtRiskMovies >= 1);
        }

        [TestMethod]
        public void GeneratePlaybook_RespectsMaxActions()
        {
            for (int i = 0; i < 6; i++)
                _reviews.Add(R(i + 1, 2, 1, "terrible awful", _now.AddDays(-i - 1)));

            var svc = new ReviewIntelligenceService(_reviews, _movies,
                new ReviewIntelligenceConfig { MaxPlaybookActions = 1 });
            var p = svc.GeneratePlaybook(_now);
            Assert.AreEqual(1, p.Actions.Count);
        }

        // ── Renderers ─────────────────────────────────────────────

        [TestMethod]
        public void RenderText_IncludesHeaderAndActions()
        {
            for (int i = 0; i < 6; i++)
                _reviews.Add(R(i + 1, 2, 1, "terrible awful", _now.AddDays(-i - 1)));

            var p = _service.GeneratePlaybook(_now);
            var text = _service.RenderText(p);

            StringAssert.Contains(text, "REVIEW INTELLIGENCE");
            StringAssert.Contains(text, "Bad Sequel");
            StringAssert.Contains(text, "[P0]");
        }

        [TestMethod]
        public void RenderMarkdown_HasTablesAndEscapesPipes()
        {
            _movies.Add(new Movie { Id = 99, Name = "Pipe|Title", Genre = Genre.Drama });
            for (int i = 0; i < 6; i++)
                _reviews.Add(R(i + 1, 99, 1, "terrible awful", _now.AddDays(-i - 1)));

            var p = _service.GeneratePlaybook(_now);
            var md = _service.RenderMarkdown(p);

            StringAssert.Contains(md, "# Review Intelligence");
            StringAssert.Contains(md, "| Priority | Movie |");
            // pipe in movie name must be escaped
            StringAssert.Contains(md, "Pipe\\|Title");
        }

        [TestMethod]
        public void RenderText_NullPlaybook_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(() => _service.RenderText(null));
        }

        // ── Constructor guards ───────────────────────────────────

        [TestMethod]
        public void Ctor_NullRepos_Throw()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new ReviewIntelligenceService(null, _movies));
            Assert.ThrowsException<ArgumentNullException>(
                () => new ReviewIntelligenceService(_reviews, null));
        }

        // ── Helpers ───────────────────────────────────────────────

        private static int Rank(string p)
        {
            switch (p) { case "P0": return 0; case "P1": return 1; case "P2": return 2; default: return 3; }
        }

        private class InMemoryTestMovieRepo : IMovieRepository
        {
            private readonly List<Movie> _data = new List<Movie>();
            public void Add(Movie entity) => _data.Add(entity);
            public void Remove(int id) => _data.RemoveAll(m => m.Id == id);
            public IReadOnlyList<Movie> GetAll() => _data.AsReadOnly();
            public Movie GetById(int id) => _data.FirstOrDefault(m => m.Id == id);
            public void Update(Movie entity) { }
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                new List<Movie>().AsReadOnly();
            public Movie GetRandom() => _data.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                _data.AsReadOnly();
        }

        private class InMemoryTestReviewRepo : IReviewRepository
        {
            private readonly List<Review> _data = new List<Review>();
            public void Add(Review entity) => _data.Add(entity);
            public void Remove(int id) => _data.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Review> GetAll() => _data.AsReadOnly();
            public Review GetById(int id) => _data.FirstOrDefault(r => r.Id == id);
            public void Update(Review entity) { }
            public IReadOnlyList<Review> GetByMovie(int movieId) =>
                _data.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Review> GetByCustomer(int customerId) =>
                _data.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public Review GetByCustomerAndMovie(int customerId, int movieId) =>
                _data.FirstOrDefault(r => r.CustomerId == customerId && r.MovieId == movieId);
            public bool HasReviewed(int customerId, int movieId) =>
                _data.Any(r => r.CustomerId == customerId && r.MovieId == movieId);
            public ReviewStats GetMovieStats(int movieId) => new ReviewStats { MovieId = movieId };
            public IReadOnlyList<MovieRating> GetTopRatedMovies(int count, int minReviews = 1) =>
                new List<MovieRating>().AsReadOnly();
            public IReadOnlyList<Review> Search(string query, int? minStars) =>
                _data.AsReadOnly();
        }
    }
}
