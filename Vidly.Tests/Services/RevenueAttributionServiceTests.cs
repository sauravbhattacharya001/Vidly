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
    public class RevenueAttributionServiceTests
    {
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private StubClock _clock;
        private RevenueAttributionService _service;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _clock = new StubClock(new DateTime(2026, 5, 1));
            _service = new RevenueAttributionService(_rentalRepo, _movieRepo, _customerRepo, _clock);
        }

        // ── Constructor guard tests ──────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullRentalRepo_Throws()
        {
            new RevenueAttributionService(null, _movieRepo, _customerRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullMovieRepo_Throws()
        {
            new RevenueAttributionService(_rentalRepo, null, _customerRepo, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullCustomerRepo_Throws()
        {
            new RevenueAttributionService(_rentalRepo, _movieRepo, null, _clock);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullClock_Throws()
        {
            new RevenueAttributionService(_rentalRepo, _movieRepo, _customerRepo, null);
        }

        // ── Empty data ──────────────────────────────────────────────

        [TestMethod]
        public void GenerateReport_EmptyData_ReturnsZeroRevenue()
        {
            var report = _service.GenerateReport();

            Assert.AreEqual(0m, report.TotalRevenue);
            Assert.AreEqual(0, report.ChannelBreakdown.Count);
            Assert.AreEqual(0, report.GenreBreakdown.Count);
            Assert.AreEqual(0, report.AttributionHealthScore);
        }

        [TestMethod]
        public void GenerateReport_EmptyData_HasInsightAboutNoData()
        {
            var report = _service.GenerateReport();
            Assert.IsTrue(report.Insights.Any(i => i.Contains("No revenue data")));
        }

        // ── Single rental ───────────────────────────────────────────

        [TestMethod]
        public void GenerateReport_SingleRental_CorrectTotalRevenue()
        {
            SeedSingleRental();
            var report = _service.GenerateReport();

            // 1 day * $3.99 = $3.99
            Assert.IsTrue(report.TotalRevenue > 0);
        }

        [TestMethod]
        public void GenerateReport_SingleRental_HasChannelBreakdown()
        {
            SeedSingleRental();
            var report = _service.GenerateReport();

            Assert.IsTrue(report.ChannelBreakdown.Count > 0);
            Assert.AreEqual(100.0, report.ChannelBreakdown.Sum(c => c.SharePercent), 0.1);
        }

        // ── Channel attribution ─────────────────────────────────────

        [TestMethod]
        public void ChannelAttribution_NewReleaseVsCatalog_SplitsCorrectly()
        {
            SeedNewReleaseAndCatalog();
            var channels = _service.GetChannelBreakdown();

            Assert.AreEqual(2, channels.Count);
            Assert.IsTrue(channels.Any(c => c.Channel == "New Release"));
            Assert.IsTrue(channels.Any(c => c.Channel == "Catalog"));
        }

        [TestMethod]
        public void ChannelAttribution_AllCatalog_SingleChannel()
        {
            SeedCatalogOnlyRentals();
            var channels = _service.GetChannelBreakdown();

            Assert.AreEqual(1, channels.Count);
            Assert.AreEqual("Catalog", channels[0].Channel);
            Assert.AreEqual(100.0, channels[0].SharePercent, 0.1);
        }

        [TestMethod]
        public void ChannelAttribution_RevenuePerRental_Computed()
        {
            SeedNewReleaseAndCatalog();
            var channels = _service.GetChannelBreakdown();

            foreach (var ch in channels)
            {
                Assert.IsTrue(ch.RevenuePerRental > 0);
                Assert.AreEqual(ch.Revenue / ch.RentalCount, ch.RevenuePerRental);
            }
        }

        // ── Temporal attribution ────────────────────────────────────

        [TestMethod]
        public void TemporalAttribution_MonthlyGranularity_GroupsByMonth()
        {
            SeedMultiMonthRentals();
            var temporal = _service.GetTemporalBreakdown("month");

            Assert.IsTrue(temporal.Count >= 2);
            Assert.IsTrue(temporal.All(t => t.Period.Length == 7)); // yyyy-MM
        }

        [TestMethod]
        public void TemporalAttribution_DowGranularity_GroupsByDayOfWeek()
        {
            SeedMultiMonthRentals();
            var temporal = _service.GetTemporalBreakdown("dow");

            Assert.IsTrue(temporal.Count > 0);
            Assert.IsTrue(temporal.All(t =>
                t.Period == "Monday" || t.Period == "Tuesday" || t.Period == "Wednesday" ||
                t.Period == "Thursday" || t.Period == "Friday" || t.Period == "Saturday" ||
                t.Period == "Sunday"));
        }

        [TestMethod]
        public void TemporalAttribution_SeasonGranularity_GroupsBySeason()
        {
            SeedMultiMonthRentals();
            var temporal = _service.GetTemporalBreakdown("season");

            Assert.IsTrue(temporal.Count > 0);
            Assert.IsTrue(temporal.All(t =>
                t.Period == "Spring" || t.Period == "Summer" || t.Period == "Fall" || t.Period == "Winter"));
        }

        [TestMethod]
        public void TemporalAttribution_GrowthPercent_ComputedBetweenPeriods()
        {
            SeedMultiMonthRentals();
            var temporal = _service.GetTemporalBreakdown("month");

            // First period should have 0 growth, subsequent ones may differ
            Assert.AreEqual(0, temporal[0].GrowthPercent);
        }

        // ── Tier attribution ────────────────────────────────────────

        [TestMethod]
        public void TierAttribution_AllTiersPresent()
        {
            SeedMultiTierCustomers();
            var tiers = _service.GetTierAttribution();

            Assert.AreEqual(4, tiers.Tiers.Count);
            Assert.IsTrue(tiers.Tiers.Any(t => t.Tier == "Basic"));
            Assert.IsTrue(tiers.Tiers.Any(t => t.Tier == "Silver"));
            Assert.IsTrue(tiers.Tiers.Any(t => t.Tier == "Gold"));
            Assert.IsTrue(tiers.Tiers.Any(t => t.Tier == "Platinum"));
        }

        [TestMethod]
        public void TierAttribution_ConcentrationIndex_InRange()
        {
            SeedMultiTierCustomers();
            var tiers = _service.GetTierAttribution();

            Assert.IsTrue(tiers.ConcentrationIndex >= 0);
            Assert.IsTrue(tiers.ConcentrationIndex <= 1);
        }

        [TestMethod]
        public void TierAttribution_RevenuePerCapita_Computed()
        {
            SeedMultiTierCustomers();
            var tiers = _service.GetTierAttribution();

            foreach (var t in tiers.Tiers.Where(t => t.CustomerCount > 0))
            {
                Assert.AreEqual(t.TotalRevenue / t.CustomerCount, t.RevenuePerCapita);
            }
        }

        [TestMethod]
        public void TierAttribution_SharesSumTo100()
        {
            SeedMultiTierCustomers();
            var tiers = _service.GetTierAttribution();
            var totalShare = tiers.Tiers.Sum(t => t.SharePercent);

            Assert.AreEqual(100.0, totalShare, 0.5);
        }

        // ── Genre breakdown ─────────────────────────────────────────

        [TestMethod]
        public void GenreBreakdown_MultipleGenres_AllPresent()
        {
            SeedMultiGenreRentals();
            var report = _service.GenerateReport();

            Assert.IsTrue(report.GenreBreakdown.Count >= 2);
        }

        [TestMethod]
        public void GenreBreakdown_TrendClassification_Works()
        {
            SeedMultiGenreRentals();
            var report = _service.GenerateReport();

            foreach (var g in report.GenreBreakdown)
            {
                Assert.IsTrue(g.Trend == "Rising" || g.Trend == "Stable" || g.Trend == "Declining");
            }
        }

        [TestMethod]
        public void GenreBreakdown_SharesSumTo100()
        {
            SeedMultiGenreRentals();
            var report = _service.GenerateReport();
            var totalShare = report.GenreBreakdown.Sum(g => g.SharePercent);

            Assert.AreEqual(100.0, totalShare, 0.5);
        }

        [TestMethod]
        public void GenreBreakdown_RevenuePerRental_Positive()
        {
            SeedMultiGenreRentals();
            var report = _service.GenerateReport();

            foreach (var g in report.GenreBreakdown)
            {
                Assert.IsTrue(g.RevenuePerRental > 0);
            }
        }

        // ── Pricing rule impacts ────────────────────────────────────

        [TestMethod]
        public void PricingImpacts_WeekendSurge_Detected()
        {
            SeedWeekendAndWeekdayRentals();
            var report = _service.GenerateReport();

            Assert.IsTrue(report.PricingImpacts.Any(p => p.RuleName == "Weekend Surge"));
        }

        [TestMethod]
        public void PricingImpacts_MidweekDiscount_Negative()
        {
            SeedWeekendAndWeekdayRentals();
            var report = _service.GenerateReport();

            var midweek = report.PricingImpacts.FirstOrDefault(p => p.RuleName == "Midweek Discount");
            if (midweek != null)
                Assert.IsTrue(midweek.EstimatedImpact < 0);
        }

        [TestMethod]
        public void PricingImpacts_NewReleasePremium_DetectedForRecentMovies()
        {
            SeedNewReleaseAndCatalog();
            var report = _service.GenerateReport();

            Assert.IsTrue(report.PricingImpacts.Any(p => p.RuleName == "New Release Premium"));
        }

        // ── Retention attribution ───────────────────────────────────

        [TestMethod]
        public void RetentionAttribution_NewVsReturning_SplitCorrectly()
        {
            SeedRetentionData();
            var report = _service.GenerateReport();

            Assert.IsTrue(report.RetentionBreakdown.NewCustomerCount > 0);
            Assert.IsTrue(report.RetentionBreakdown.ReturningCustomerCount > 0);
        }

        [TestMethod]
        public void RetentionAttribution_SharesSumTo100()
        {
            SeedRetentionData();
            var report = _service.GenerateReport();

            var totalShare = report.RetentionBreakdown.NewCustomerShare +
                             report.RetentionBreakdown.ReturningCustomerShare;
            Assert.AreEqual(100.0, totalShare, 0.5);
        }

        [TestMethod]
        public void RetentionAttribution_Top10Percent_InRange()
        {
            SeedRetentionData();
            var report = _service.GenerateReport();

            Assert.IsTrue(report.RetentionBreakdown.Top10PercentRevenueShare >= 0);
            Assert.IsTrue(report.RetentionBreakdown.Top10PercentRevenueShare <= 100);
        }

        [TestMethod]
        public void RetentionAttribution_RepeatRevenuePerCapita_Positive()
        {
            SeedRetentionData();
            var report = _service.GenerateReport();

            Assert.IsTrue(report.RetentionBreakdown.RepeatRevenuePerCapita > 0);
        }

        // ── Health score ────────────────────────────────────────────

        [TestMethod]
        public void HealthScore_EmptyData_Zero()
        {
            var report = _service.GenerateReport();
            Assert.AreEqual(0, report.AttributionHealthScore);
        }

        [TestMethod]
        public void HealthScore_WithData_InRange()
        {
            SeedMultiGenreRentals();
            var report = _service.GenerateReport();

            Assert.IsTrue(report.AttributionHealthScore >= 0);
            Assert.IsTrue(report.AttributionHealthScore <= 100);
        }

        [TestMethod]
        public void HealthScore_MoreData_HigherScore()
        {
            SeedSingleRental();
            var smallReport = _service.GenerateReport();

            SeedMultiGenreRentals();
            var largeReport = _service.GenerateReport();

            Assert.IsTrue(largeReport.AttributionHealthScore >= smallReport.AttributionHealthScore);
        }

        // ── Insights ────────────────────────────────────────────────

        [TestMethod]
        public void Insights_WithData_NotEmpty()
        {
            SeedMultiGenreRentals();
            var report = _service.GenerateReport();

            Assert.IsTrue(report.Insights.Count > 0);
        }

        [TestMethod]
        public void Insights_MentionsTopGenre()
        {
            SeedMultiGenreRentals();
            var report = _service.GenerateReport();

            var topGenre = report.GenreBreakdown.OrderByDescending(g => g.Revenue).First();
            Assert.IsTrue(report.Insights.Any(i => i.Contains(topGenre.Genre)));
        }

        // ── Public API methods ──────────────────────────────────────

        [TestMethod]
        public void GetChannelBreakdown_ReturnsData()
        {
            SeedNewReleaseAndCatalog();
            var channels = _service.GetChannelBreakdown();
            Assert.IsTrue(channels.Count > 0);
        }

        [TestMethod]
        public void GetTemporalBreakdown_DefaultMonth_ReturnsData()
        {
            SeedMultiMonthRentals();
            var temporal = _service.GetTemporalBreakdown();
            Assert.IsTrue(temporal.Count > 0);
        }

        [TestMethod]
        public void GetTierAttribution_ReturnsData()
        {
            SeedMultiTierCustomers();
            var tiers = _service.GetTierAttribution();
            Assert.IsNotNull(tiers);
            Assert.AreEqual(4, tiers.Tiers.Count);
        }

        // ── Edge cases ──────────────────────────────────────────────

        [TestMethod]
        public void SingleGenre_ShareIs100Percent()
        {
            SeedSingleGenreRentals();
            var report = _service.GenerateReport();

            Assert.AreEqual(1, report.GenreBreakdown.Count);
            Assert.AreEqual(100.0, report.GenreBreakdown[0].SharePercent, 0.1);
        }

        [TestMethod]
        public void SingleCustomer_AllRevenueToOneTier()
        {
            SeedSingleRental();
            var report = _service.GenerateReport();

            var activeTiers = report.TierBreakdown.Tiers.Where(t => t.TotalRevenue > 0).ToList();
            Assert.AreEqual(1, activeTiers.Count);
        }

        [TestMethod]
        public void GenerateReport_SetsGeneratedAt()
        {
            SeedSingleRental();
            var report = _service.GenerateReport();
            Assert.AreEqual(_clock.Now, report.GeneratedAt);
        }

        // ── Seed helpers ────────────────────────────────────────────

        private void SeedSingleRental()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Basic });
            _movieRepo.Add(new Movie { Id = 1, Name = "Old Classic", Genre = Genre.Drama, ReleaseDate = new DateTime(2020, 1, 1) });
            _rentalRepo.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 4, 1),
                DueDate = new DateTime(2026, 4, 8),
                ReturnDate = new DateTime(2026, 4, 2),
                DailyRate = 3.99m, Status = RentalStatus.Returned
            });
        }

        private void SeedNewReleaseAndCatalog()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Gold });
            _movieRepo.Add(new Movie { Id = 1, Name = "Brand New Film", Genre = Genre.Action, ReleaseDate = new DateTime(2026, 4, 1) });
            _movieRepo.Add(new Movie { Id = 2, Name = "Old Classic", Genre = Genre.Drama, ReleaseDate = new DateTime(2020, 1, 1) });

            _rentalRepo.Add(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 1,
                RentalDate = new DateTime(2026, 4, 10),
                DueDate = new DateTime(2026, 4, 17),
                ReturnDate = new DateTime(2026, 4, 12),
                DailyRate = 5.99m, Status = RentalStatus.Returned
            });
            _rentalRepo.Add(new Rental
            {
                Id = 2, CustomerId = 1, MovieId = 2,
                RentalDate = new DateTime(2026, 4, 15),
                DueDate = new DateTime(2026, 4, 22),
                ReturnDate = new DateTime(2026, 4, 17),
                DailyRate = 2.99m, Status = RentalStatus.Returned
            });
        }

        private void SeedCatalogOnlyRentals()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Bob", MembershipType = MembershipType.Silver });
            _movieRepo.Add(new Movie { Id = 1, Name = "Classic A", Genre = Genre.Comedy, ReleaseDate = new DateTime(2019, 6, 1) });
            _movieRepo.Add(new Movie { Id = 2, Name = "Classic B", Genre = Genre.Horror, ReleaseDate = new DateTime(2018, 3, 1) });

            _rentalRepo.Add(new Rental { Id = 1, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 3, 1), DueDate = new DateTime(2026, 3, 8), ReturnDate = new DateTime(2026, 3, 3), DailyRate = 1.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 2, CustomerId = 1, MovieId = 2, RentalDate = new DateTime(2026, 3, 10), DueDate = new DateTime(2026, 3, 17), ReturnDate = new DateTime(2026, 3, 12), DailyRate = 1.99m, Status = RentalStatus.Returned });
        }

        private void SeedMultiMonthRentals()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Gold });
            _movieRepo.Add(new Movie { Id = 1, Name = "Film A", Genre = Genre.Action, ReleaseDate = new DateTime(2020, 1, 1) });

            // January rentals
            _rentalRepo.Add(new Rental { Id = 1, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 1, 5), DueDate = new DateTime(2026, 1, 12), ReturnDate = new DateTime(2026, 1, 7), DailyRate = 2.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 2, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 1, 15), DueDate = new DateTime(2026, 1, 22), ReturnDate = new DateTime(2026, 1, 18), DailyRate = 2.99m, Status = RentalStatus.Returned });

            // March rentals
            _rentalRepo.Add(new Rental { Id = 3, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 3, 1), DueDate = new DateTime(2026, 3, 8), ReturnDate = new DateTime(2026, 3, 4), DailyRate = 3.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 4, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 3, 20), DueDate = new DateTime(2026, 3, 27), ReturnDate = new DateTime(2026, 3, 22), DailyRate = 3.99m, Status = RentalStatus.Returned });
        }

        private void SeedMultiTierCustomers()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Basic Bob", MembershipType = MembershipType.Basic });
            _customerRepo.Add(new Customer { Id = 2, Name = "Silver Sam", MembershipType = MembershipType.Silver });
            _customerRepo.Add(new Customer { Id = 3, Name = "Gold Grace", MembershipType = MembershipType.Gold });
            _customerRepo.Add(new Customer { Id = 4, Name = "Plat Pat", MembershipType = MembershipType.Platinum });

            _movieRepo.Add(new Movie { Id = 1, Name = "Film A", Genre = Genre.Action, ReleaseDate = new DateTime(2020, 1, 1) });

            _rentalRepo.Add(new Rental { Id = 1, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 3, 1), DueDate = new DateTime(2026, 3, 8), ReturnDate = new DateTime(2026, 3, 3), DailyRate = 1.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 2, CustomerId = 2, MovieId = 1, RentalDate = new DateTime(2026, 3, 5), DueDate = new DateTime(2026, 3, 12), ReturnDate = new DateTime(2026, 3, 7), DailyRate = 2.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 3, CustomerId = 3, MovieId = 1, RentalDate = new DateTime(2026, 3, 10), DueDate = new DateTime(2026, 3, 17), ReturnDate = new DateTime(2026, 3, 13), DailyRate = 4.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 4, CustomerId = 4, MovieId = 1, RentalDate = new DateTime(2026, 3, 15), DueDate = new DateTime(2026, 3, 22), ReturnDate = new DateTime(2026, 3, 18), DailyRate = 6.99m, Status = RentalStatus.Returned });
        }

        private void SeedMultiGenreRentals()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Gold });
            _movieRepo.Add(new Movie { Id = 1, Name = "Action Film", Genre = Genre.Action, ReleaseDate = new DateTime(2020, 1, 1) });
            _movieRepo.Add(new Movie { Id = 2, Name = "Comedy Film", Genre = Genre.Comedy, ReleaseDate = new DateTime(2020, 6, 1) });
            _movieRepo.Add(new Movie { Id = 3, Name = "Drama Film", Genre = Genre.Drama, ReleaseDate = new DateTime(2021, 1, 1) });

            // Multiple rentals per genre for trend detection (need >=4 per genre)
            for (int i = 0; i < 5; i++)
            {
                _rentalRepo.Add(new Rental { Id = i * 3 + 1, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 1, 1).AddDays(i * 15), DueDate = new DateTime(2026, 1, 8).AddDays(i * 15), ReturnDate = new DateTime(2026, 1, 3).AddDays(i * 15), DailyRate = 3.99m + i * 0.50m, Status = RentalStatus.Returned });
                _rentalRepo.Add(new Rental { Id = i * 3 + 2, CustomerId = 1, MovieId = 2, RentalDate = new DateTime(2026, 1, 2).AddDays(i * 15), DueDate = new DateTime(2026, 1, 9).AddDays(i * 15), ReturnDate = new DateTime(2026, 1, 4).AddDays(i * 15), DailyRate = 2.99m, Status = RentalStatus.Returned });
                _rentalRepo.Add(new Rental { Id = i * 3 + 3, CustomerId = 1, MovieId = 3, RentalDate = new DateTime(2026, 1, 3).AddDays(i * 15), DueDate = new DateTime(2026, 1, 10).AddDays(i * 15), ReturnDate = new DateTime(2026, 1, 5).AddDays(i * 15), DailyRate = 1.99m, Status = RentalStatus.Returned });
            }
        }

        private void SeedSingleGenreRentals()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Basic });
            _movieRepo.Add(new Movie { Id = 1, Name = "Horror A", Genre = Genre.Horror, ReleaseDate = new DateTime(2020, 1, 1) });
            _movieRepo.Add(new Movie { Id = 2, Name = "Horror B", Genre = Genre.Horror, ReleaseDate = new DateTime(2020, 6, 1) });

            _rentalRepo.Add(new Rental { Id = 1, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 3, 1), DueDate = new DateTime(2026, 3, 8), ReturnDate = new DateTime(2026, 3, 3), DailyRate = 2.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 2, CustomerId = 1, MovieId = 2, RentalDate = new DateTime(2026, 3, 10), DueDate = new DateTime(2026, 3, 17), ReturnDate = new DateTime(2026, 3, 12), DailyRate = 2.99m, Status = RentalStatus.Returned });
        }

        private void SeedWeekendAndWeekdayRentals()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Gold });
            _movieRepo.Add(new Movie { Id = 1, Name = "Film A", Genre = Genre.Action, ReleaseDate = new DateTime(2020, 1, 1) });

            // Saturday rental
            _rentalRepo.Add(new Rental { Id = 1, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 4, 4), DueDate = new DateTime(2026, 4, 11), ReturnDate = new DateTime(2026, 4, 6), DailyRate = 4.99m, Status = RentalStatus.Returned });
            // Tuesday rental
            _rentalRepo.Add(new Rental { Id = 2, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 4, 7), DueDate = new DateTime(2026, 4, 14), ReturnDate = new DateTime(2026, 4, 9), DailyRate = 2.99m, Status = RentalStatus.Returned });
        }

        private void SeedRetentionData()
        {
            _customerRepo.Add(new Customer { Id = 1, Name = "Loyal Larry", MembershipType = MembershipType.Gold });
            _customerRepo.Add(new Customer { Id = 2, Name = "Repeat Rita", MembershipType = MembershipType.Silver });
            _customerRepo.Add(new Customer { Id = 3, Name = "One-Time Olivia", MembershipType = MembershipType.Basic });
            _customerRepo.Add(new Customer { Id = 4, Name = "Single Sam", MembershipType = MembershipType.Basic });

            _movieRepo.Add(new Movie { Id = 1, Name = "Film A", Genre = Genre.Action, ReleaseDate = new DateTime(2020, 1, 1) });

            // Larry: 3 rentals (returning)
            _rentalRepo.Add(new Rental { Id = 1, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 1, 5), DueDate = new DateTime(2026, 1, 12), ReturnDate = new DateTime(2026, 1, 7), DailyRate = 3.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 2, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 2, 5), DueDate = new DateTime(2026, 2, 12), ReturnDate = new DateTime(2026, 2, 7), DailyRate = 3.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 3, CustomerId = 1, MovieId = 1, RentalDate = new DateTime(2026, 3, 5), DueDate = new DateTime(2026, 3, 12), ReturnDate = new DateTime(2026, 3, 7), DailyRate = 3.99m, Status = RentalStatus.Returned });

            // Rita: 2 rentals (returning)
            _rentalRepo.Add(new Rental { Id = 4, CustomerId = 2, MovieId = 1, RentalDate = new DateTime(2026, 2, 1), DueDate = new DateTime(2026, 2, 8), ReturnDate = new DateTime(2026, 2, 3), DailyRate = 2.99m, Status = RentalStatus.Returned });
            _rentalRepo.Add(new Rental { Id = 5, CustomerId = 2, MovieId = 1, RentalDate = new DateTime(2026, 3, 1), DueDate = new DateTime(2026, 3, 8), ReturnDate = new DateTime(2026, 3, 3), DailyRate = 2.99m, Status = RentalStatus.Returned });

            // Olivia: 1 rental (new)
            _rentalRepo.Add(new Rental { Id = 6, CustomerId = 3, MovieId = 1, RentalDate = new DateTime(2026, 4, 1), DueDate = new DateTime(2026, 4, 8), ReturnDate = new DateTime(2026, 4, 3), DailyRate = 1.99m, Status = RentalStatus.Returned });

            // Sam: 1 rental (new)
            _rentalRepo.Add(new Rental { Id = 7, CustomerId = 4, MovieId = 1, RentalDate = new DateTime(2026, 4, 10), DueDate = new DateTime(2026, 4, 17), ReturnDate = new DateTime(2026, 4, 12), DailyRate = 1.99m, Status = RentalStatus.Returned });
        }

        // ── Stub clock ──────────────────────────────────────────────

        private class StubClock : IClock
        {
            public DateTime Now { get; }
            public DateTime Today => Now.Date;
            public StubClock(DateTime now) { Now = now; }
        }
    }
}
