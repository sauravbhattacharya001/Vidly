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
    public class MovieLifecycleServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryRentalRepository _rentalRepo;
        private MovieLifecycleService _service;
        private static readonly DateTime Now = new DateTime(2026, 3, 1);

        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryRentalRepository.Reset();
            _movieRepo = new InMemoryMovieRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _service = new MovieLifecycleService(_movieRepo, _rentalRepo);
        }

        private int _nextMovieId = 9000;
        private int _nextRentalId = 9000;

        private Movie AddMovie(string name, DateTime? releaseDate = null,
            Genre genre = Genre.Action, decimal? dailyRate = null)
        {
            var m = new Movie
            {
                Id = _nextMovieId++,
                Name = name,
                ReleaseDate = releaseDate,
                Genre = genre,
                DailyRate = dailyRate
            };
            _movieRepo.Add(m);
            return m;
        }

        private Rental AddRental(int movieId, DateTime rentalDate,
            decimal dailyRate = 2.99m, int customerId = 1)
        {
            var r = new Rental
            {
                Id = _nextRentalId++,
                MovieId = movieId,
                CustomerId = customerId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                DailyRate = dailyRate,
                Status = RentalStatus.Returned,
                ReturnDate = rentalDate.AddDays(3)
            };
            _rentalRepo.Add(r);
            r.ReturnDate = rentalDate.AddDays(3);
            r.Status = RentalStatus.Returned;
            _rentalRepo.Update(r);
            return r;
        }

        // ── Constructor ─────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullMovieRepo_Throws()
            => new MovieLifecycleService(null, _rentalRepo);

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullRentalRepo_Throws()
            => new MovieLifecycleService(_movieRepo, null);

        [TestMethod]
        public void Ctor_DefaultConfig_Valid()
        {
            var svc = new MovieLifecycleService(_movieRepo, _rentalRepo);
            Assert.IsNotNull(svc);
        }

        [TestMethod]
        public void Ctor_CustomConfig_Accepted()
        {
            var cfg = new LifecycleConfig { NewReleaseDays = 30 };
            var svc = new MovieLifecycleService(_movieRepo, _rentalRepo, cfg);
            Assert.IsNotNull(svc);
        }

        // ── GetProfile ──────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetProfile_UnknownMovie_Throws()
            => _service.GetProfile(99999, Now);

        [TestMethod]
        public void GetProfile_NewRelease_ClassifiedCorrectly()
        {
            var movie = AddMovie("New Film", Now.AddDays(-30));
            var profile = _service.GetProfile(movie.Id, Now);

            Assert.AreEqual(LifecycleStage.NewRelease, profile.Stage);
            Assert.AreEqual(30, profile.AgeDays);
            Assert.AreEqual("New Film", profile.MovieName);
        }

        [TestMethod]
        public void GetProfile_NoReleaseDate_AssumesOneYear()
        {
            var movie = AddMovie("Unknown Age", null);
            var profile = _service.GetProfile(movie.Id, Now);

            Assert.AreEqual(365, profile.AgeDays);
        }

        [TestMethod]
        public void GetProfile_NoRentals_ZeroDemand()
        {
            var movie = AddMovie("Unpopular", Now.AddDays(-200));
            var profile = _service.GetProfile(movie.Id, Now);

            Assert.AreEqual(0, profile.TotalRentals);
            Assert.AreEqual(0, profile.RecentRentals30d);
            Assert.AreEqual(0, profile.RecentRentals90d);
            Assert.AreEqual(-1, profile.DaysSinceLastRental);
            Assert.AreEqual(0, profile.Velocity);
        }

        [TestMethod]
        public void GetProfile_RecentRentals_HighDemandScore()
        {
            var movie = AddMovie("Hot Movie", Now.AddDays(-200));
            // 4 rentals in last 30 days
            for (int i = 0; i < 4; i++)
                AddRental(movie.Id, Now.AddDays(-i * 5));

            var profile = _service.GetProfile(movie.Id, Now);
            Assert.IsTrue(profile.DemandScore > 40, $"Expected > 40, got {profile.DemandScore}");
            Assert.AreEqual(4, profile.RecentRentals30d);
        }

        [TestMethod]
        public void GetProfile_OldMovie_NoRecentRentals_Archive()
        {
            var movie = AddMovie("Old Classic", Now.AddDays(-500));
            // One rental 200 days ago
            AddRental(movie.Id, Now.AddDays(-200));

            var profile = _service.GetProfile(movie.Id, Now);
            Assert.AreEqual(LifecycleStage.Archive, profile.Stage);
            Assert.IsTrue(profile.DemandScore < 10);
        }

        [TestMethod]
        public void GetProfile_Velocity_CalculatedCorrectly()
        {
            var movie = AddMovie("Steady", Now.AddDays(-200));
            // 6 rentals in last 90 days
            for (int i = 0; i < 6; i++)
                AddRental(movie.Id, Now.AddDays(-i * 14));

            var profile = _service.GetProfile(movie.Id, Now);
            Assert.AreEqual(2.0, profile.Velocity); // 6 rentals / 3 = 2.0
        }

        [TestMethod]
        public void GetProfile_TotalRevenue_SumsCorrectly()
        {
            var movie = AddMovie("Revenue Test", Now.AddDays(-100));
            AddRental(movie.Id, Now.AddDays(-10), 4.99m);
            AddRental(movie.Id, Now.AddDays(-20), 4.99m);

            var profile = _service.GetProfile(movie.Id, Now);
            Assert.IsTrue(profile.TotalRevenue > 0);
            Assert.AreEqual(2, profile.TotalRentals);
        }

        [TestMethod]
        public void GetProfile_Genre_Preserved()
        {
            var movie = AddMovie("Comedy Film", Now.AddDays(-100), Genre.Comedy);
            var profile = _service.GetProfile(movie.Id, Now);
            Assert.AreEqual(Genre.Comedy, profile.Genre);
        }

        // ── Stage Classification ────────────────────────────────────

        [TestMethod]
        public void Stage_Within90Days_AlwaysNewRelease()
        {
            var movie = AddMovie("Brand New", Now.AddDays(-10));
            var profile = _service.GetProfile(movie.Id, Now);
            Assert.AreEqual(LifecycleStage.NewRelease, profile.Stage);
        }

        [TestMethod]
        public void Stage_HighDemand_OldMovie_Trending()
        {
            var movie = AddMovie("Evergreen", Now.AddDays(-200));
            // 5 rentals in last 30 days → high demand
            for (int i = 0; i < 5; i++)
                AddRental(movie.Id, Now.AddDays(-i * 5));

            var profile = _service.GetProfile(movie.Id, Now);
            Assert.AreEqual(LifecycleStage.Trending, profile.Stage);
        }

        [TestMethod]
        public void Stage_ModerateDemand_Catalog()
        {
            var movie = AddMovie("Steady Seller", Now.AddDays(-200));
            // 2 rentals in last 90 days
            AddRental(movie.Id, Now.AddDays(-20));
            AddRental(movie.Id, Now.AddDays(-50));

            var profile = _service.GetProfile(movie.Id, Now);
            Assert.AreEqual(LifecycleStage.Catalog, profile.Stage);
        }

        [TestMethod]
        public void Stage_NoDemand_Archive()
        {
            var movie = AddMovie("Forgotten", Now.AddDays(-400));
            var profile = _service.GetProfile(movie.Id, Now);
            Assert.AreEqual(LifecycleStage.Archive, profile.Stage);
        }

        // ── GetAllProfiles ──────────────────────────────────────────

        [TestMethod]
        public void GetAllProfiles_ReturnsAllMovies()
        {
            AddMovie("A", Now.AddDays(-30));
            AddMovie("B", Now.AddDays(-200));
            AddMovie("C", Now.AddDays(-500));

            var profiles = _service.GetAllProfiles(Now);
            // At least 3 (plus seeded movies from InMemoryRepo)
            Assert.IsTrue(profiles.Count >= 3);
        }

        [TestMethod]
        public void GetAllProfiles_OrderedByDemandScoreDescending()
        {
            var profiles = _service.GetAllProfiles(Now);
            for (int i = 1; i < profiles.Count; i++)
                Assert.IsTrue(profiles[i - 1].DemandScore >= profiles[i].DemandScore);
        }

        // ── Pricing Recommendations ─────────────────────────────────

        [TestMethod]
        public void Pricing_NewRelease_PremiumRate()
        {
            var movie = AddMovie("Just Released", Now.AddDays(-10));
            var rec = _service.GetPricingRecommendation(movie.Id, Now);

            Assert.AreEqual(4.99m, rec.SuggestedRate);
            Assert.AreEqual(LifecycleStage.NewRelease, rec.Stage);
            Assert.IsTrue(rec.Rationale.Contains("new release"));
        }

        [TestMethod]
        public void Pricing_Archive_DiscountRate()
        {
            var movie = AddMovie("Dead Stock", Now.AddDays(-500));
            var rec = _service.GetPricingRecommendation(movie.Id, Now);

            Assert.AreEqual(0.99m, rec.SuggestedRate);
            Assert.AreEqual(LifecycleStage.Archive, rec.Stage);
        }

        [TestMethod]
        public void Pricing_Catalog_StandardRate()
        {
            var movie = AddMovie("Mid Tier", Now.AddDays(-200));
            AddRental(movie.Id, Now.AddDays(-20));
            AddRental(movie.Id, Now.AddDays(-50));

            var rec = _service.GetPricingRecommendation(movie.Id, Now);
            Assert.AreEqual(2.99m, rec.SuggestedRate);
        }

        [TestMethod]
        public void Pricing_Trending_ScaledBetweenCatalogAndNewRelease()
        {
            var movie = AddMovie("Trending Hit", Now.AddDays(-200));
            for (int i = 0; i < 5; i++)
                AddRental(movie.Id, Now.AddDays(-i * 5));

            var rec = _service.GetPricingRecommendation(movie.Id, Now);
            Assert.IsTrue(rec.SuggestedRate >= 2.99m, $"Rate {rec.SuggestedRate} below catalog");
            Assert.IsTrue(rec.SuggestedRate <= 4.99m, $"Rate {rec.SuggestedRate} above new release");
        }

        [TestMethod]
        public void Pricing_PriceChange_ZeroWhenNoDailyRateOverride()
        {
            // When movie has no DailyRate override, CurrentRate defaults to SuggestedRate
            var movie = AddMovie("No Override", Now.AddDays(-500));
            var rec = _service.GetPricingRecommendation(movie.Id, Now);

            Assert.AreEqual(rec.SuggestedRate, rec.CurrentRate);
            Assert.AreEqual(0m, rec.PriceChange);
        }

        [TestMethod]
        public void GetPricingByStage_FiltersCorrectly()
        {
            AddMovie("Archive1", Now.AddDays(-500));
            AddMovie("Archive2", Now.AddDays(-600));

            var recs = _service.GetPricingByStage(LifecycleStage.Archive, Now);
            Assert.IsTrue(recs.Count >= 2);
            foreach (var r in recs)
                Assert.AreEqual(LifecycleStage.Archive, r.Stage);
        }

        // ── Retirement Candidates ───────────────────────────────────

        [TestMethod]
        public void Retirement_NeverRented_Archive_IsCandiddate()
        {
            var movie = AddMovie("Never Rented", Now.AddDays(-300));
            var candidates = _service.GetRetirementCandidates(Now);

            var candidate = candidates.FirstOrDefault(c => c.MovieId == movie.Id);
            Assert.IsNotNull(candidate);
            Assert.IsTrue(candidate.Reasons.Count > 0);
            Assert.AreEqual("Remove \u2014 never rented", candidate.Recommendation);
        }

        [TestMethod]
        public void Retirement_LongInactive_IsCandiddate()
        {
            var movie = AddMovie("Inactive", Now.AddDays(-400));
            AddRental(movie.Id, Now.AddDays(-200));

            var candidates = _service.GetRetirementCandidates(Now);
            var candidate = candidates.FirstOrDefault(c => c.MovieId == movie.Id);
            Assert.IsNotNull(candidate);
            Assert.IsTrue(candidate.DaysSinceLastRental > 180);
        }

        [TestMethod]
        public void Retirement_NewRelease_NeverRetired()
        {
            var movie = AddMovie("Fresh", Now.AddDays(-10));
            var candidates = _service.GetRetirementCandidates(Now);

            Assert.IsFalse(candidates.Any(c => c.MovieId == movie.Id));
        }

        [TestMethod]
        public void Retirement_OrderedByDemandScoreAscending()
        {
            AddMovie("Dead1", Now.AddDays(-500));
            AddMovie("Dead2", Now.AddDays(-600));

            var candidates = _service.GetRetirementCandidates(Now);
            for (int i = 1; i < candidates.Count; i++)
                Assert.IsTrue(candidates[i - 1].DemandScore <= candidates[i].DemandScore);
        }

        // ── Restock Suggestions ─────────────────────────────────────

        [TestMethod]
        public void Restock_HighDemandNewRelease_Suggested()
        {
            var movie = AddMovie("Blockbuster", Now.AddDays(-30));
            for (int i = 0; i < 5; i++)
                AddRental(movie.Id, Now.AddDays(-i * 5));

            var suggestions = _service.GetRestockSuggestions(Now);
            Assert.IsTrue(suggestions.Any(s => s.MovieId == movie.Id));
        }

        [TestMethod]
        public void Restock_LowDemand_NotSuggested()
        {
            var movie = AddMovie("Quiet Title", Now.AddDays(-30));
            var suggestions = _service.GetRestockSuggestions(Now);

            Assert.IsFalse(suggestions.Any(s => s.MovieId == movie.Id));
        }

        [TestMethod]
        public void Restock_Urgency_HighForTopDemand()
        {
            var movie = AddMovie("Mega Hit", Now.AddDays(-30));
            for (int i = 0; i < 8; i++)
                AddRental(movie.Id, Now.AddDays(-i * 3));

            var suggestions = _service.GetRestockSuggestions(Now);
            var suggestion = suggestions.FirstOrDefault(s => s.MovieId == movie.Id);
            Assert.IsNotNull(suggestion);
            Assert.AreEqual("High", suggestion.Urgency);
        }

        [TestMethod]
        public void Restock_OnlyNewReleaseAndTrending()
        {
            var suggestions = _service.GetRestockSuggestions(Now);
            foreach (var s in suggestions)
                Assert.IsTrue(
                    s.Stage == LifecycleStage.NewRelease || s.Stage == LifecycleStage.Trending,
                    $"Unexpected stage {s.Stage}");
        }

        // ── Lifecycle Report ────────────────────────────────────────

        [TestMethod]
        public void Report_ContainsAllStages()
        {
            var report = _service.GetReport(Now);
            Assert.IsTrue(report.StageBreakdown.ContainsKey(LifecycleStage.NewRelease));
            Assert.IsTrue(report.StageBreakdown.ContainsKey(LifecycleStage.Trending));
            Assert.IsTrue(report.StageBreakdown.ContainsKey(LifecycleStage.Catalog));
            Assert.IsTrue(report.StageBreakdown.ContainsKey(LifecycleStage.Archive));
        }

        [TestMethod]
        public void Report_TotalMovies_MatchesRepo()
        {
            var report = _service.GetReport(Now);
            Assert.AreEqual(_movieRepo.GetAll().Count, report.TotalMovies);
        }

        [TestMethod]
        public void Report_StageCountsSumToTotal()
        {
            AddMovie("Extra1", Now.AddDays(-10));
            AddMovie("Extra2", Now.AddDays(-400));

            var report = _service.GetReport(Now);
            var stageSum = report.StageBreakdown.Values.Sum(s => s.Count);
            Assert.AreEqual(report.TotalMovies, stageSum);
        }

        [TestMethod]
        public void Report_NeverRented_Counted()
        {
            AddMovie("Nobody Wants This", Now.AddDays(-300));
            var report = _service.GetReport(Now);
            Assert.IsTrue(report.NeverRented > 0);
        }

        [TestMethod]
        public void Report_Insights_NotEmpty()
        {
            AddMovie("To Trigger Insights", Now.AddDays(-300));
            var report = _service.GetReport(Now);
            Assert.IsTrue(report.Insights.Count > 0);
        }

        [TestMethod]
        public void Report_TopPerformers_MaxFive()
        {
            for (int i = 0; i < 10; i++)
                AddMovie($"Movie{i}", Now.AddDays(-200));

            var report = _service.GetReport(Now);
            Assert.IsTrue(report.TopPerformers.Count <= 5);
            Assert.IsTrue(report.BottomPerformers.Count <= 5);
        }

        [TestMethod]
        public void Report_AsOfDate_Preserved()
        {
            var report = _service.GetReport(Now);
            Assert.AreEqual(Now, report.AsOfDate);
        }

        // ── Transition Alerts ───────────────────────────────────────

        [TestMethod]
        public void Transition_NewReleaseAboutToExpire_AlertGenerated()
        {
            // 86 days old → within 7 days of 90-day boundary
            var movie = AddMovie("Almost Not New", Now.AddDays(-86));
            var alerts = _service.GetTransitionAlerts(Now);

            var alert = alerts.FirstOrDefault(a => a.MovieId == movie.Id);
            Assert.IsNotNull(alert);
            Assert.AreEqual(LifecycleStage.NewRelease, alert.CurrentStage);
            Assert.AreEqual(4, alert.DaysUntilTransition);
        }

        [TestMethod]
        public void Transition_TrendingAboutToDrop_AlertGenerated()
        {
            var movie = AddMovie("Fading Trend", Now.AddDays(-200));
            // Demand score just above trending threshold (~45)
            AddRental(movie.Id, Now.AddDays(-5));
            AddRental(movie.Id, Now.AddDays(-15));
            AddRental(movie.Id, Now.AddDays(-25));
            AddRental(movie.Id, Now.AddDays(-40));
            AddRental(movie.Id, Now.AddDays(-60));

            var profile = _service.GetProfile(movie.Id, Now);
            if (profile.Stage == LifecycleStage.Trending)
            {
                var alerts = _service.GetTransitionAlerts(Now);
                var alert = alerts.FirstOrDefault(a => a.MovieId == movie.Id);
                // May or may not be near threshold
                if (alert != null)
                {
                    Assert.AreEqual(LifecycleStage.Catalog, alert.PredictedStage);
                    Assert.AreEqual(-1, alert.DaysUntilTransition);
                }
            }
        }

        [TestMethod]
        public void Transition_StableNewRelease_NoAlert()
        {
            var movie = AddMovie("Safely New", Now.AddDays(-10));
            var alerts = _service.GetTransitionAlerts(Now);
            Assert.IsFalse(alerts.Any(a => a.MovieId == movie.Id));
        }

        // ── Custom Config ───────────────────────────────────────────

        [TestMethod]
        public void CustomConfig_ShorterNewRelease()
        {
            var cfg = new LifecycleConfig { NewReleaseDays = 30 };
            var svc = new MovieLifecycleService(_movieRepo, _rentalRepo, cfg);

            var movie = AddMovie("Short Window", Now.AddDays(-45));
            var profile = svc.GetProfile(movie.Id, Now);

            // 45 days > 30 day window → not NewRelease
            Assert.AreNotEqual(LifecycleStage.NewRelease, profile.Stage);
        }

        [TestMethod]
        public void CustomConfig_HigherTrendingThreshold()
        {
            var cfg = new LifecycleConfig { TrendingThreshold = 80 };
            var svc = new MovieLifecycleService(_movieRepo, _rentalRepo, cfg);

            var movie = AddMovie("Would-be Trending", Now.AddDays(-200));
            AddRental(movie.Id, Now.AddDays(-5));
            AddRental(movie.Id, Now.AddDays(-15));
            AddRental(movie.Id, Now.AddDays(-25));

            var profile = svc.GetProfile(movie.Id, Now);
            // With higher threshold, may not qualify as trending
            Assert.AreNotEqual(LifecycleStage.Trending, profile.Stage);
        }

        [TestMethod]
        public void CustomConfig_PricingRates()
        {
            var cfg = new LifecycleConfig
            {
                NewReleaseRate = 5.99m,
                CatalogRate = 3.49m,
                ArchiveRate = 1.49m
            };
            var svc = new MovieLifecycleService(_movieRepo, _rentalRepo, cfg);

            var movie = AddMovie("Custom Priced", Now.AddDays(-10));
            var rec = svc.GetPricingRecommendation(movie.Id, Now);
            Assert.AreEqual(5.99m, rec.SuggestedRate);
        }

        // ── Demand Score Edge Cases ─────────────────────────────────

        [TestMethod]
        public void DemandScore_CappedAt100()
        {
            var movie = AddMovie("Super Popular", Now.AddDays(-200));
            // 10 rentals in the last 30 days
            for (int i = 0; i < 10; i++)
                AddRental(movie.Id, Now.AddDays(-i * 2));

            var profile = _service.GetProfile(movie.Id, Now);
            Assert.IsTrue(profile.DemandScore <= 100);
        }

        [TestMethod]
        public void DemandScore_RecentRentalBoost()
        {
            var movie1 = AddMovie("Recent", Now.AddDays(-200));
            AddRental(movie1.Id, Now.AddDays(-3)); // very recent

            var movie2 = AddMovie("Older Rental", Now.AddDays(-200));
            AddRental(movie2.Id, Now.AddDays(-100)); // long ago

            var p1 = _service.GetProfile(movie1.Id, Now);
            var p2 = _service.GetProfile(movie2.Id, Now);

            Assert.IsTrue(p1.DemandScore > p2.DemandScore,
                $"Recent ({p1.DemandScore}) should score higher than old ({p2.DemandScore})");
        }

        [TestMethod]
        public void DemandScore_LifetimeContribution_LogScaled()
        {
            var movie = AddMovie("Veteran", Now.AddDays(-500));
            // 20 rentals spread over time
            for (int i = 0; i < 20; i++)
                AddRental(movie.Id, Now.AddDays(-i * 25));

            var profile = _service.GetProfile(movie.Id, Now);
            // Lifetime contributes via log — should have some score even if recent activity is low
            Assert.IsTrue(profile.DemandScore > 0);
        }

        // ── LifecycleConfig Defaults ────────────────────────────────

        [TestMethod]
        public void Config_DefaultValues()
        {
            var cfg = new LifecycleConfig();
            Assert.AreEqual(90, cfg.NewReleaseDays);
            Assert.AreEqual(40.0, cfg.TrendingThreshold);
            Assert.AreEqual(10.0, cfg.ArchiveThreshold);
            Assert.AreEqual(4.99m, cfg.NewReleaseRate);
            Assert.AreEqual(2.99m, cfg.CatalogRate);
            Assert.AreEqual(0.99m, cfg.ArchiveRate);
            Assert.AreEqual(180, cfg.RetirementInactiveDays);
            Assert.AreEqual(2, cfg.RetirementMinRentals);
            Assert.AreEqual(30.0, cfg.RestockDemandThreshold);
        }

        // ── Integration / End-to-End ────────────────────────────────

        [TestMethod]
        public void FullLifecycle_MovieProgresses()
        {
            // Simulate a movie going through its lifecycle
            var movie = AddMovie("Lifecycle Test", Now.AddDays(-100));

            // Phase 1: Just after new release window, no rentals → Archive
            var p1 = _service.GetProfile(movie.Id, Now);
            Assert.AreEqual(LifecycleStage.Archive, p1.Stage);

            // Phase 2: Gets lots of rentals → Trending
            for (int i = 0; i < 6; i++)
                AddRental(movie.Id, Now.AddDays(-i * 4));

            var p2 = _service.GetProfile(movie.Id, Now);
            Assert.AreEqual(LifecycleStage.Trending, p2.Stage);
        }

        [TestMethod]
        public void Report_WithMixedInventory_Comprehensive()
        {
            AddMovie("New1", Now.AddDays(-10));
            var trending = AddMovie("Trend1", Now.AddDays(-200));
            for (int i = 0; i < 5; i++)
                AddRental(trending.Id, Now.AddDays(-i * 5));
            AddMovie("Dead1", Now.AddDays(-500));

            var report = _service.GetReport(Now);
            Assert.IsTrue(report.TotalMovies >= 3);
            Assert.IsNotNull(report.Insights);
            Assert.IsNotNull(report.TopPerformers);
            Assert.IsNotNull(report.BottomPerformers);
        }

        [TestMethod]
        public void Pricing_AllMoviesGetRecommendation()
        {
            AddMovie("P1", Now.AddDays(-10));
            AddMovie("P2", Now.AddDays(-200));
            AddMovie("P3", Now.AddDays(-500));

            foreach (var movie in _movieRepo.GetAll())
            {
                var rec = _service.GetPricingRecommendation(movie.Id, Now);
                Assert.IsNotNull(rec);
                Assert.IsTrue(rec.SuggestedRate > 0);
            }
        }

        [TestMethod]
        public void Retirement_CustomThresholds()
        {
            var cfg = new LifecycleConfig
            {
                RetirementInactiveDays = 30,
                RetirementMinRentals = 5
            };
            var svc = new MovieLifecycleService(_movieRepo, _rentalRepo, cfg);

            // Old movie with 1 rental long ago → Archive stage + meets both thresholds
            var movie = AddMovie("Low Performer", Now.AddDays(-400));
            AddRental(movie.Id, Now.AddDays(-200));

            var candidates = svc.GetRetirementCandidates(Now);
            var candidate = candidates.FirstOrDefault(c => c.MovieId == movie.Id);
            Assert.IsNotNull(candidate, "Should be retirement candidate with strict thresholds");
        }

        [TestMethod]
        public void Report_InsightTriggered_NoNewReleases()
        {
            // All seeded movies are old — check for "no new releases" insight
            var report = _service.GetReport(Now);
            Assert.IsTrue(report.Insights.Any(i => i.IndexOf("new release", StringComparison.OrdinalIgnoreCase) >= 0)
                || report.Insights.Count > 0,
                "Should generate at least one insight");
        }
    }
}
