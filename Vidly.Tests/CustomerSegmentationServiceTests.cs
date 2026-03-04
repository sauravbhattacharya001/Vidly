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
    public class CustomerSegmentationServiceTests
    {
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryRentalRepository _rentalRepo;
        private CustomerSegmentationService _service;
        private static readonly DateTime Now = DateTime.Today;

        [TestInitialize]
        public void Setup()
        {
            _customerRepo = new InMemoryCustomerRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _service = new CustomerSegmentationService(_customerRepo, _rentalRepo);
        }

        private int _nextCustId = 5000;
        private int _nextRentalId = 5000;

        private Customer AddCustomer(string name)
        {
            var c = new Customer { Id = _nextCustId++, Name = name, MembershipType = MembershipType.Basic };
            _customerRepo.Add(c);
            return c;
        }

        private Rental AddRental(int customerId, int movieId, DateTime rentalDate, decimal dailyRate = 3.99m, DateTime? returnDate = null)
        {
            var r = new Rental
            {
                Id = _nextRentalId++,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                DailyRate = dailyRate,
                ReturnDate = returnDate ?? rentalDate.AddDays(3),
                Status = RentalStatus.Returned
            };
            _rentalRepo.Add(r);
            return r;
        }

        // ── Constructor ─────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullCustomerRepo_Throws()
        {
            new CustomerSegmentationService(null, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullRentalRepo_Throws()
        {
            new CustomerSegmentationService(_customerRepo, null);
        }

        // ── AnalyzeAll ──────────────────────────────────────────────

        [TestMethod]
        public void AnalyzeAll_IncludesSeededAndNewCustomers()
        {
            // Pre-seeded data exists; just verify we get results
            var result = _service.AnalyzeAll(Now);
            Assert.IsTrue(result.Count >= 1, "Should include at least seeded customers with rentals");
        }

        [TestMethod]
        public void AnalyzeAll_CustomerWithNoRentals_Excluded()
        {
            var c = AddCustomer("NoRentals");
            var result = _service.AnalyzeAll(Now);
            Assert.IsFalse(result.Any(p => p.CustomerId == c.Id),
                "Customer with no rentals should not appear");
        }

        [TestMethod]
        public void AnalyzeAll_NewCustomerWithRentals_Included()
        {
            var c = AddCustomer("Active");
            AddRental(c.Id, 9001, Now.AddDays(-5));

            var result = _service.AnalyzeAll(Now);
            Assert.IsTrue(result.Any(p => p.CustomerId == c.Id));
        }

        [TestMethod]
        public void AnalyzeAll_RecencyCalculation_Correct()
        {
            var c = AddCustomer("RecencyTest");
            AddRental(c.Id, 9002, Now.AddDays(-10));

            var result = _service.AnalyzeAll(Now);
            var profile = result.First(p => p.CustomerId == c.Id);
            Assert.AreEqual(10, profile.DaysSinceLastRental);
        }

        [TestMethod]
        public void AnalyzeAll_FrequencyCalculation_Correct()
        {
            var c = AddCustomer("FreqTest");
            AddRental(c.Id, 9003, Now.AddDays(-10));
            AddRental(c.Id, 9004, Now.AddDays(-5));
            AddRental(c.Id, 9005, Now.AddDays(-2));

            var result = _service.AnalyzeAll(Now);
            var profile = result.First(p => p.CustomerId == c.Id);
            Assert.AreEqual(3, profile.RentalCount);
        }

        [TestMethod]
        public void AnalyzeAll_MonetaryCalculation_Positive()
        {
            var c = AddCustomer("MoneyTest");
            AddRental(c.Id, 9006, Now.AddDays(-10), 5.00m);
            AddRental(c.Id, 9007, Now.AddDays(-5), 3.00m);

            var result = _service.AnalyzeAll(Now);
            var profile = result.First(p => p.CustomerId == c.Id);
            Assert.IsTrue(profile.TotalSpend > 0);
        }

        [TestMethod]
        public void AnalyzeAll_OrderedByCompositeScoreDesc()
        {
            var result = _service.AnalyzeAll(Now);
            for (int i = 1; i < result.Count; i++)
                Assert.IsTrue(result[i - 1].CompositeScore >= result[i].CompositeScore,
                    "Results should be ordered by composite score descending");
        }

        [TestMethod]
        public void AnalyzeAll_RfmCode_FormattedCorrectly()
        {
            var c = AddCustomer("CodeTest");
            AddRental(c.Id, 9008, Now.AddDays(-1));

            var result = _service.AnalyzeAll(Now);
            var profile = result.First(p => p.CustomerId == c.Id);
            var parts = profile.RfmCode.Split('-');
            Assert.AreEqual(3, parts.Length);
            foreach (var p in parts)
                Assert.IsTrue(int.TryParse(p, out _));
        }

        [TestMethod]
        public void AnalyzeAll_ScoresWithinRange()
        {
            // Add enough customers for varied scores
            for (int i = 0; i < 15; i++)
            {
                var c = AddCustomer($"Range{i}");
                for (int j = 0; j <= i; j++)
                    AddRental(c.Id, 9100 + i * 20 + j, Now.AddDays(-(i + 1) * 5), (i + 1) * 2m);
            }

            var result = _service.AnalyzeAll(Now);
            foreach (var p in result)
            {
                Assert.IsTrue(p.RecencyScore >= 1 && p.RecencyScore <= 5, $"R={p.RecencyScore}");
                Assert.IsTrue(p.FrequencyScore >= 1 && p.FrequencyScore <= 5, $"F={p.FrequencyScore}");
                Assert.IsTrue(p.MonetaryScore >= 1 && p.MonetaryScore <= 5, $"M={p.MonetaryScore}");
            }
        }

        [TestMethod]
        public void AnalyzeAll_CompositeIsAverageOfRFM()
        {
            var c = AddCustomer("AvgTest");
            AddRental(c.Id, 9200, Now.AddDays(-5));

            var result = _service.AnalyzeAll(Now);
            var p = result.First(pr => pr.CustomerId == c.Id);
            var expected = (p.RecencyScore + p.FrequencyScore + p.MonetaryScore) / 3.0;
            Assert.AreEqual(expected, p.CompositeScore, 0.001);
        }

        [TestMethod]
        public void AnalyzeAll_SegmentAssigned()
        {
            var c = AddCustomer("SegTest");
            AddRental(c.Id, 9201, Now.AddDays(-1));

            var result = _service.AnalyzeAll(Now);
            var p = result.First(pr => pr.CustomerId == c.Id);
            Assert.IsTrue(Enum.IsDefined(typeof(CustomerSegment), p.Segment));
        }

        [TestMethod]
        public void AnalyzeAll_HighVsLowActivity_ScoreDifference()
        {
            var heavy = AddCustomer("HeavyUser");
            for (int i = 0; i < 15; i++)
                AddRental(heavy.Id, 9300 + i, Now.AddDays(-1 - i), 10m);

            var light = AddCustomer("LightUser");
            AddRental(light.Id, 9400, Now.AddDays(-300), 1m);

            var result = _service.AnalyzeAll(Now);
            var heavyP = result.First(p => p.CustomerId == heavy.Id);
            var lightP = result.First(p => p.CustomerId == light.Id);
            Assert.IsTrue(heavyP.CompositeScore > lightP.CompositeScore);
        }

        [TestMethod]
        public void AnalyzeAll_UsesLatestRentalForRecency()
        {
            var c = AddCustomer("LatestTest");
            AddRental(c.Id, 9401, Now.AddDays(-100));
            AddRental(c.Id, 9402, Now.AddDays(-2));

            var result = _service.AnalyzeAll(Now);
            var p = result.First(pr => pr.CustomerId == c.Id);
            Assert.AreEqual(2, p.DaysSinceLastRental);
        }

        [TestMethod]
        public void AnalyzeAll_ZeroDayRecency()
        {
            var c = AddCustomer("TodayRenter");
            AddRental(c.Id, 9403, Now);

            var result = _service.AnalyzeAll(Now);
            var p = result.First(pr => pr.CustomerId == c.Id);
            Assert.AreEqual(0, p.DaysSinceLastRental);
        }

        // ── AnalyzeCustomer ─────────────────────────────────────────

        [TestMethod]
        public void AnalyzeCustomer_Found_ReturnsProfile()
        {
            var c = AddCustomer("FindMe");
            AddRental(c.Id, 9500, Now.AddDays(-5));

            var result = _service.AnalyzeCustomer(c.Id, Now);
            Assert.IsNotNull(result);
            Assert.AreEqual(c.Id, result.CustomerId);
        }

        [TestMethod]
        public void AnalyzeCustomer_NotFound_ReturnsNull()
        {
            var result = _service.AnalyzeCustomer(99999, Now);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void AnalyzeCustomer_NoRentals_ReturnsNull()
        {
            var c = AddCustomer("NoRentals2");
            var result = _service.AnalyzeCustomer(c.Id, Now);
            Assert.IsNull(result);
        }

        // ── GetBySegment ────────────────────────────────────────────

        [TestMethod]
        public void GetBySegment_ReturnsOnlyMatchingSegment()
        {
            var result = _service.AnalyzeAll(Now);
            if (result.Count == 0) return;

            var firstSegment = result[0].Segment;
            var filtered = _service.GetBySegment(firstSegment, Now);
            Assert.IsTrue(filtered.All(p => p.Segment == firstSegment));
        }

        [TestMethod]
        public void GetBySegment_EmptyForUnusedSegment()
        {
            // At least one segment should be empty with few customers
            var allSegments = Enum.GetValues(typeof(CustomerSegment)).Cast<CustomerSegment>();
            var emptyCounts = allSegments.Count(s => _service.GetBySegment(s, Now).Count == 0);
            Assert.IsTrue(emptyCounts > 0, "Some segments should be empty");
        }

        // ── GetSummary ──────────────────────────────────────────────

        [TestMethod]
        public void GetSummary_TotalMatchesAnalyzeAll()
        {
            var profiles = _service.AnalyzeAll(Now);
            var summary = _service.GetSummary(Now);
            Assert.AreEqual(profiles.Count, summary.TotalCustomersAnalyzed);
        }

        [TestMethod]
        public void GetSummary_AsOfDateSet()
        {
            var summary = _service.GetSummary(Now);
            Assert.AreEqual(Now, summary.AsOfDate);
        }

        [TestMethod]
        public void GetSummary_DistributionCoversAllSegments()
        {
            var summary = _service.GetSummary(Now);
            foreach (CustomerSegment seg in Enum.GetValues(typeof(CustomerSegment)))
                Assert.IsTrue(summary.Distribution.ContainsKey(seg), $"Missing segment: {seg}");
        }

        [TestMethod]
        public void GetSummary_DistributionCountsSumToTotal()
        {
            var summary = _service.GetSummary(Now);
            var total = summary.Distribution.Values.Sum(s => s.Count);
            Assert.AreEqual(summary.TotalCustomersAnalyzed, total);
        }

        [TestMethod]
        public void GetSummary_EmptySegmentHasZeroStats()
        {
            var summary = _service.GetSummary(Now);
            var emptySeg = summary.Distribution.FirstOrDefault(kv => kv.Value.Count == 0);
            if (emptySeg.Value == null) return; // all segments populated (unlikely)
            Assert.AreEqual(0m, emptySeg.Value.AverageSpend);
            Assert.AreEqual(0.0, emptySeg.Value.AverageFrequency);
            Assert.AreEqual(0.0, emptySeg.Value.AverageRecencyDays);
        }

        // ── GetAtRiskCustomers ──────────────────────────────────────

        [TestMethod]
        public void GetAtRiskCustomers_ReturnsOnlyRiskSegments()
        {
            // Add some old customers
            for (int i = 0; i < 10; i++)
            {
                var c = AddCustomer($"OldCust{i}");
                for (int j = 0; j <= i; j++)
                    AddRental(c.Id, 9600 + i * 20 + j, Now.AddDays(-(i + 1) * 30), (i + 1) * 2m);
            }

            var atRisk = _service.GetAtRiskCustomers(Now);
            var validSegments = new[] { CustomerSegment.AtRisk, CustomerSegment.CantLoseThem, CustomerSegment.Hibernating };
            Assert.IsTrue(atRisk.All(p => validSegments.Contains(p.Segment)));
        }

        [TestMethod]
        public void GetAtRiskCustomers_OrderedByRecencyAsc()
        {
            for (int i = 0; i < 10; i++)
            {
                var c = AddCustomer($"RiskOrder{i}");
                for (int j = 0; j <= i; j++)
                    AddRental(c.Id, 9800 + i * 20 + j, Now.AddDays(-(i + 1) * 20));
            }

            var atRisk = _service.GetAtRiskCustomers(Now);
            for (int i = 1; i < atRisk.Count; i++)
                Assert.IsTrue(atRisk[i - 1].RecencyScore <= atRisk[i].RecencyScore);
        }

        // ── GetMarketingRecommendations ─────────────────────────────

        [TestMethod]
        public void GetMarketingRecommendations_CoversAllSegments()
        {
            var recs = _service.GetMarketingRecommendations();
            foreach (CustomerSegment seg in Enum.GetValues(typeof(CustomerSegment)))
                Assert.IsTrue(recs.ContainsKey(seg), $"Missing recommendation for: {seg}");
        }

        [TestMethod]
        public void GetMarketingRecommendations_NoEmptyStrings()
        {
            var recs = _service.GetMarketingRecommendations();
            foreach (var kv in recs)
                Assert.IsFalse(string.IsNullOrWhiteSpace(kv.Value), $"Empty rec for {kv.Key}");
        }

        [TestMethod]
        public void GetMarketingRecommendations_Has11Entries()
        {
            var recs = _service.GetMarketingRecommendations();
            Assert.AreEqual(11, recs.Count);
        }

        // ── CompareSegments ─────────────────────────────────────────

        [TestMethod]
        public void CompareSegments_SamePeriod_NoMigrations()
        {
            var migrations = _service.CompareSegments(Now, Now);
            Assert.AreEqual(0, migrations.Count);
        }

        [TestMethod]
        public void CompareSegments_DifferentPeriods_DoesNotThrow()
        {
            var migrations = _service.CompareSegments(Now.AddDays(-60), Now);
            Assert.IsNotNull(migrations);
        }

        [TestMethod]
        public void CompareSegments_OrderedByAbsScoreChange()
        {
            // Add varied data
            for (int i = 0; i < 8; i++)
            {
                var c = AddCustomer($"Migrant{i}");
                AddRental(c.Id, 9900 + i, Now.AddDays(-i * 30));
                if (i < 4) AddRental(c.Id, 9950 + i, Now.AddDays(-1));
            }

            var migrations = _service.CompareSegments(Now.AddDays(-90), Now);
            for (int i = 1; i < migrations.Count; i++)
                Assert.IsTrue(Math.Abs(migrations[i - 1].ScoreChange) >= Math.Abs(migrations[i].ScoreChange));
        }

        [TestMethod]
        public void CompareSegments_MigrationsHaveDifferentSegments()
        {
            for (int i = 0; i < 8; i++)
            {
                var c = AddCustomer($"MigCheck{i}");
                AddRental(c.Id, 10000 + i, Now.AddDays(-i * 30));
                if (i < 3) AddRental(c.Id, 10050 + i, Now.AddDays(-1));
            }

            var migrations = _service.CompareSegments(Now.AddDays(-120), Now);
            foreach (var m in migrations)
                Assert.AreNotEqual(m.FromSegment, m.ToSegment);
        }

        // ── ClassifySegment ─────────────────────────────────────────

        [TestMethod]
        public void ClassifySegment_Champions_HighAll()
        {
            Assert.AreEqual(CustomerSegment.Champions,
                CustomerSegmentationService.ClassifySegment(5, 5, 5));
        }

        [TestMethod]
        public void ClassifySegment_Champions_FourPlus()
        {
            Assert.AreEqual(CustomerSegment.Champions,
                CustomerSegmentationService.ClassifySegment(4, 4, 4));
        }

        [TestMethod]
        public void ClassifySegment_LoyalCustomers()
        {
            Assert.AreEqual(CustomerSegment.LoyalCustomers,
                CustomerSegmentationService.ClassifySegment(3, 5, 4));
        }

        [TestMethod]
        public void ClassifySegment_NewCustomers()
        {
            Assert.AreEqual(CustomerSegment.NewCustomers,
                CustomerSegmentationService.ClassifySegment(5, 1, 1));
        }

        [TestMethod]
        public void ClassifySegment_Lost()
        {
            Assert.AreEqual(CustomerSegment.Lost,
                CustomerSegmentationService.ClassifySegment(1, 1, 1));
        }

        [TestMethod]
        public void ClassifySegment_AtRisk()
        {
            Assert.AreEqual(CustomerSegment.AtRisk,
                CustomerSegmentationService.ClassifySegment(2, 1, 1));
        }

        [TestMethod]
        public void ClassifySegment_Hibernating()
        {
            Assert.AreEqual(CustomerSegment.Hibernating,
                CustomerSegmentationService.ClassifySegment(1, 3, 1));
        }

        [TestMethod]
        public void ClassifySegment_NeedAttention()
        {
            Assert.AreEqual(CustomerSegment.NeedAttention,
                CustomerSegmentationService.ClassifySegment(3, 1, 1));
        }

        [TestMethod]
        public void ClassifySegment_PotentialLoyalists()
        {
            Assert.AreEqual(CustomerSegment.PotentialLoyalists,
                CustomerSegmentationService.ClassifySegment(4, 3, 1));
        }

        [TestMethod]
        public void ClassifySegment_AboutToSleep()
        {
            Assert.AreEqual(CustomerSegment.AboutToSleep,
                CustomerSegmentationService.ClassifySegment(2, 2, 1));
        }

        [TestMethod]
        public void ClassifySegment_CantLoseThem()
        {
            Assert.AreEqual(CustomerSegment.CantLoseThem,
                CustomerSegmentationService.ClassifySegment(1, 5, 1));
        }

        [TestMethod]
        public void ClassifySegment_Promising()
        {
            Assert.AreEqual(CustomerSegment.Promising,
                CustomerSegmentationService.ClassifySegment(3, 2, 2));
        }

        // ── GetQuantileBreaks ───────────────────────────────────────

        [TestMethod]
        public void GetQuantileBreaks_EmptyValues_ReturnsEmpty()
        {
            var breaks = CustomerSegmentationService.GetQuantileBreaks(new List<double>(), 5);
            Assert.AreEqual(0, breaks.Count);
        }

        [TestMethod]
        public void GetQuantileBreaks_ReturnsNMinus1Breaks()
        {
            var values = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
            var breaks = CustomerSegmentationService.GetQuantileBreaks(values, 5);
            Assert.AreEqual(4, breaks.Count);
        }

        [TestMethod]
        public void GetQuantileBreaks_BreaksAreOrdered()
        {
            var values = Enumerable.Range(1, 50).Select(i => (double)i).ToList();
            var breaks = CustomerSegmentationService.GetQuantileBreaks(values, 5);
            for (int i = 1; i < breaks.Count; i++)
                Assert.IsTrue(breaks[i] >= breaks[i - 1]);
        }

        [TestMethod]
        public void GetQuantileBreaks_SingleValue_AllSameBreaks()
        {
            var values = Enumerable.Repeat(42.0, 10).ToList();
            var breaks = CustomerSegmentationService.GetQuantileBreaks(values, 5);
            Assert.AreEqual(4, breaks.Count);
            Assert.IsTrue(breaks.All(b => b == 42.0));
        }

        // ── AssignBucket ────────────────────────────────────────────

        [TestMethod]
        public void AssignBucket_BelowFirst_ReturnsBucket1()
        {
            var breaks = new List<double> { 10, 20, 30, 40 };
            Assert.AreEqual(1, CustomerSegmentationService.AssignBucket(5, breaks));
        }

        [TestMethod]
        public void AssignBucket_AboveAll_ReturnsHighest()
        {
            var breaks = new List<double> { 10, 20, 30, 40 };
            Assert.AreEqual(5, CustomerSegmentationService.AssignBucket(50, breaks));
        }

        [TestMethod]
        public void AssignBucket_OnBreakpoint_AssignsLowerBucket()
        {
            var breaks = new List<double> { 10, 20, 30, 40 };
            Assert.AreEqual(2, CustomerSegmentationService.AssignBucket(20, breaks));
        }

        [TestMethod]
        public void AssignBucket_EmptyBreaks_Returns1()
        {
            Assert.AreEqual(1, CustomerSegmentationService.AssignBucket(99, new List<double>()));
        }

        [TestMethod]
        public void AssignBucket_ExactlyOnFirstBreak()
        {
            var breaks = new List<double> { 10, 20, 30, 40 };
            Assert.AreEqual(1, CustomerSegmentationService.AssignBucket(10, breaks));
        }

        // ── Integration ─────────────────────────────────────────────

        [TestMethod]
        public void Integration_FullPipeline()
        {
            // Add 15 customers with varied rental patterns
            var rng = new Random(42);
            for (int i = 0; i < 15; i++)
            {
                var c = AddCustomer($"IntegCust{i}");
                var rentalCount = rng.Next(1, 12);
                for (int j = 0; j < rentalCount; j++)
                {
                    var daysAgo = rng.Next(1, 365);
                    AddRental(c.Id, 10100 + i * 20 + j, Now.AddDays(-daysAgo), (decimal)(rng.NextDouble() * 10 + 1));
                }
            }

            var profiles = _service.AnalyzeAll(Now);
            Assert.IsTrue(profiles.Count >= 15);

            var summary = _service.GetSummary(Now);
            Assert.AreEqual(profiles.Count, summary.TotalCustomersAnalyzed);
            Assert.IsTrue(summary.Distribution.Values.Sum(s => s.Count) == profiles.Count);

            var atRisk = _service.GetAtRiskCustomers(Now);
            Assert.IsNotNull(atRisk);

            var recs = _service.GetMarketingRecommendations();
            Assert.AreEqual(11, recs.Count);

            var migrations = _service.CompareSegments(Now.AddDays(-180), Now);
            Assert.IsNotNull(migrations);
        }

        [TestMethod]
        public void Integration_SingleCustomerAnalysis()
        {
            var c = AddCustomer("VIPCustomer");
            for (int i = 0; i < 10; i++)
                AddRental(c.Id, 10500 + i, Now.AddDays(-i), 15m);

            var profile = _service.AnalyzeCustomer(c.Id, Now);
            Assert.IsNotNull(profile);
            Assert.AreEqual("VIPCustomer", profile.CustomerName);
            Assert.AreEqual(10, profile.RentalCount);
            Assert.IsTrue(profile.TotalSpend > 0);
            Assert.IsTrue(profile.CompositeScore > 0);
        }
    }
}
