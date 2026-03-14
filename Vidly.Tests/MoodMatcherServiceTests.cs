using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class MoodMatcherServiceTests
    {
        #region Test Helpers

        private class TestMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();

            public void Add(Movie movie) { _movies[movie.Id] = movie; }
            public Movie GetById(int id) => _movies.TryGetValue(id, out var m) ? m : null;
            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList().AsReadOnly();
            public void Update(Movie movie) { _movies[movie.Id] = movie; }
            public void Remove(int id) { _movies.Remove(id); }
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) => new List<Movie>().AsReadOnly();
            public Movie GetRandom() => _movies.Values.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) => new List<Movie>().AsReadOnly();
        }

        private TestMovieRepository _movieRepo;
        private MoodMatcherService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new TestMovieRepository();
            _service = new MoodMatcherService(_movieRepo);
        }

        private Movie AddMovie(int id, string name, Genre genre, int rating = 4)
        {
            var m = new Movie { Id = id, Name = name, Genre = genre, Rating = rating };
            _movieRepo.Add(m);
            return m;
        }

        private void SeedMovies()
        {
            AddMovie(1, "Die Hard", Genre.Action, 5);
            AddMovie(2, "Airplane", Genre.Comedy, 4);
            AddMovie(3, "The Notebook", Genre.Romance, 4);
            AddMovie(4, "Alien", Genre.Horror, 5);
            AddMovie(5, "Interstellar", Genre.SciFi, 5);
            AddMovie(6, "Toy Story", Genre.Animation, 5);
            AddMovie(7, "Se7en", Genre.Thriller, 5);
            AddMovie(8, "Schindler's List", Genre.Drama, 5);
            AddMovie(9, "Planet Earth", Genre.Documentary, 4);
            AddMovie(10, "Indiana Jones", Genre.Adventure, 4);
        }

        #endregion

        // -------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRepo_Throws()
        {
            new MoodMatcherService(null);
        }

        // -------------------------------------------------------------------
        // GetMappings
        // -------------------------------------------------------------------

        [TestMethod]
        public void GetMappings_Happy_ReturnsComedyFirst()
        {
            var mappings = _service.GetMappings(Mood.Happy);
            Assert.IsTrue(mappings.Count > 0);
            Assert.AreEqual(Genre.Comedy, mappings[0].Genre);
            Assert.AreEqual(1.0, mappings[0].Affinity);
        }

        [TestMethod]
        public void GetMappings_OrderedByAffinityDescending()
        {
            var mappings = _service.GetMappings(Mood.Excited);
            for (int i = 1; i < mappings.Count; i++)
                Assert.IsTrue(mappings[i - 1].Affinity >= mappings[i].Affinity);
        }

        [TestMethod]
        public void GetMappings_AllMoodsReturnResults()
        {
            foreach (Mood mood in Enum.GetValues(typeof(Mood)))
            {
                var mappings = _service.GetMappings(mood);
                Assert.IsTrue(mappings.Count > 0, $"Mood {mood} should have mappings");
            }
        }

        // -------------------------------------------------------------------
        // GetMoodsForGenre
        // -------------------------------------------------------------------

        [TestMethod]
        public void GetMoodsForGenre_Comedy_ReturnsMultipleMoods()
        {
            var moods = _service.GetMoodsForGenre(Genre.Comedy);
            Assert.IsTrue(moods.Count >= 3); // Happy, Stressed, Relaxed, etc.
        }

        [TestMethod]
        public void GetMoodsForGenre_OrderedByAffinityDesc()
        {
            var moods = _service.GetMoodsForGenre(Genre.Action);
            for (int i = 1; i < moods.Count; i++)
                Assert.IsTrue(moods[i - 1].Affinity >= moods[i].Affinity);
        }

        // -------------------------------------------------------------------
        // Recommend
        // -------------------------------------------------------------------

        [TestMethod]
        public void Recommend_Happy_ReturnsComediesFirst()
        {
            SeedMovies();
            var recs = _service.Recommend(Mood.Happy);
            Assert.IsTrue(recs.Count > 0);
            // Comedy has affinity 1.0 for Happy, so it should score highest
            Assert.AreEqual(Genre.Comedy, recs[0].MatchedGenre);
        }

        [TestMethod]
        public void Recommend_Scared_ReturnsHorrorFirst()
        {
            SeedMovies();
            var recs = _service.Recommend(Mood.Scared);
            Assert.AreEqual(Genre.Horror, recs[0].MatchedGenre);
        }

        [TestMethod]
        public void Recommend_RespectsLimit()
        {
            SeedMovies();
            var recs = _service.Recommend(Mood.Bored, limit: 3);
            Assert.IsTrue(recs.Count <= 3);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Recommend_ZeroLimit_Throws()
        {
            _service.Recommend(Mood.Happy, limit: 0);
        }

        [TestMethod]
        public void Recommend_NoMovies_ReturnsEmpty()
        {
            var recs = _service.Recommend(Mood.Happy);
            Assert.AreEqual(0, recs.Count);
        }

        [TestMethod]
        public void Recommend_MovieWithoutGenre_Skipped()
        {
            _movieRepo.Add(new Movie { Id = 1, Name = "Unknown", Genre = null, Rating = 5 });
            var recs = _service.Recommend(Mood.Happy);
            Assert.AreEqual(0, recs.Count);
        }

        [TestMethod]
        public void Recommend_CombinedScore_FactorsRating()
        {
            AddMovie(1, "Bad Comedy", Genre.Comedy, 1);
            AddMovie(2, "Great Comedy", Genre.Comedy, 5);
            var recs = _service.Recommend(Mood.Happy);
            Assert.AreEqual(2, recs.Count);
            Assert.AreEqual("Great Comedy", recs[0].Movie.Name);
        }

        [TestMethod]
        public void Recommend_ScoresBetweenZeroAndOne()
        {
            SeedMovies();
            var recs = _service.Recommend(Mood.Excited);
            foreach (var r in recs)
            {
                Assert.IsTrue(r.CombinedScore >= 0 && r.CombinedScore <= 1.0);
                Assert.IsTrue(r.MoodScore >= 0 && r.MoodScore <= 1.0);
            }
        }

        // -------------------------------------------------------------------
        // RecommendBlend
        // -------------------------------------------------------------------

        [TestMethod]
        public void RecommendBlend_TwoMoods_BlendScores()
        {
            SeedMovies();
            var blend = new Dictionary<Mood, double>
            {
                [Mood.Happy] = 0.5,
                [Mood.Scared] = 0.5
            };
            var recs = _service.RecommendBlend(blend);
            Assert.IsTrue(recs.Count > 0);
            // Should include both comedies and horror
            var genres = recs.Select(r => r.MatchedGenre).Distinct().ToList();
            Assert.IsTrue(genres.Contains(Genre.Comedy) || genres.Contains(Genre.Horror));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecommendBlend_Empty_Throws()
        {
            _service.RecommendBlend(new Dictionary<Mood, double>());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecommendBlend_Null_Throws()
        {
            _service.RecommendBlend(null);
        }

        [TestMethod]
        public void RecommendBlend_NormalisesWeights()
        {
            SeedMovies();
            var blend = new Dictionary<Mood, double>
            {
                [Mood.Excited] = 10.0,
                [Mood.Relaxed] = 10.0
            };
            var recs = _service.RecommendBlend(blend);
            foreach (var r in recs)
                Assert.IsTrue(r.CombinedScore <= 1.0);
        }

        // -------------------------------------------------------------------
        // LogMood / GetMoodHistory / GetCurrentMood
        // -------------------------------------------------------------------

        [TestMethod]
        public void LogMood_AddsEntry()
        {
            var entry = _service.LogMood(1, Mood.Happy, "Feeling great");
            Assert.AreEqual(1, entry.CustomerId);
            Assert.AreEqual(Mood.Happy, entry.Mood);
            Assert.AreEqual("Feeling great", entry.Note);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void LogMood_InvalidCustomer_Throws()
        {
            _service.LogMood(0, Mood.Happy);
        }

        [TestMethod]
        public void GetMoodHistory_ReturnsChronologicalDesc()
        {
            _service.LogMood(1, Mood.Happy);
            _service.LogMood(1, Mood.Sad);
            _service.LogMood(1, Mood.Excited);
            var history = _service.GetMoodHistory(1);
            Assert.AreEqual(3, history.Count);
            Assert.AreEqual(Mood.Excited, history[0].Mood); // most recent
        }

        [TestMethod]
        public void GetMoodHistory_RespectsLimit()
        {
            _service.LogMood(1, Mood.Happy);
            _service.LogMood(1, Mood.Sad);
            _service.LogMood(1, Mood.Excited);
            var history = _service.GetMoodHistory(1, limit: 2);
            Assert.AreEqual(2, history.Count);
        }

        [TestMethod]
        public void GetMoodHistory_IsolatedByCustomer()
        {
            _service.LogMood(1, Mood.Happy);
            _service.LogMood(2, Mood.Sad);
            Assert.AreEqual(1, _service.GetMoodHistory(1).Count);
            Assert.AreEqual(1, _service.GetMoodHistory(2).Count);
        }

        [TestMethod]
        public void GetCurrentMood_ReturnsLatest()
        {
            _service.LogMood(1, Mood.Happy);
            _service.LogMood(1, Mood.Sad);
            var current = _service.GetCurrentMood(1);
            Assert.AreEqual(Mood.Sad, current.Mood);
        }

        [TestMethod]
        public void GetCurrentMood_NoHistory_ReturnsNull()
        {
            Assert.IsNull(_service.GetCurrentMood(99));
        }

        // -------------------------------------------------------------------
        // RecommendForCustomer
        // -------------------------------------------------------------------

        [TestMethod]
        public void RecommendForCustomer_UsesCurrentMood()
        {
            SeedMovies();
            _service.LogMood(1, Mood.Scared);
            var recs = _service.RecommendForCustomer(1);
            Assert.IsTrue(recs.Count > 0);
            Assert.AreEqual(Genre.Horror, recs[0].MatchedGenre);
        }

        [TestMethod]
        public void RecommendForCustomer_NoMood_ReturnsEmpty()
        {
            SeedMovies();
            var recs = _service.RecommendForCustomer(99);
            Assert.AreEqual(0, recs.Count);
        }

        // -------------------------------------------------------------------
        // GetMoodStats
        // -------------------------------------------------------------------

        [TestMethod]
        public void GetMoodStats_CalculatesCorrectly()
        {
            _service.LogMood(1, Mood.Happy);
            _service.LogMood(1, Mood.Happy);
            _service.LogMood(1, Mood.Sad);

            var stats = _service.GetMoodStats(1);
            Assert.AreEqual(3, stats.TotalEntries);
            Assert.AreEqual(Mood.Happy, stats.MostFrequentMood);
            Assert.AreEqual(2, stats.MoodCounts[Mood.Happy]);
            Assert.AreEqual(1, stats.MoodCounts[Mood.Sad]);
        }

        [TestMethod]
        public void GetMoodStats_EmptyHistory_ReturnsZero()
        {
            var stats = _service.GetMoodStats(99);
            Assert.AreEqual(0, stats.TotalEntries);
            Assert.IsNull(stats.MostFrequentMood);
        }

        [TestMethod]
        public void GetMoodStats_Percentages_SumTo100()
        {
            _service.LogMood(1, Mood.Happy);
            _service.LogMood(1, Mood.Sad);
            _service.LogMood(1, Mood.Excited);
            _service.LogMood(1, Mood.Excited);

            var stats = _service.GetMoodStats(1);
            var totalPct = stats.MoodPercentages.Values.Sum();
            Assert.AreEqual(100.0, totalPct, 0.1);
        }

        // -------------------------------------------------------------------
        // GetMoodTrends
        // -------------------------------------------------------------------

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetMoodTrends_InvalidRange_Throws()
        {
            _service.GetMoodTrends(1, DateTime.Today, DateTime.Today.AddDays(-1));
        }

        [TestMethod]
        public void GetMoodTrends_ReturnsEmpty_NoEntries()
        {
            var trends = _service.GetMoodTrends(1, DateTime.Today.AddDays(-7), DateTime.Today);
            Assert.AreEqual(0, trends.Count);
        }

        // -------------------------------------------------------------------
        // GetAllMappings
        // -------------------------------------------------------------------

        [TestMethod]
        public void GetAllMappings_ReturnsAll12Moods()
        {
            var mappings = _service.GetAllMappings();
            var moods = mappings.Select(m => m.Mood).Distinct().ToList();
            Assert.AreEqual(12, moods.Count);
        }

        // -------------------------------------------------------------------
        // SuggestMoodForGenre
        // -------------------------------------------------------------------

        [TestMethod]
        public void SuggestMoodForGenre_Horror_ReturnScared()
        {
            var mood = _service.SuggestMoodForGenre(Genre.Horror);
            Assert.AreEqual(Mood.Scared, mood);
        }

        [TestMethod]
        public void SuggestMoodForGenre_Romance_ReturnRomantic()
        {
            var mood = _service.SuggestMoodForGenre(Genre.Romance);
            Assert.AreEqual(Mood.Romantic, mood);
        }

        [TestMethod]
        public void SuggestMoodForGenre_Action_ReturnExcited()
        {
            var mood = _service.SuggestMoodForGenre(Genre.Action);
            Assert.AreEqual(Mood.Excited, mood);
        }
    }
}
