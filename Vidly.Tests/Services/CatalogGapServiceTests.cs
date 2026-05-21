using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="CatalogGapService"/>.
    ///
    /// The service compares per-genre catalog share against per-genre rental
    /// demand share and emits gap verdicts, acquisition recommendations and
    /// demand-signal patterns. These tests exercise each of those branches
    /// with deterministic catalogs assembled directly into the in-memory
    /// repositories (which are static singletons - hence the Reset() calls).
    /// </summary>
    [TestClass]
    public class CatalogGapServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;
        private CatalogGapService _service;

        [TestInitialize]
        public void Setup()
        {
            // Start every test from a fully empty catalog so demand/supply
            // shares are computed only against the fixtures we add below.
            InMemoryMovieRepository.ResetEmpty();
            InMemoryRentalRepository.Reset();
            // Reset() seeds three rentals/movies referencing the default
            // seed Movies. Clear those so we control the entire dataset.
            ClearRentals();

            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _service = new CatalogGapService(_movieRepo, _rentalRepo);
        }

        private void ClearRentals()
        {
            var repo = new InMemoryRentalRepository();
            foreach (var r in repo.GetAll().ToList())
            {
                repo.Remove(r.Id);
            }
        }

        // ---- Fixture helpers ----

        private Movie AddMovie(string name, Genre genre, int? rating = 4, DateTime? releaseDate = null)
        {
            var movie = new Movie
            {
                Name = name,
                Genre = genre,
                Rating = rating,
                ReleaseDate = releaseDate ?? DateTime.Today.AddDays(-365),
                DailyRate = 2.99m
            };
            _movieRepo.Add(movie); // Add() assigns the Id in-place
            return movie;
        }

        private void AddRentals(int movieId, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _rentalRepo.Add(new Rental
                {
                    MovieId = movieId,
                    CustomerId = 1,
                    RentalDate = DateTime.Today.AddDays(-10 - i),
                    DueDate = DateTime.Today.AddDays(-3 - i),
                    ReturnDate = DateTime.Today.AddDays(-5 - i),
                    DailyRate = 2.99m,
                    Status = RentalStatus.Returned
                });
            }
        }

        // ---- Empty catalog ----

        [TestMethod]
        public void GetDashboard_EmptyCatalog_ReturnsSafeDefaults()
        {
            var dashboard = _service.GetDashboard();

            Assert.IsNotNull(dashboard);
            Assert.AreEqual(0, dashboard.Health.TotalMovies);
            Assert.AreEqual(0, dashboard.Health.TotalRentals);
            Assert.AreEqual(0, dashboard.Health.ActiveGenres);
            Assert.AreEqual(0, dashboard.Health.StaleMovies);
            // With no gaps, the penalty is zero and the score is the maximum.
            Assert.AreEqual(100.0, dashboard.OverallCoverageScore);
            Assert.AreEqual("A", dashboard.CoverageGrade);
            Assert.IsNotNull(dashboard.GenreGaps);
            Assert.AreEqual(Enum.GetValues(typeof(Genre)).Length, dashboard.GenreGaps.Count,
                "Every genre should appear in the gap report, even with zero movies.");
            Assert.IsTrue(dashboard.GenreGaps.All(g => g.MovieCount == 0 && g.RentalCount == 0));
            Assert.IsTrue(dashboard.GenreGaps.All(g => g.Verdict == "Balanced"),
                "Empty catalog should produce balanced verdicts (gap score 0).");
            CollectionAssert.AreEqual(new List<AcquisitionRecommendation>(), dashboard.Recommendations);
        }

        // ---- Per-genre verdicts ----

        [TestMethod]
        public void GetDashboard_DemandFarExceedsSupply_FlagsUnderserved()
        {
            // Catalog: 5 Drama, 1 Action. Demand: 0 Drama, 8 Action.
            // Action: catalog 16.7%, demand 100% -> gap +83.3 -> Underserved.
            for (int i = 0; i < 5; i++) AddMovie($"Drama {i}", Genre.Drama);
            var action = AddMovie("Lone Action", Genre.Action);
            AddRentals(action.Id, 8);

            var dashboard = _service.GetDashboard();
            var actionGap = dashboard.GenreGaps.Single(g => g.Genre == Genre.Action);

            Assert.AreEqual("Underserved", actionGap.Verdict);
            Assert.IsTrue(actionGap.GapScore > 5,
                $"Expected gap score above the +5 underserved threshold but was {actionGap.GapScore}.");
            Assert.IsTrue(actionGap.MarketSharePct > actionGap.CatalogSharePct);

            var rec = dashboard.Recommendations.SingleOrDefault(r => r.Genre == Genre.Action);
            Assert.IsNotNull(rec, "An underserved genre should produce an acquisition recommendation.");
            Assert.AreEqual("High", rec.Priority, "Gap > 15 must map to High priority.");
            Assert.IsTrue(rec.SuggestedCount >= 1);
            Assert.IsTrue(rec.MinRating == 4, "DemandRatio > 2 should require min rating 4.");
        }

        [TestMethod]
        public void GetDashboard_SupplyFarExceedsDemand_FlagsOversaturated()
        {
            // Catalog: 9 Comedy, 1 Action. Demand: 0 Comedy, 10 Action.
            // Comedy: catalog 90%, demand 0% -> gap -90 -> Oversaturated.
            for (int i = 0; i < 9; i++) AddMovie($"Comedy {i}", Genre.Comedy);
            var action = AddMovie("Action", Genre.Action);
            AddRentals(action.Id, 10);

            var dashboard = _service.GetDashboard();
            var comedyGap = dashboard.GenreGaps.Single(g => g.Genre == Genre.Comedy);

            Assert.AreEqual("Oversaturated", comedyGap.Verdict);
            Assert.IsTrue(comedyGap.GapScore < -5);
            Assert.IsFalse(dashboard.Recommendations.Any(r => r.Genre == Genre.Comedy),
                "Oversaturated genres should not receive acquisition recommendations.");
        }

        [TestMethod]
        public void GetDashboard_GapWithinTolerance_IsBalanced()
        {
            // Single movie that gets rented once - catalog 100%, demand 100%, gap = 0.
            var movie = AddMovie("Even Steven", Genre.Drama);
            AddRentals(movie.Id, 1);

            var dashboard = _service.GetDashboard();
            var dramaGap = dashboard.GenreGaps.Single(g => g.Genre == Genre.Drama);

            Assert.AreEqual("Balanced", dramaGap.Verdict);
            Assert.AreEqual(0.0, dramaGap.GapScore, 0.05);
        }

        // ---- Ordering / shape ----

        [TestMethod]
        public void GetDashboard_GenreGaps_OrderedByGapScoreDescending()
        {
            // Two underserved genres at different intensities.
            for (int i = 0; i < 6; i++) AddMovie($"Drama {i}", Genre.Drama);
            var horror = AddMovie("Horror One", Genre.Horror);
            var thriller = AddMovie("Thriller One", Genre.Thriller);
            AddRentals(horror.Id, 6);
            AddRentals(thriller.Id, 2);

            var gaps = _service.GetDashboard().GenreGaps;

            // Descending order means earlier entries have higher or equal GapScore.
            for (int i = 1; i < gaps.Count; i++)
            {
                Assert.IsTrue(gaps[i - 1].GapScore >= gaps[i].GapScore,
                    $"GenreGaps not in descending GapScore order at index {i}: " +
                    $"{gaps[i - 1].GapScore} < {gaps[i].GapScore}");
            }
            Assert.AreEqual(Genre.Horror, gaps.First().Genre,
                "Largest positive gap should sort to the top.");
        }

        [TestMethod]
        public void GetDashboard_RecommendationPriority_TracksGapScore()
        {
            // Three genres with three different gap intensities.
            // Catalog: 1 Action, 1 Comedy, 1 Drama (33% each).
            // Rentals: 30 Action, 10 Comedy, 0 Drama.
            // Demand shares: 75% / 25% / 0%; gaps: +41.7 / -8.3 / -33.3
            // -> Action: High (>15), Comedy: Oversaturated (no rec), Drama: Oversaturated (no rec).
            var action = AddMovie("Act", Genre.Action);
            var comedy = AddMovie("Com", Genre.Comedy);
            AddMovie("Dra", Genre.Drama);
            AddRentals(action.Id, 30);
            AddRentals(comedy.Id, 10);

            var dashboard = _service.GetDashboard();

            var actionRec = dashboard.Recommendations.Single(r => r.Genre == Genre.Action);
            Assert.AreEqual("High", actionRec.Priority);
            Assert.IsTrue(actionRec.SuggestedCount >= (int)Math.Ceiling(41.7 / 5.0) - 1,
                "SuggestedCount should track ceil(gapScore / 5).");
            Assert.IsTrue(actionRec.ExpectedImpact > 0);
        }

        [TestMethod]
        public void GetDashboard_RecommendationPriority_MediumAndLowBoundaries()
        {
            // Construct a +6 gap (Medium) and a +3 gap (Low) carefully:
            // 100 movies total split 5/5/90 across three genres, then rentals
            // arranged so the gap scores land in the expected bands.
            // Action: 5 movies (5%), 11 rentals out of 100 (11%) -> gap +6 -> Medium
            // Comedy: 5 movies (5%), 8 rentals out of 100  (8%) -> gap +3 -> Low
            // Drama: 90 movies, 81 rentals
            var actionMovies = Enumerable.Range(0, 5)
                .Select(i => AddMovie($"A{i}", Genre.Action)).ToList();
            var comedyMovies = Enumerable.Range(0, 5)
                .Select(i => AddMovie($"C{i}", Genre.Comedy)).ToList();
            var dramaMovies = Enumerable.Range(0, 90)
                .Select(i => AddMovie($"D{i}", Genre.Drama)).ToList();

            AddRentals(actionMovies[0].Id, 11);
            AddRentals(comedyMovies[0].Id, 8);
            // Spread drama rentals across a few movies so gap goes negative.
            AddRentals(dramaMovies[0].Id, 40);
            AddRentals(dramaMovies[1].Id, 41);

            var dashboard = _service.GetDashboard();

            var actionRec = dashboard.Recommendations.Single(r => r.Genre == Genre.Action);
            var comedyRec = dashboard.Recommendations.Single(r => r.Genre == Genre.Comedy);

            Assert.AreEqual("Medium", actionRec.Priority,
                $"Action gap {dashboard.GenreGaps.Single(g => g.Genre == Genre.Action).GapScore} " +
                $"should map to Medium (5 < gap <= 15).");
            Assert.AreEqual("Low", comedyRec.Priority,
                $"Comedy gap {dashboard.GenreGaps.Single(g => g.Genre == Genre.Comedy).GapScore} " +
                $"should map to Low (0 < gap <= 5).");

            // High-priority recommendations should sort ahead of Medium and Low.
            var priorityOrder = dashboard.Recommendations.Select(r => r.Priority).ToList();
            var priorityRank = priorityOrder
                .Select(p => p == "High" ? 3 : p == "Medium" ? 2 : 1).ToList();
            for (int i = 1; i < priorityRank.Count; i++)
            {
                Assert.IsTrue(priorityRank[i - 1] >= priorityRank[i],
                    "Recommendations are not sorted by descending priority.");
            }
        }

        // ---- Coverage grade ----

        [TestMethod]
        public void GetDashboard_BalancedCatalog_GradesA()
        {
            // 10 movies / 10 rentals all in Action -> perfect alignment.
            var movie = AddMovie("Hit", Genre.Action);
            AddRentals(movie.Id, 10);

            var dashboard = _service.GetDashboard();

            Assert.AreEqual("A", dashboard.CoverageGrade);
            Assert.AreEqual(100.0, dashboard.OverallCoverageScore);
        }

        [TestMethod]
        public void GetDashboard_LargeGaps_DropGradeBelowF()
        {
            // Heavy single-genre demand on a tiny slice of catalog
            // produces a huge gap penalty.
            for (int i = 0; i < 9; i++) AddMovie($"Drama {i}", Genre.Drama);
            var action = AddMovie("Single Action", Genre.Action);
            AddRentals(action.Id, 50);

            var dashboard = _service.GetDashboard();

            Assert.IsTrue(dashboard.OverallCoverageScore < 60,
                $"Expected sub-60 coverage score but got {dashboard.OverallCoverageScore}.");
            Assert.AreEqual("F", dashboard.CoverageGrade);
        }

        // ---- Health summary ----

        [TestMethod]
        public void GetDashboard_StaleMovies_AreMoviesWithZeroRentals()
        {
            var rented = AddMovie("Rented", Genre.Action);
            AddMovie("Never Rented A", Genre.Drama);
            AddMovie("Never Rented B", Genre.Comedy);
            AddRentals(rented.Id, 3);

            var dashboard = _service.GetDashboard();

            Assert.AreEqual(3, dashboard.Health.TotalMovies);
            Assert.AreEqual(3, dashboard.Health.TotalRentals);
            Assert.AreEqual(2, dashboard.Health.StaleMovies);
        }

        [TestMethod]
        public void GetDashboard_AvgRating_IgnoresMoviesWithoutRating()
        {
            AddMovie("Rated 5", Genre.Action, rating: 5);
            AddMovie("Rated 3", Genre.Comedy, rating: 3);
            AddMovie("Unrated", Genre.Drama, rating: null);

            var dashboard = _service.GetDashboard();

            // Average of {5,3} = 4, rounded to 1 decimal place.
            Assert.AreEqual(4.0, dashboard.Health.AvgRating, 0.05);
        }

        [TestMethod]
        public void GetDashboard_DiversityIndex_ZeroForSingleGenre()
        {
            // All movies in one genre: Shannon entropy is undefined / zero.
            for (int i = 0; i < 5; i++) AddMovie($"D{i}", Genre.Drama);

            var dashboard = _service.GetDashboard();

            Assert.AreEqual(0.0, dashboard.Health.DiversityIndex, 0.01);
            Assert.AreEqual(1, dashboard.Health.ActiveGenres);
        }

        [TestMethod]
        public void GetDashboard_DiversityIndex_MaxForEvenSpread()
        {
            // Equal counts across N genres -> normalized entropy = 1 (i.e. 100%).
            AddMovie("Act", Genre.Action);
            AddMovie("Com", Genre.Comedy);
            AddMovie("Dra", Genre.Drama);
            AddMovie("Hor", Genre.Horror);

            var dashboard = _service.GetDashboard();

            Assert.AreEqual(100.0, dashboard.Health.DiversityIndex, 0.5,
                "Perfectly even spread should normalize to a diversity index of 100.");
            Assert.AreEqual(4, dashboard.Health.ActiveGenres);
        }

        // ---- Demand signals ----

        [TestMethod]
        public void GetDashboard_UnderservedGenres_EmitDemandSignals()
        {
            var action = AddMovie("Act", Genre.Action);
            AddMovie("Dra", Genre.Drama);
            AddMovie("Com", Genre.Comedy);
            AddRentals(action.Id, 20);

            var dashboard = _service.GetDashboard();

            var actionSignal = dashboard.UnmetDemand
                .SingleOrDefault(s => s.Category == "Genre Gap" && s.Pattern.Contains("Action"));
            Assert.IsNotNull(actionSignal, "Underserved Action genre should emit a Genre Gap signal.");
            Assert.IsTrue(actionSignal.Confidence > 0 && actionSignal.Confidence <= 1.0);
            StringAssert.Contains(actionSignal.ActionItem, "Action");
        }

        [TestMethod]
        public void GetDashboard_HighRatedRentalsDominate_EmitRatingGapSignal()
        {
            // Catalog: 1 high-rated, 4 low-rated -> high-rated movies = 20%.
            // Demand: only the high-rated movie gets rented -> 100% high-rated.
            // Difference 80 percentage points > 10 -> Rating Gap signal fires.
            var hi = AddMovie("Top", Genre.Action, rating: 5);
            AddMovie("Mid A", Genre.Drama, rating: 2);
            AddMovie("Mid B", Genre.Drama, rating: 2);
            AddMovie("Mid C", Genre.Drama, rating: 2);
            AddMovie("Mid D", Genre.Drama, rating: 2);
            AddRentals(hi.Id, 10);

            var dashboard = _service.GetDashboard();

            Assert.IsTrue(dashboard.UnmetDemand.Any(s => s.Category == "Rating Gap"),
                "A catalog dominated by high-rated rentals should emit a Rating Gap signal.");
        }

        [TestMethod]
        public void GetDashboard_OldCatalog_EmitsFreshnessGapSignal()
        {
            // All movies released > 5 years ago -> freshness 0% -> signal fires.
            var oldDate = DateTime.Today.AddYears(-10);
            for (int i = 0; i < 4; i++)
                AddMovie($"Classic {i}", Genre.Drama, releaseDate: oldDate);

            var dashboard = _service.GetDashboard();

            Assert.IsTrue(dashboard.UnmetDemand.Any(s => s.Category == "Freshness Gap"),
                "A catalog with no recent releases should emit a Freshness Gap signal.");
            Assert.IsTrue(dashboard.Health.FreshnessScore < 30);
        }

        [TestMethod]
        public void GetDashboard_FreshCatalog_DoesNotEmitFreshnessGap()
        {
            var recent = DateTime.Today.AddMonths(-6);
            for (int i = 0; i < 4; i++)
                AddMovie($"Fresh {i}", Genre.Drama, releaseDate: recent);

            var dashboard = _service.GetDashboard();

            Assert.IsFalse(dashboard.UnmetDemand.Any(s => s.Category == "Freshness Gap"),
                "Catalog of recent releases should not emit a Freshness Gap signal.");
            Assert.AreEqual(100.0, dashboard.Health.FreshnessScore, 0.5);
        }

        // ---- Timestamp ----

        [TestMethod]
        public void GetDashboard_AnalyzedAt_IsCloseToNow()
        {
            var before = DateTime.Now;
            var dashboard = _service.GetDashboard();
            var after = DateTime.Now;

            Assert.IsTrue(dashboard.AnalyzedAt >= before.AddSeconds(-1)
                       && dashboard.AnalyzedAt <= after.AddSeconds(1),
                "AnalyzedAt should be stamped at dashboard generation time.");
        }
    }
}
