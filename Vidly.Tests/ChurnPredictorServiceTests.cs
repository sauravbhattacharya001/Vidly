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
    public class ChurnPredictorServiceTests
    {
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private ChurnPredictorService _service;
        private static readonly DateTime Now = new DateTime(2026, 3, 1);

        [TestInitialize]
        public void Setup()
        {
            InMemoryMovieRepository.Reset();
            InMemoryCustomerRepository.Reset();
            InMemoryRentalRepository.Reset();
            _customerRepo = new InMemoryCustomerRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _service = new ChurnPredictorService(_customerRepo, _rentalRepo, _movieRepo);
        }

        private int _nextCustId = 8000;
        private int _nextRentalId = 8000;
        private int _nextMovieId = 8000;

        private Customer AddCustomer(string name, MembershipType tier = MembershipType.Basic)
        {
            var c = new Customer { Id = _nextCustId++, Name = name, MembershipType = tier };
            _customerRepo.Add(c);
            return c;
        }

        private Movie AddMovie(string name, Genre genre = Genre.Action)
        {
            var m = new Movie { Id = _nextMovieId++, Name = name, Genre = genre };
            _movieRepo.Add(m);
            return m;
        }

        private Rental AddRental(int customerId, int movieId, DateTime rentalDate,
            decimal dailyRate = 3.99m, DateTime? returnDate = null, bool late = false)
        {
            var due = rentalDate.AddDays(7);
            var ret = returnDate ?? rentalDate.AddDays(late ? 10 : 3);
            var r = new Rental
            {
                Id = _nextRentalId++,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rentalDate,
                DueDate = due,
                DailyRate = dailyRate,
                Status = RentalStatus.Active
            };
            _rentalRepo.Add(r);
            // Set ReturnDate after Add since the repo clears it on insert
            r.ReturnDate = ret;
            r.Status = RentalStatus.Returned;
            _rentalRepo.Update(r);
            return r;
        }

        // ── Constructor ─────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullCustomerRepo_Throws()
            => new ChurnPredictorService(null, _rentalRepo, _movieRepo);

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullRentalRepo_Throws()
            => new ChurnPredictorService(_customerRepo, null, _movieRepo);

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullMovieRepo_Throws()
            => new ChurnPredictorService(_customerRepo, _rentalRepo, null);

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Ctor_InvalidConfig_Throws()
        {
            var bad = new ChurnConfig { RecencyWeight = 0.5, FrequencyDeclineWeight = 0.5, EngagementWeight = 0.5 };
            new ChurnPredictorService(_customerRepo, _rentalRepo, _movieRepo, bad);
        }

        [TestMethod]
        public void Ctor_DefaultConfig_Valid()
        {
            var svc = new ChurnPredictorService(_customerRepo, _rentalRepo, _movieRepo);
            Assert.IsNotNull(svc);
        }

        // ── Analyze Single ──────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Analyze_NonExistentCustomer_Throws()
            => _service.Analyze(99999, Now);

        [TestMethod]
        public void Analyze_RecentActiveCustomer_LowRisk()
        {
            var c = AddCustomer("Active Andy");
            var m1 = AddMovie("Movie A", Genre.Action);
            var m2 = AddMovie("Movie B", Genre.Comedy);
            var m3 = AddMovie("Movie C", Genre.Drama);
            var m4 = AddMovie("Movie D", Genre.Horror);
            var m5 = AddMovie("Movie E", Genre.SciFi);

            for (int i = 0; i < 20; i++)
            {
                var movies = new[] { m1, m2, m3, m4, m5 };
                AddRental(c.Id, movies[i % 5].Id, Now.AddDays(-5 * (20 - i)));
            }

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(ChurnRisk.Low, profile.RiskLevel);
            Assert.IsTrue(profile.RiskScore < 25);
            Assert.AreEqual(20, profile.TotalRentals);
            Assert.AreEqual(5, profile.GenreDiversity);
        }

        [TestMethod]
        public void Analyze_LongInactiveCustomer_HighRisk()
        {
            var c = AddCustomer("Gone Gary");
            var m = AddMovie("Old Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-150));
            AddRental(c.Id, m.Id, Now.AddDays(-145));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.RiskScore >= 50, $"Expected >= 50, got {profile.RiskScore}");
            Assert.IsTrue(profile.DaysSinceLastRental >= 140);
        }

        [TestMethod]
        public void Analyze_FrequentLateReturns_IncreasesRisk()
        {
            var c = AddCustomer("Late Larry");
            var m = AddMovie("Movie");

            // All 10 rentals returned late (day 10 vs due day 7)
            for (int i = 0; i < 10; i++)
                AddRental(c.Id, m.Id, Now.AddDays(-10 * (10 - i)), late: true);

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(1.0, profile.LateReturnRate, 0.01, "All returns should be late");
            Assert.IsTrue(profile.Factors.LateReturnScore >= 90,
                $"Expected late score >= 90 for 100% late rate, got {profile.Factors.LateReturnScore}");
        }

        [TestMethod]
        public void Analyze_DecliningFrequency_HigherRisk()
        {
            var c = AddCustomer("Declining Dan");
            var m = AddMovie("Movie");

            AddRental(c.Id, m.Id, Now.AddDays(-200));
            AddRental(c.Id, m.Id, Now.AddDays(-195));
            AddRental(c.Id, m.Id, Now.AddDays(-190));
            AddRental(c.Id, m.Id, Now.AddDays(-100));
            AddRental(c.Id, m.Id, Now.AddDays(-50));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.FrequencyTrend > 0, "Expected positive trend (growing gaps)");
        }

        [TestMethod]
        public void Analyze_SingleGenre_LowerDiversity()
        {
            var c = AddCustomer("Genre George");
            var m1 = AddMovie("Action 1", Genre.Action);
            var m2 = AddMovie("Action 2", Genre.Action);
            var m3 = AddMovie("Action 3", Genre.Action);

            AddRental(c.Id, m1.Id, Now.AddDays(-30));
            AddRental(c.Id, m2.Id, Now.AddDays(-20));
            AddRental(c.Id, m3.Id, Now.AddDays(-10));

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(1, profile.GenreDiversity);
            Assert.IsTrue(profile.Factors.DiversityScore >= 70);
        }

        [TestMethod]
        public void Analyze_HighDiversity_LowDiversityScore()
        {
            var c = AddCustomer("Diverse Diana");
            var genres = new[] { Genre.Action, Genre.Comedy, Genre.Drama, Genre.Horror, Genre.SciFi };
            foreach (var g in genres)
            {
                var m = AddMovie($"Movie {g}", g);
                AddRental(c.Id, m.Id, Now.AddDays(-10));
            }

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(5, profile.GenreDiversity);
            Assert.IsTrue(profile.Factors.DiversityScore <= 10);
        }

        [TestMethod]
        public void Analyze_NewCustomerFewRentals_ModerateEngagementRisk()
        {
            var c = AddCustomer("New Nancy");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-5));

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(1, profile.TotalRentals);
            Assert.IsTrue(profile.Factors.EngagementScore >= 60);
        }

        [TestMethod]
        public void Analyze_RiskScoreClamped0To100()
        {
            var c = AddCustomer("Test");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-5));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.RiskScore >= 0 && profile.RiskScore <= 100);
        }

        [TestMethod]
        public void Analyze_FactorsPopulated()
        {
            var c = AddCustomer("Test");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-30));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsNotNull(profile.Factors);
        }

        [TestMethod]
        public void Analyze_RetentionActionsPopulated()
        {
            var c = AddCustomer("Test");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-100));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.RetentionActions.Count > 0);
        }

        // ── AnalyzeAll ──────────────────────────────────────────────

        [TestMethod]
        public void AnalyzeAll_ReturnsProfiles()
        {
            // Pre-seeded data means we'll have some profiles
            var result = _service.AnalyzeAll(Now);
            Assert.IsTrue(result.Count > 0, "Should have profiles from seeded data");
        }

        [TestMethod]
        public void AnalyzeAll_IncludesAddedCustomer()
        {
            var c = AddCustomer("New Customer");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-10));

            var result = _service.AnalyzeAll(Now);
            Assert.IsTrue(result.Any(p => p.CustomerId == c.Id));
        }

        [TestMethod]
        public void AnalyzeAll_SortedByRiskDescending()
        {
            var result = _service.AnalyzeAll(Now);
            if (result.Count >= 2)
            {
                for (int i = 1; i < result.Count; i++)
                    Assert.IsTrue(result[i - 1].RiskScore >= result[i].RiskScore,
                        $"Profile at {i-1} ({result[i-1].RiskScore}) should be >= profile at {i} ({result[i].RiskScore})");
            }
        }

        // ── GetSummary ──────────────────────────────────────────────

        [TestMethod]
        public void GetSummary_CountsAddUp()
        {
            var summary = _service.GetSummary(Now);
            var total = summary.LowRiskCount + summary.MediumRiskCount +
                       summary.HighRiskCount + summary.CriticalRiskCount;
            Assert.AreEqual(summary.TotalCustomersAnalyzed, total);
        }

        [TestMethod]
        public void GetSummary_TopAtRisk_RespectTopN()
        {
            var summary = _service.GetSummary(Now, topN: 3);
            Assert.IsTrue(summary.TopAtRisk.Count <= 3);
        }

        [TestMethod]
        public void GetSummary_RevenueAtRisk_NonNegative()
        {
            var summary = _service.GetSummary(Now);
            Assert.IsTrue(summary.RevenueAtRisk >= 0);
        }

        [TestMethod]
        public void GetSummary_ByTier_Populated()
        {
            var summary = _service.GetSummary(Now);
            Assert.IsTrue(summary.ByTier.Count > 0, "Should have tier breakdown from seeded data");
            foreach (var tier in summary.ByTier.Values)
            {
                Assert.IsTrue(tier.Count > 0);
                Assert.IsTrue(tier.AverageRiskScore >= 0 && tier.AverageRiskScore <= 100);
            }
        }

        [TestMethod]
        public void GetSummary_AverageRiskScore_Reasonable()
        {
            var summary = _service.GetSummary(Now);
            Assert.IsTrue(summary.AverageRiskScore >= 0 && summary.AverageRiskScore <= 100);
        }

        // ── GetByRiskLevel ──────────────────────────────────────────

        [TestMethod]
        public void GetByRiskLevel_FiltersCorrectly()
        {
            var levels = new[] { ChurnRisk.Low, ChurnRisk.Medium, ChurnRisk.High, ChurnRisk.Critical };
            foreach (var level in levels)
            {
                var results = _service.GetByRiskLevel(level, Now);
                foreach (var p in results)
                    Assert.AreEqual(level, p.RiskLevel);
            }
        }

        // ── GetAboveThreshold ───────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetAboveThreshold_NegativeThreshold_Throws()
            => _service.GetAboveThreshold(-1, Now);

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetAboveThreshold_Over100_Throws()
            => _service.GetAboveThreshold(101, Now);

        [TestMethod]
        public void GetAboveThreshold_FiltersCorrectly()
        {
            var results = _service.GetAboveThreshold(30, Now);
            foreach (var p in results)
                Assert.IsTrue(p.RiskScore >= 30);
        }

        [TestMethod]
        public void GetAboveThreshold_100_ReturnsOnlyMax()
        {
            var results = _service.GetAboveThreshold(100, Now);
            foreach (var p in results)
                Assert.AreEqual(100, p.RiskScore);
        }

        // ── GetWinnableCustomers ────────────────────────────────────

        [TestMethod]
        public void GetWinnableCustomers_CorrectRiskLevels()
        {
            var results = _service.GetWinnableCustomers(Now, minLifetimeRentals: 1);
            foreach (var p in results)
            {
                Assert.IsTrue(p.RiskLevel == ChurnRisk.Medium || p.RiskLevel == ChurnRisk.High);
            }
        }

        [TestMethod]
        public void GetWinnableCustomers_SortedBySpendDescending()
        {
            var results = _service.GetWinnableCustomers(Now, minLifetimeRentals: 1);
            if (results.Count >= 2)
            {
                for (int i = 1; i < results.Count; i++)
                    Assert.IsTrue(results[i - 1].TotalSpend >= results[i].TotalSpend);
            }
        }

        [TestMethod]
        public void GetWinnableCustomers_RespectsMinRentals()
        {
            var results = _service.GetWinnableCustomers(Now, minLifetimeRentals: 5);
            foreach (var p in results)
                Assert.IsTrue(p.TotalRentals >= 5);
        }

        // ── CompareOverTime ─────────────────────────────────────────

        [TestMethod]
        public void CompareOverTime_AllCategoriesValid()
        {
            var c = AddCustomer("Tracked");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-200));
            AddRental(c.Id, m.Id, Now.AddDays(-195));

            var earlier = Now.AddDays(-100);
            var later = Now;

            var (improved, worsened, stable) = _service.CompareOverTime(earlier, later);
            // All returned profiles should have valid data
            foreach (var mv in improved.Concat(worsened).Concat(stable))
            {
                Assert.IsTrue(mv.PreviousScore >= 0 && mv.PreviousScore <= 100);
                Assert.IsTrue(mv.CurrentScore >= 0 && mv.CurrentScore <= 100);
            }
        }

        [TestMethod]
        public void CompareOverTime_DeltaCalculated()
        {
            var c = AddCustomer("Test Delta");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-100));

            var (improved, worsened, stable) = _service.CompareOverTime(Now.AddDays(-50), Now);
            var all = improved.Concat(worsened).Concat(stable).ToList();
            foreach (var mv in all)
            {
                Assert.AreEqual(Math.Round(mv.CurrentScore - mv.PreviousScore, 2), mv.Delta);
            }
        }

        [TestMethod]
        public void CompareOverTime_HighThreshold_AllStable()
        {
            var c = AddCustomer("Stable Test");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-50));

            var (improved, worsened, stable) = _service.CompareOverTime(
                Now.AddDays(-10), Now, significantChange: 999);
            Assert.AreEqual(0, improved.Count);
            Assert.AreEqual(0, worsened.Count);
        }

        // ── Retention Actions ───────────────────────────────────────

        [TestMethod]
        public void RetentionActions_LongInactive_WeMissYou()
        {
            var c = AddCustomer("Gone");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-100));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.RetentionActions.Any(a => a.Contains("miss you")));
        }

        [TestMethod]
        public void RetentionActions_HighValueAtRisk_PersonalOutreach()
        {
            var c = AddCustomer("VIP");
            var m = AddMovie("Movie");
            for (int i = 0; i < 5; i++)
                AddRental(c.Id, m.Id, Now.AddDays(-160 + i * 2), dailyRate: 30m);

            var profile = _service.Analyze(c.Id, Now);
            if (profile.TotalSpend > 100 && profile.RiskLevel >= ChurnRisk.High)
                Assert.IsTrue(profile.RetentionActions.Any(a => a.Contains("personal outreach")));
        }

        [TestMethod]
        public void RetentionActions_NewCustomer_Onboarding()
        {
            var c = AddCustomer("Newbie");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-5));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.RetentionActions.Any(a => a.Contains("onboarding")));
        }

        [TestMethod]
        public void RetentionActions_HighLateRate_ExtendPeriod()
        {
            var c = AddCustomer("Always Late");
            var m = AddMovie("Movie");
            // Use the helper with late: true
            for (int i = 0; i < 5; i++)
                AddRental(c.Id, m.Id, Now.AddDays(-10 * (5 - i)), late: true);

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.LateReturnRate > 0.3,
                $"Expected late rate > 0.3, got {profile.LateReturnRate}");
            Assert.IsTrue(profile.RetentionActions.Any(a =>
                a.Contains("rental period") || a.Contains("auto-renewal")),
                "Should suggest extending rental periods");
        }

        [TestMethod]
        public void RetentionActions_BasicWithManyRentals_UpgradeOffer()
        {
            var c = AddCustomer("Loyal Basic", MembershipType.Basic);
            var m = AddMovie("Movie");
            for (int i = 0; i < 12; i++)
                AddRental(c.Id, m.Id, Now.AddDays(-5 * (12 - i)));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.RetentionActions.Any(a => a.Contains("Silver membership")));
        }

        [TestMethod]
        public void RetentionActions_SingleGenre_CrossGenreSuggestion()
        {
            var c = AddCustomer("Genre Lock");
            var m = AddMovie("Action Only", Genre.Action);
            for (int i = 0; i < 5; i++)
                AddRental(c.Id, m.Id, Now.AddDays(-5 * (5 - i)));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.RetentionActions.Any(a => a.Contains("cross-genre")));
        }

        [TestMethod]
        public void RetentionActions_MediumInactive_NewArrivals()
        {
            var c = AddCustomer("Medium Inactive");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-65));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.RetentionActions.Any(a => a.Contains("new arrivals")));
        }

        // ── ChurnConfig ────────────────────────────────────────────

        [TestMethod]
        public void ChurnConfig_DefaultIsValid()
        {
            var config = new ChurnConfig();
            Assert.IsTrue(config.IsValid());
        }

        [TestMethod]
        public void ChurnConfig_InvalidWeights_NotValid()
        {
            var config = new ChurnConfig { RecencyWeight = 0.9 };
            Assert.IsFalse(config.IsValid());
        }

        [TestMethod]
        public void ChurnConfig_CustomThresholds_AffectClassification()
        {
            var config = new ChurnConfig
            {
                LowThreshold = 10,
                MediumThreshold = 20,
                HighThreshold = 30
            };
            var svc = new ChurnPredictorService(_customerRepo, _rentalRepo, _movieRepo, config);

            var c = AddCustomer("Test");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-60));

            var profile = svc.Analyze(c.Id, Now);
            Assert.IsTrue(profile.RiskLevel >= ChurnRisk.Medium);
        }

        // ── Edge Cases ──────────────────────────────────────────────

        [TestMethod]
        public void Analyze_CustomerWithSingleRental_Works()
        {
            var c = AddCustomer("Solo Sam");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-10));

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsNotNull(profile);
            Assert.AreEqual(1, profile.TotalRentals);
            Assert.AreEqual(0, profile.AvgDaysBetweenRentals);
        }

        [TestMethod]
        public void Analyze_AllRentalsSameDay_ZeroGap()
        {
            var c = AddCustomer("Binge Billy");
            var m1 = AddMovie("Movie 1");
            var m2 = AddMovie("Movie 2");
            var m3 = AddMovie("Movie 3");
            AddRental(c.Id, m1.Id, Now.AddDays(-10));
            AddRental(c.Id, m2.Id, Now.AddDays(-10));
            AddRental(c.Id, m3.Id, Now.AddDays(-10));

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(0, profile.AvgDaysBetweenRentals);
        }

        [TestMethod]
        public void Analyze_MovieNotInRepo_HandledGracefully()
        {
            var c = AddCustomer("Test");
            var r = new Rental
            {
                Id = _nextRentalId++,
                CustomerId = c.Id,
                MovieId = 99999,
                RentalDate = Now.AddDays(-10),
                DueDate = Now.AddDays(-3),
                DailyRate = 3.99m,
                ReturnDate = Now.AddDays(-7),
                Status = RentalStatus.Returned
            };
            _rentalRepo.Add(r);

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(0, profile.GenreDiversity);
        }

        [TestMethod]
        public void Analyze_MembershipTypePreserved()
        {
            var c = AddCustomer("Gold Girl", MembershipType.Gold);
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-5));

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(MembershipType.Gold, profile.MembershipType);
        }

        [TestMethod]
        public void Analyze_AvgDaysBetweenRentals_Calculated()
        {
            var c = AddCustomer("Regular");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-30));
            AddRental(c.Id, m.Id, Now.AddDays(-20));
            AddRental(c.Id, m.Id, Now.AddDays(-10));

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(10, profile.AvgDaysBetweenRentals);
        }

        [TestMethod]
        public void Analyze_RiskClassification_Low()
        {
            var c = AddCustomer("Test Low");
            var genres = new[] { Genre.Action, Genre.Comedy, Genre.Drama, Genre.Horror, Genre.SciFi, Genre.Animation };
            for (int i = 0; i < 25; i++)
            {
                var m = AddMovie($"M{i}", genres[i % genres.Length]);
                AddRental(c.Id, m.Id, Now.AddDays(-2 * (25 - i)));
            }

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual(ChurnRisk.Low, profile.RiskLevel);
        }

        [TestMethod]
        public void Analyze_CustomerName_Set()
        {
            var c = AddCustomer("Named Nora");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-5));

            var profile = _service.Analyze(c.Id, Now);
            Assert.AreEqual("Named Nora", profile.CustomerName);
        }

        [TestMethod]
        public void Analyze_TotalSpend_Calculated()
        {
            var c = AddCustomer("Spender");
            var m = AddMovie("Movie");
            AddRental(c.Id, m.Id, Now.AddDays(-10), dailyRate: 5m);

            var profile = _service.Analyze(c.Id, Now);
            Assert.IsTrue(profile.TotalSpend > 0);
        }

        [TestMethod]
        public void GetSummary_TierStats_CountsMatchTotal()
        {
            var summary = _service.GetSummary(Now);
            var tierTotal = summary.ByTier.Values.Sum(t => t.Count);
            Assert.AreEqual(summary.TotalCustomersAnalyzed, tierTotal);
        }

        [TestMethod]
        public void GetAboveThreshold_ZeroThreshold_ReturnsAll()
        {
            var all = _service.AnalyzeAll(Now);
            var results = _service.GetAboveThreshold(0, Now);
            Assert.AreEqual(all.Count, results.Count);
        }
    }
}
