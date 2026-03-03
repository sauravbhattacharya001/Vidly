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
    public class RatingEngineServiceTests
    {
        private InMemoryReviewRepository _reviewRepo;
        private InMemoryMovieRepository _movieRepo;
        private RatingEngineService _engine;

        [TestInitialize]
        public void Setup()
        {
            _reviewRepo = new InMemoryReviewRepository();
            _movieRepo = new InMemoryMovieRepository();
            _engine = new RatingEngineService(_reviewRepo, _movieRepo);
        }

        private Movie AddMovie(int id, string name, Genre genre = Genre.Action)
        {
            var movie = new Movie { Id = id, Name = name, Genre = genre };
            _movieRepo.Add(movie);
            return movie;
        }

        private Review AddReview(int movieId, int customerId, int stars, DateTime? date = null)
        {
            var review = new Review
            {
                Id = _reviewRepo.GetAll().Count + 1,
                MovieId = movieId,
                CustomerId = customerId,
                Stars = stars,
                CreatedDate = date ?? DateTime.Now
            };
            _reviewRepo.Add(review);
            return review;
        }

        // -- Constructor --

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullReviewRepo_Throws()
        {
            new RatingEngineService(null, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RatingEngineService(_reviewRepo, null);
        }

        // -- GetBayesianRating --

        [TestMethod]
        public void GetBayesianRating_UnknownMovie_ReturnsNull()
        {
            var result = _engine.GetBayesianRating(999);
            Assert.IsNull(result);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetBayesianRating_MinVotesZero_Throws()
        {
            AddMovie(1, "Test");
            _engine.GetBayesianRating(1, minVotes: 0);
        }

        [TestMethod]
        public void GetBayesianRating_NoReviews_ReturnsGlobalMean()
        {
            AddMovie(1, "Unreviewed Movie");
            AddMovie(2, "Other Movie");
            AddReview(2, 1, 4);
            AddReview(2, 2, 5);

            var result = _engine.GetBayesianRating(1);
            Assert.AreEqual(4.5, result.GlobalMean, 0.01);
            Assert.AreEqual(4.5, result.BayesianWeightedRating, 0.01);
            Assert.AreEqual(0, result.ArithmeticMean);
            Assert.AreEqual(0, result.VoteCount);
            Assert.IsFalse(result.MeetsThreshold);
        }

        [TestMethod]
        public void GetBayesianRating_FewReviews_PullsTowardGlobalMean()
        {
            AddMovie(1, "Cult Classic");
            AddMovie(2, "Mainstream");

            AddReview(1, 1, 5);

            for (int i = 2; i <= 11; i++)
                AddReview(2, i, 3);

            var cult = _engine.GetBayesianRating(1);
            var mainstream = _engine.GetBayesianRating(2);

            Assert.AreEqual(5.0, cult.ArithmeticMean, 0.01);
            Assert.IsTrue(cult.BayesianWeightedRating < 5.0,
                "Bayesian should pull single-vote 5.0 toward global mean");

            Assert.IsTrue(
                Math.Abs(mainstream.BayesianWeightedRating - mainstream.ArithmeticMean)
                < Math.Abs(cult.BayesianWeightedRating - cult.ArithmeticMean),
                "More votes = less Bayesian pull");
        }

        [TestMethod]
        public void GetBayesianRating_ManyReviews_MeetsThreshold()
        {
            AddMovie(1, "Popular Movie");
            for (int i = 1; i <= 10; i++)
                AddReview(1, i, 4);

            var result = _engine.GetBayesianRating(1, minVotes: 5);
            Assert.IsTrue(result.MeetsThreshold);
            Assert.AreEqual(10, result.VoteCount);
        }

        [TestMethod]
        public void GetBayesianRating_ReturnsCorrectMovieInfo()
        {
            AddMovie(1, "Action Hero", Genre.Action);
            AddReview(1, 1, 4);

            var result = _engine.GetBayesianRating(1);
            Assert.AreEqual(1, result.MovieId);
            Assert.AreEqual("Action Hero", result.MovieName);
            Assert.AreEqual(Genre.Action, result.Genre);
        }

        [TestMethod]
        public void GetBayesianRating_HighMinVotes_StrongPull()
        {
            AddMovie(1, "Test");
            AddReview(1, 1, 5);
            AddMovie(2, "Filler");
            AddReview(2, 2, 3);

            var lowM = _engine.GetBayesianRating(1, minVotes: 1);
            var highM = _engine.GetBayesianRating(1, minVotes: 100);

            Assert.IsTrue(highM.BayesianWeightedRating < lowM.BayesianWeightedRating,
                "Higher minVotes should pull more toward global mean");
        }

        // -- GetRankedMovies --

        [TestMethod]
        public void GetRankedMovies_EmptyCatalog_ReturnsEmpty()
        {
            var result = _engine.GetRankedMovies();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetRankedMovies_RanksHigherBayesianFirst()
        {
            AddMovie(1, "Many Good Reviews");
            AddMovie(2, "Few Perfect Reviews");

            for (int i = 1; i <= 20; i++)
                AddReview(1, i, 4);

            AddReview(2, 100, 5);

            var ranked = _engine.GetRankedMovies();
            Assert.AreEqual(2, ranked.Count);
            Assert.AreEqual(1, ranked[0].MovieId);
        }

        [TestMethod]
        public void GetRankedMovies_RespectsLimit()
        {
            for (int i = 1; i <= 10; i++)
            {
                AddMovie(i, "Movie " + i);
                AddReview(i, i, 5 - (i % 5));
            }

            var ranked = _engine.GetRankedMovies(limit: 3);
            Assert.AreEqual(3, ranked.Count);
        }

        [TestMethod]
        public void GetRankedMovies_BreaksTiesByVoteCount()
        {
            AddMovie(1, "Movie A");
            AddMovie(2, "Movie B");

            for (int i = 1; i <= 5; i++)
                AddReview(1, i, 4);
            for (int i = 10; i <= 19; i++)
                AddReview(2, i, 4);

            var ranked = _engine.GetRankedMovies();
            Assert.AreEqual(2, ranked[0].MovieId);
        }

        [TestMethod]
        public void GetRankedMovies_IncludesUnreviewedMovies()
        {
            AddMovie(1, "Reviewed");
            AddMovie(2, "Unreviewed");
            AddReview(1, 1, 5);

            var ranked = _engine.GetRankedMovies();
            Assert.AreEqual(2, ranked.Count);
        }

        // -- GetGenreRankings --

        [TestMethod]
        public void GetGenreRankings_FiltersToGenre()
        {
            AddMovie(1, "Action A", Genre.Action);
            AddMovie(2, "Comedy B", Genre.Comedy);
            AddMovie(3, "Action C", Genre.Action);

            AddReview(1, 1, 5);
            AddReview(2, 1, 5);
            AddReview(3, 1, 3);

            var actionRanked = _engine.GetGenreRankings(Genre.Action);
            Assert.AreEqual(2, actionRanked.Count);
            Assert.IsTrue(actionRanked.All(r => r.Genre == Genre.Action));
        }

        [TestMethod]
        public void GetGenreRankings_RespectsLimit()
        {
            for (int i = 1; i <= 5; i++)
            {
                AddMovie(i, "Action " + i, Genre.Action);
                AddReview(i, 1, 5);
            }

            var ranked = _engine.GetGenreRankings(Genre.Action, limit: 2);
            Assert.AreEqual(2, ranked.Count);
        }

        [TestMethod]
        public void GetGenreRankings_EmptyGenre_ReturnsEmpty()
        {
            AddMovie(1, "Action Only", Genre.Action);
            AddReview(1, 1, 5);

            var comedyRanked = _engine.GetGenreRankings(Genre.Comedy);
            Assert.AreEqual(0, comedyRanked.Count);
        }

        // -- GetAllGenreRankings --

        [TestMethod]
        public void GetAllGenreRankings_GroupsByGenre()
        {
            AddMovie(1, "Action A", Genre.Action);
            AddMovie(2, "Comedy B", Genre.Comedy);
            AddReview(1, 1, 5);
            AddReview(2, 1, 4);

            var all = _engine.GetAllGenreRankings();
            Assert.IsTrue(all.ContainsKey(Genre.Action));
            Assert.IsTrue(all.ContainsKey(Genre.Comedy));
            Assert.IsFalse(all.ContainsKey(Genre.Horror));
        }

        [TestMethod]
        public void GetAllGenreRankings_RespectsTopPerGenre()
        {
            for (int i = 1; i <= 5; i++)
            {
                AddMovie(i, "Action " + i, Genre.Action);
                AddReview(i, 1, 5);
            }

            var all = _engine.GetAllGenreRankings(topPerGenre: 2);
            Assert.AreEqual(2, all[Genre.Action].Count);
        }

        // -- GetTrendingScore --

        [TestMethod]
        public void GetTrendingScore_UnknownMovie_ReturnsNull()
        {
            Assert.IsNull(_engine.GetTrendingScore(999));
        }

        [TestMethod]
        public void GetTrendingScore_NoReviews_ReturnsZeroScore()
        {
            AddMovie(1, "No Reviews");
            var result = _engine.GetTrendingScore(1);
            Assert.AreEqual(0, result.TrendingRating);
            Assert.AreEqual(0, result.Score);
            Assert.AreEqual(0, result.TotalReviews);
            Assert.AreEqual(0, result.RecentReviews);
        }

        [TestMethod]
        public void GetTrendingScore_RecentReviews_HigherWeight()
        {
            AddMovie(1, "Recently Reviewed");
            AddMovie(2, "Old Reviews");

            var now = new DateTime(2026, 3, 1);
            AddReview(1, 1, 5, now.AddDays(-1));
            AddReview(2, 2, 5, now.AddDays(-90));

            var recent = _engine.GetTrendingScore(1, asOf: now);
            var old = _engine.GetTrendingScore(2, asOf: now);

            Assert.IsTrue(recent.TrendingRating > old.TrendingRating,
                "Recent review should have higher trending rating");
        }

        [TestMethod]
        public void GetTrendingScore_CountsRecentReviews()
        {
            AddMovie(1, "Active Movie");
            var now = new DateTime(2026, 3, 1);

            for (int i = 1; i <= 5; i++)
                AddReview(1, i, 4, now.AddDays(-i));

            for (int i = 6; i <= 8; i++)
                AddReview(1, i, 4, now.AddDays(-60));

            var result = _engine.GetTrendingScore(1, asOf: now);
            Assert.AreEqual(8, result.TotalReviews);
            Assert.AreEqual(5, result.RecentReviews);
        }

        [TestMethod]
        public void GetTrendingScore_ScoreRewardsActivityAndRating()
        {
            AddMovie(1, "Popular Good");
            AddMovie(2, "Popular Bad");

            var now = new DateTime(2026, 3, 1);

            for (int i = 1; i <= 10; i++)
                AddReview(1, i, 5, now.AddDays(-i));
            for (int i = 11; i <= 20; i++)
                AddReview(2, i, 1, now.AddDays(-(i - 10)));

            var good = _engine.GetTrendingScore(1, asOf: now);
            var bad = _engine.GetTrendingScore(2, asOf: now);

            Assert.IsTrue(good.Score > bad.Score,
                "Higher-rated movie should have higher trending score");
        }

        [TestMethod]
        public void GetTrendingScore_IncludesHalfLife()
        {
            AddMovie(1, "Test");
            AddReview(1, 1, 5);

            var result = _engine.GetTrendingScore(1);
            Assert.AreEqual(RatingEngineService.TrendingHalfLifeDays, result.HalfLifeDays);
        }

        [TestMethod]
        public void GetTrendingScore_FutureDatedReview_TreatedAsNow()
        {
            AddMovie(1, "Future");
            var now = new DateTime(2026, 3, 1);
            AddReview(1, 1, 5, now.AddDays(10));

            var result = _engine.GetTrendingScore(1, asOf: now);
            Assert.AreEqual(5.0, result.TrendingRating, 0.01);
        }

        // -- GetTrendingMovies --

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetTrendingMovies_ZeroCount_Throws()
        {
            _engine.GetTrendingMovies(0);
        }

        [TestMethod]
        public void GetTrendingMovies_ReturnsTopByScore()
        {
            var now = new DateTime(2026, 3, 1);

            AddMovie(1, "Hot Movie");
            AddMovie(2, "Lukewarm Movie");

            for (int i = 1; i <= 10; i++)
                AddReview(1, i, 5, now.AddDays(-1));
            AddReview(2, 20, 3, now.AddDays(-60));

            var trending = _engine.GetTrendingMovies(5, asOf: now);
            Assert.IsTrue(trending.Count >= 1);
            Assert.AreEqual(1, trending[0].MovieId);
        }

        [TestMethod]
        public void GetTrendingMovies_ExcludesUnreviewedMovies()
        {
            AddMovie(1, "Has Reviews");
            AddMovie(2, "No Reviews");
            AddReview(1, 1, 4);

            var trending = _engine.GetTrendingMovies();
            Assert.AreEqual(1, trending.Count);
            Assert.AreEqual(1, trending[0].MovieId);
        }

        // -- GetControversyScore --

        [TestMethod]
        public void GetControversyScore_UnknownMovie_ReturnsNull()
        {
            Assert.IsNull(_engine.GetControversyScore(999));
        }

        [TestMethod]
        public void GetControversyScore_OneReview_InsufficientData()
        {
            AddMovie(1, "Lonely Movie");
            AddReview(1, 1, 5);

            var result = _engine.GetControversyScore(1);
            Assert.AreEqual(0, result.Score);
            Assert.AreEqual("Insufficient Data", result.Label);
            Assert.AreEqual(1, result.ReviewCount);
        }

        [TestMethod]
        public void GetControversyScore_UnanimousRating_Consensus()
        {
            AddMovie(1, "Everyone Agrees");
            for (int i = 1; i <= 10; i++)
                AddReview(1, i, 4);

            var result = _engine.GetControversyScore(1);
            Assert.AreEqual("Consensus", result.Label);
            Assert.AreEqual(0, result.StandardDeviation, 0.01);
            Assert.AreEqual(0, result.Polarization, 0.01);
        }

        [TestMethod]
        public void GetControversyScore_PolarizedRatings_Controversial()
        {
            AddMovie(1, "Love It or Hate It");
            for (int i = 1; i <= 10; i++)
                AddReview(1, i, i <= 5 ? 1 : 5);

            var result = _engine.GetControversyScore(1);
            Assert.IsTrue(result.Score >= 50,
                "Equal split of 1s and 5s should be controversial. Score: " + result.Score);
            Assert.IsTrue(result.StandardDeviation > 1.5);
            Assert.AreEqual(100.0, result.Polarization, 0.01);
        }

        [TestMethod]
        public void GetControversyScore_HasDistribution()
        {
            AddMovie(1, "Mixed Bag");
            AddReview(1, 1, 1);
            AddReview(1, 2, 3);
            AddReview(1, 3, 5);

            var result = _engine.GetControversyScore(1);
            Assert.IsNotNull(result.Distribution);
            Assert.AreEqual(5, result.Distribution.Length);
            Assert.AreEqual(1, result.Distribution[0]);
            Assert.AreEqual(0, result.Distribution[1]);
            Assert.AreEqual(1, result.Distribution[2]);
            Assert.AreEqual(0, result.Distribution[3]);
            Assert.AreEqual(1, result.Distribution[4]);
        }

        [TestMethod]
        public void GetControversyScore_ScoreCappedAt100()
        {
            AddMovie(1, "Extreme Split");
            for (int i = 1; i <= 50; i++)
                AddReview(1, i, i <= 25 ? 1 : 5);

            var result = _engine.GetControversyScore(1);
            Assert.IsTrue(result.Score <= 100);
        }

        [TestMethod]
        public void GetControversyScore_MeanRatingReflectsAverage()
        {
            AddMovie(1, "Test");
            AddReview(1, 1, 2);
            AddReview(1, 2, 4);

            var result = _engine.GetControversyScore(1);
            Assert.AreEqual(3.0, result.MeanRating, 0.01);
        }

        [TestMethod]
        public void GetControversyScore_NoReviews_InsufficientData()
        {
            AddMovie(1, "Empty");
            var result = _engine.GetControversyScore(1);
            Assert.AreEqual("Insufficient Data", result.Label);
            Assert.AreEqual(0, result.ReviewCount);
        }

        // -- GetMostControversial --

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetMostControversial_ZeroCount_Throws()
        {
            _engine.GetMostControversial(0);
        }

        [TestMethod]
        public void GetMostControversial_RespectsMinReviews()
        {
            AddMovie(1, "Enough Reviews");
            AddMovie(2, "Too Few");

            for (int i = 1; i <= 10; i++)
                AddReview(1, i, i % 2 == 0 ? 1 : 5);
            AddReview(2, 20, 1);
            AddReview(2, 21, 5);

            var result = _engine.GetMostControversial(count: 10, minReviews: 5);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].MovieId);
        }

        [TestMethod]
        public void GetMostControversial_OrdersByScoreDescending()
        {
            AddMovie(1, "Very Controversial");
            AddMovie(2, "Mildly Controversial");

            for (int i = 1; i <= 10; i++)
                AddReview(1, i, i <= 5 ? 1 : 5);
            for (int i = 11; i <= 20; i++)
                AddReview(2, i, i <= 15 ? 3 : 4);

            var result = _engine.GetMostControversial(count: 10, minReviews: 5);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].MovieId);
        }

        // -- GetRatingReport --

        [TestMethod]
        public void GetRatingReport_UnknownMovie_ReturnsNull()
        {
            Assert.IsNull(_engine.GetRatingReport(999));
        }

        [TestMethod]
        public void GetRatingReport_CombinesAllMetrics()
        {
            AddMovie(1, "Full Report Movie", Genre.Drama);
            for (int i = 1; i <= 5; i++)
                AddReview(1, i, 4);

            var report = _engine.GetRatingReport(1);
            Assert.IsNotNull(report);
            Assert.IsNotNull(report.Bayesian);
            Assert.IsNotNull(report.Trending);
            Assert.IsNotNull(report.Controversy);

            Assert.AreEqual(1, report.Bayesian.MovieId);
            Assert.AreEqual(1, report.Trending.MovieId);
            Assert.AreEqual(1, report.Controversy.MovieId);
        }

        [TestMethod]
        public void GetRatingReport_BayesianUsesCustomMinVotes()
        {
            AddMovie(1, "Test Movie");
            AddReview(1, 1, 5);

            var report = _engine.GetRatingReport(1, minVotes: 10);
            Assert.AreEqual(10, report.Bayesian.MinVotesThreshold);
            Assert.IsFalse(report.Bayesian.MeetsThreshold);
        }
    }
}
