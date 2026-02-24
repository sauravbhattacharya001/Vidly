using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vidly.Tests
{
    /// <summary>
    /// Tests for CustomerActivityService — activity reports, loyalty scoring,
    /// genre breakdown, monthly activity, and insights.
    /// </summary>
    [TestClass]
    public class CustomerActivityServiceTests
    {
        // ── Helpers ────────────────────────────────────────────────

        private static Rental MakeRental(
            int id, int customerId, int movieId, string movieName,
            int daysAgo, RentalStatus status = RentalStatus.Returned,
            decimal lateFee = 0m, int? returnDaysAgo = null)
        {
            var rentalDate = DateTime.Today.AddDays(-daysAgo);
            return new Rental
            {
                Id = id,
                CustomerId = customerId,
                CustomerName = "Test Customer",
                MovieId = movieId,
                MovieName = movieName,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = status == RentalStatus.Returned
                    ? (DateTime?)rentalDate.AddDays(returnDaysAgo ?? 5)
                    : null,
                DailyRate = 3.99m,
                LateFee = lateFee,
                Status = status
            };
        }

        private static Dictionary<int, Movie> MakeMovieLookup()
        {
            return new Dictionary<int, Movie>
            {
                { 1, new Movie { Id = 1, Name = "Action Movie", Genre = Genre.Action, Rating = 4 } },
                { 2, new Movie { Id = 2, Name = "Comedy Movie", Genre = Genre.Comedy, Rating = 5 } },
                { 3, new Movie { Id = 3, Name = "Drama Movie", Genre = Genre.Drama, Rating = 3 } },
                { 4, new Movie { Id = 4, Name = "Sci-Fi Movie", Genre = Genre.SciFi, Rating = 5 } },
                { 5, new Movie { Id = 5, Name = "Horror Movie", Genre = Genre.Horror, Rating = 2 } },
                { 6, new Movie { Id = 6, Name = "Action 2", Genre = Genre.Action, Rating = 4 } },
                { 7, new Movie { Id = 7, Name = "Comedy 2", Genre = Genre.Comedy, Rating = 3 } },
                { 8, new Movie { Id = 8, Name = "No Genre", Genre = null, Rating = 4 } },
            };
        }

        private static Customer MakeCustomer(
            int id = 1, string name = "Test Customer",
            MembershipType tier = MembershipType.Basic,
            int? memberMonthsAgo = 6)
        {
            return new Customer
            {
                Id = id,
                Name = name,
                MembershipType = tier,
                MemberSince = memberMonthsAgo.HasValue
                    ? (DateTime?)DateTime.Today.AddMonths(-memberMonthsAgo.Value)
                    : null
            };
        }

        // ── BuildSummary Tests ─────────────────────────────────────

        [TestMethod]
        public void BuildSummary_EmptyRentals_ReturnsDefaults()
        {
            var summary = CustomerActivityService.BuildSummary(new List<Rental>());

            Assert.AreEqual(0, summary.TotalRentals);
            Assert.AreEqual(0, summary.ActiveRentals);
            Assert.AreEqual(0, summary.OverdueRentals);
            Assert.AreEqual(0, summary.ReturnedRentals);
            Assert.AreEqual(0m, summary.TotalSpent);
            Assert.AreEqual(0m, summary.TotalLateFees);
            Assert.AreEqual(0, summary.AverageRentalDays);
            Assert.AreEqual(0m, summary.AverageSpentPerRental);
        }

        [TestMethod]
        public void BuildSummary_NullRentals_ReturnsDefaults()
        {
            var summary = CustomerActivityService.BuildSummary(null);
            Assert.AreEqual(0, summary.TotalRentals);
        }

        [TestMethod]
        public void BuildSummary_CountsByStatus()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 5, RentalStatus.Active),
                MakeRental(2, 1, 2, "M2", 10, RentalStatus.Overdue),
                MakeRental(3, 1, 3, "M3", 15, RentalStatus.Returned),
                MakeRental(4, 1, 4, "M4", 20, RentalStatus.Returned),
            };

            var summary = CustomerActivityService.BuildSummary(rentals);

            Assert.AreEqual(4, summary.TotalRentals);
            Assert.AreEqual(1, summary.ActiveRentals);
            Assert.AreEqual(1, summary.OverdueRentals);
            Assert.AreEqual(2, summary.ReturnedRentals);
        }

        [TestMethod]
        public void BuildSummary_CalculatesLateFees()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10, RentalStatus.Returned, lateFee: 3.00m),
                MakeRental(2, 1, 2, "M2", 20, RentalStatus.Returned, lateFee: 1.50m),
            };

            var summary = CustomerActivityService.BuildSummary(rentals);
            Assert.AreEqual(4.50m, summary.TotalLateFees);
        }

        [TestMethod]
        public void BuildSummary_AverageRentalDays_OnlyCompletedRentals()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 20, RentalStatus.Returned, returnDaysAgo: 10), // 10 days
                MakeRental(2, 1, 2, "M2", 10, RentalStatus.Returned, returnDaysAgo: 4),  // 6 days
                MakeRental(3, 1, 3, "M3", 5, RentalStatus.Active), // not counted
            };

            var summary = CustomerActivityService.BuildSummary(rentals);
            // Avg of completed = (10 + 6) / 2 = 8.0
            Assert.AreEqual(8.0, summary.AverageRentalDays);
        }

        [TestMethod]
        public void BuildSummary_OnTimeReturnRate()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10, RentalStatus.Returned, lateFee: 0m),
                MakeRental(2, 1, 2, "M2", 20, RentalStatus.Returned, lateFee: 0m),
                MakeRental(3, 1, 3, "M3", 30, RentalStatus.Returned, lateFee: 3.00m),
            };

            var summary = CustomerActivityService.BuildSummary(rentals);
            // 2/3 on time = 66.7%
            Assert.AreEqual(66.7, summary.OnTimeReturnRate);
        }

        [TestMethod]
        public void BuildSummary_TracksFirstAndLastRental()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 30),
                MakeRental(2, 1, 2, "M2", 5),
            };

            var summary = CustomerActivityService.BuildSummary(rentals);
            Assert.AreEqual(DateTime.Today.AddDays(-30), summary.FirstRentalDate);
            Assert.AreEqual(DateTime.Today.AddDays(-5), summary.LastRentalDate);
        }

        // ── BuildGenreBreakdown Tests ──────────────────────────────

        [TestMethod]
        public void BuildGenreBreakdown_GroupsByGenre()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "Action", 10),
                MakeRental(2, 1, 6, "Action 2", 15),
                MakeRental(3, 1, 2, "Comedy", 20),
            };
            var lookup = MakeMovieLookup();

            var breakdown = CustomerActivityService.BuildGenreBreakdown(rentals, lookup);

            Assert.AreEqual(2, breakdown.Count);
            Assert.AreEqual(Genre.Action, breakdown[0].Genre);
            Assert.AreEqual(2, breakdown[0].RentalCount);
            Assert.AreEqual(Genre.Comedy, breakdown[1].Genre);
            Assert.AreEqual(1, breakdown[1].RentalCount);
        }

        [TestMethod]
        public void BuildGenreBreakdown_CalculatesPercentage()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10),
                MakeRental(2, 1, 1, "M1", 15),
                MakeRental(3, 1, 1, "M1", 20),
                MakeRental(4, 1, 2, "M2", 25),
            };
            var lookup = MakeMovieLookup();

            var breakdown = CustomerActivityService.BuildGenreBreakdown(rentals, lookup);

            Assert.AreEqual(75.0, breakdown[0].Percentage); // 3/4
            Assert.AreEqual(25.0, breakdown[1].Percentage); // 1/4
        }

        [TestMethod]
        public void BuildGenreBreakdown_SkipsMoviesWithoutGenre()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 8, "No Genre", 10), // movie 8 has no genre
                MakeRental(2, 1, 1, "Action", 15),
            };
            var lookup = MakeMovieLookup();

            var breakdown = CustomerActivityService.BuildGenreBreakdown(rentals, lookup);

            Assert.AreEqual(1, breakdown.Count());
            Assert.AreEqual(Genre.Action, breakdown[0].Genre);
        }

        [TestMethod]
        public void BuildGenreBreakdown_SkipsUnknownMovies()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 999, "Unknown", 10), // not in lookup
            };
            var lookup = MakeMovieLookup();

            var breakdown = CustomerActivityService.BuildGenreBreakdown(rentals, lookup);
            Assert.AreEqual(0, breakdown.Count);
        }

        [TestMethod]
        public void BuildGenreBreakdown_AccumulatesTotalSpent()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10),
                MakeRental(2, 1, 6, "M6", 15),
            };
            var lookup = MakeMovieLookup();

            var breakdown = CustomerActivityService.BuildGenreBreakdown(rentals, lookup);
            Assert.AreEqual(1, breakdown.Count());
            // Both are Action, each TotalCost = 5 * 3.99 = 19.95
            Assert.IsTrue(breakdown[0].TotalSpent > 0);
        }

        [TestMethod]
        public void BuildGenreBreakdown_EmptyRentals_ReturnsEmpty()
        {
            var breakdown = CustomerActivityService.BuildGenreBreakdown(
                new List<Rental>(), MakeMovieLookup());
            Assert.AreEqual(0, breakdown.Count);
        }

        // ── BuildMonthlyActivity Tests ─────────────────────────────

        [TestMethod]
        public void BuildMonthlyActivity_Returns6Months()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 5),
            };

            var monthly = CustomerActivityService.BuildMonthlyActivity(rentals);

            Assert.AreEqual(6, monthly.Count);
        }

        [TestMethod]
        public void BuildMonthlyActivity_CurrentMonthHasRentals()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 1), // yesterday
                MakeRental(2, 1, 2, "M2", 2), // 2 days ago
            };

            var monthly = CustomerActivityService.BuildMonthlyActivity(rentals);
            var currentMonth = monthly.Last();

            Assert.AreEqual(DateTime.Today.Month, currentMonth.Month);
            Assert.AreEqual(2, currentMonth.RentalCount);
        }

        [TestMethod]
        public void BuildMonthlyActivity_EmptyRentals_AllZeros()
        {
            var monthly = CustomerActivityService.BuildMonthlyActivity(new List<Rental>());

            Assert.AreEqual(6, monthly.Count);
            Assert.IsTrue(monthly.All(m => m.RentalCount == 0));
        }

        [TestMethod]
        public void BuildMonthlyActivity_HasMonthNames()
        {
            var monthly = CustomerActivityService.BuildMonthlyActivity(new List<Rental>());

            foreach (var m in monthly)
            {
                Assert.IsFalse(string.IsNullOrEmpty(m.MonthName));
                Assert.IsTrue(m.Year > 0);
                Assert.IsTrue(m.Month >= 1 && m.Month <= 12);
            }
        }

        // ── CalculateLoyaltyScore Tests ────────────────────────────

        [TestMethod]
        public void CalculateLoyaltyScore_NoRentals_ReturnsZero()
        {
            var customer = MakeCustomer();
            var score = CustomerActivityService.CalculateLoyaltyScore(new List<Rental>(), customer);
            Assert.AreEqual(0, score);
        }

        [TestMethod]
        public void CalculateLoyaltyScore_NullRentals_ReturnsZero()
        {
            var customer = MakeCustomer();
            var score = CustomerActivityService.CalculateLoyaltyScore(null, customer);
            Assert.AreEqual(0, score);
        }

        [TestMethod]
        public void CalculateLoyaltyScore_IncreasesWithRentals()
        {
            var customer = MakeCustomer();
            var fewRentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10),
            };
            var manyRentals = new List<Rental>();
            for (int i = 0; i < 10; i++)
                manyRentals.Add(MakeRental(i + 1, 1, i + 1, $"M{i}", 10 + i));

            var scoreFew = CustomerActivityService.CalculateLoyaltyScore(fewRentals, customer);
            var scoreMany = CustomerActivityService.CalculateLoyaltyScore(manyRentals, customer);

            Assert.IsTrue(scoreMany > scoreFew);
        }

        [TestMethod]
        public void CalculateLoyaltyScore_HigherTier_HigherScore()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10),
            };

            var basic = MakeCustomer(tier: MembershipType.Basic);
            var platinum = MakeCustomer(tier: MembershipType.Platinum);

            var scoreBasic = CustomerActivityService.CalculateLoyaltyScore(rentals, basic);
            var scorePlatinum = CustomerActivityService.CalculateLoyaltyScore(rentals, platinum);

            Assert.IsTrue(scorePlatinum > scoreBasic);
        }

        [TestMethod]
        public void CalculateLoyaltyScore_MaxIs100()
        {
            var customer = MakeCustomer(tier: MembershipType.Platinum, memberMonthsAgo: 24);
            var rentals = new List<Rental>();
            for (int i = 0; i < 50; i++)
                rentals.Add(MakeRental(i + 1, 1, 1, "M1", i + 1));

            var score = CustomerActivityService.CalculateLoyaltyScore(rentals, customer);
            Assert.IsTrue(score >= 0 && score <= 100);
        }

        [TestMethod]
        public void CalculateLoyaltyScore_OnTimeReturns_BoostScore()
        {
            var customer = MakeCustomer();
            var onTimeRentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10, RentalStatus.Returned, lateFee: 0m),
                MakeRental(2, 1, 2, "M2", 20, RentalStatus.Returned, lateFee: 0m),
            };
            var lateRentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10, RentalStatus.Returned, lateFee: 5m),
                MakeRental(2, 1, 2, "M2", 20, RentalStatus.Returned, lateFee: 5m),
            };

            var scoreOnTime = CustomerActivityService.CalculateLoyaltyScore(onTimeRentals, customer);
            var scoreLate = CustomerActivityService.CalculateLoyaltyScore(lateRentals, customer);

            Assert.IsTrue(scoreOnTime > scoreLate);
        }

        [TestMethod]
        public void CalculateLoyaltyScore_NoMemberSince_SkipsAccountAge()
        {
            var customer = MakeCustomer(memberMonthsAgo: null);
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10),
            };

            var score = CustomerActivityService.CalculateLoyaltyScore(rentals, customer);
            Assert.IsTrue(score > 0); // still gets points from other factors
        }

        // ── GenerateInsights Tests ─────────────────────────────────

        [TestMethod]
        public void GenerateInsights_NoRentals_SuggestsMovies()
        {
            var customer = MakeCustomer();
            var insights = CustomerActivityService.GenerateInsights(
                new List<Rental>(), customer, MakeMovieLookup(),
                new List<GenreActivity>());

            Assert.AreEqual(1, insights.Count());
            Assert.AreEqual("No rentals yet", insights[0].Title);
            Assert.AreEqual(InsightType.Info, insights[0].Type);
        }

        [TestMethod]
        public void GenerateInsights_OverdueRentals_ShowsWarning()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 5, RentalStatus.Overdue),
            };

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup(),
                CustomerActivityService.BuildGenreBreakdown(rentals, MakeMovieLookup()));

            Assert.IsTrue(insights.Any(i => i.Title.Contains("overdue") && i.Type == InsightType.Warning));
        }

        [TestMethod]
        public void GenerateInsights_MultipleOverdue_ShowsCount()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 5, RentalStatus.Overdue),
                MakeRental(2, 1, 2, "M2", 10, RentalStatus.Overdue),
            };

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup(),
                CustomerActivityService.BuildGenreBreakdown(rentals, MakeMovieLookup()));

            Assert.IsTrue(insights.Any(i => i.Title.Contains("2 overdue")));
        }

        [TestMethod]
        public void GenerateInsights_PerfectReturns_ShowsPositive()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10, RentalStatus.Returned, lateFee: 0m),
                MakeRental(2, 1, 2, "M2", 20, RentalStatus.Returned, lateFee: 0m),
                MakeRental(3, 1, 3, "M3", 30, RentalStatus.Returned, lateFee: 0m),
            };

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup(),
                CustomerActivityService.BuildGenreBreakdown(rentals, MakeMovieLookup()));

            Assert.IsTrue(insights.Any(i => i.Title.Contains("Perfect") && i.Type == InsightType.Positive));
        }

        [TestMethod]
        public void GenerateInsights_FrequentLateReturns_ShowsWarning()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10, RentalStatus.Returned, lateFee: 3m),
                MakeRental(2, 1, 2, "M2", 20, RentalStatus.Returned, lateFee: 3m),
                MakeRental(3, 1, 3, "M3", 30, RentalStatus.Returned, lateFee: 0m),
            };

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup(),
                CustomerActivityService.BuildGenreBreakdown(rentals, MakeMovieLookup()));

            Assert.IsTrue(insights.Any(i => i.Title.Contains("late") && i.Type == InsightType.Warning));
        }

        [TestMethod]
        public void GenerateInsights_ShowsTopGenre()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10),
                MakeRental(2, 1, 6, "M6", 15),
                MakeRental(3, 1, 2, "M2", 20),
            };

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup(),
                CustomerActivityService.BuildGenreBreakdown(rentals, MakeMovieLookup()));

            Assert.IsTrue(insights.Any(i => i.Title.Contains("Action")));
        }

        [TestMethod]
        public void GenerateInsights_HighSpender_ShowsPositive()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>();
            for (int i = 0; i < 30; i++)
                rentals.Add(MakeRental(i + 1, 1, 1, "M1", i + 1));

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup(),
                CustomerActivityService.BuildGenreBreakdown(rentals, MakeMovieLookup()));

            Assert.IsTrue(insights.Any(i => i.Title.Contains("High-value") && i.Type == InsightType.Positive));
        }

        [TestMethod]
        public void GenerateInsights_BasicWithManyRentals_SuggestsUpgrade()
        {
            var customer = MakeCustomer(tier: MembershipType.Basic);
            var rentals = new List<Rental>();
            for (int i = 0; i < 6; i++)
                rentals.Add(MakeRental(i + 1, 1, i + 1, $"M{i}", i + 1));

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup(),
                CustomerActivityService.BuildGenreBreakdown(rentals, MakeMovieLookup()));

            Assert.IsTrue(insights.Any(i => i.Title.Contains("Upgrade")));
        }

        [TestMethod]
        public void GenerateInsights_InactiveCustomer_ShowsWarning()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 45, RentalStatus.Returned), // 45 days ago
            };

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup(),
                CustomerActivityService.BuildGenreBreakdown(rentals, MakeMovieLookup()));

            Assert.IsTrue(insights.Any(i => i.Title.Contains("Inactive") && i.Type == InsightType.Warning));
        }

        [TestMethod]
        public void GenerateInsights_GoldMember_NoUpgradeSuggestion()
        {
            var customer = MakeCustomer(tier: MembershipType.Gold);
            var rentals = new List<Rental>();
            for (int i = 0; i < 10; i++)
                rentals.Add(MakeRental(i + 1, 1, 1, "M1", i + 1));

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup(),
                CustomerActivityService.BuildGenreBreakdown(rentals, MakeMovieLookup()));

            Assert.IsFalse(insights.Any(i => i.Title.Contains("Upgrade")));
        }

        // ── ActivityReport Model Tests ──────────────────────────────

        [TestMethod]
        public void ActivityReport_DefaultsAreInitialized()
        {
            var report = new CustomerActivityReport();
            Assert.IsNotNull(report.RentalHistory);
            Assert.IsNotNull(report.Summary);
            Assert.IsNotNull(report.GenreBreakdown);
            Assert.IsNotNull(report.MonthlyActivity);
            Assert.IsNotNull(report.Insights);
        }

        [TestMethod]
        public void ActivitySummary_DefaultsAreZero()
        {
            var summary = new ActivitySummary();
            Assert.AreEqual(0, summary.TotalRentals);
            Assert.AreEqual(0m, summary.TotalSpent);
            Assert.AreEqual(0m, summary.TotalLateFees);
            Assert.AreEqual(0, summary.AverageRentalDays);
        }

        [TestMethod]
        public void GenreActivity_DefaultsAreZero()
        {
            var activity = new GenreActivity();
            Assert.AreEqual(0, activity.RentalCount);
            Assert.AreEqual(0m, activity.TotalSpent);
            Assert.AreEqual(0, activity.Percentage);
        }

        [TestMethod]
        public void MonthlyActivityEntry_HasProperties()
        {
            var entry = new MonthlyActivityEntry
            {
                Year = 2026,
                Month = 2,
                MonthName = "Feb 2026",
                RentalCount = 5,
                TotalSpent = 19.95m
            };

            Assert.AreEqual(2026, entry.Year);
            Assert.AreEqual("Feb 2026", entry.MonthName);
            Assert.AreEqual(5, entry.RentalCount);
            Assert.AreEqual(19.95m, entry.TotalSpent);
        }

        [TestMethod]
        public void ActivityInsight_HasAllProperties()
        {
            var insight = new ActivityInsight
            {
                Icon = "⚠️",
                Title = "Test",
                Description = "Description",
                Type = InsightType.Warning
            };

            Assert.AreEqual("⚠️", insight.Icon);
            Assert.AreEqual("Test", insight.Title);
            Assert.AreEqual(InsightType.Warning, insight.Type);
        }

        [TestMethod]
        public void InsightType_HasAllValues()
        {
            Assert.AreEqual(3, Enum.GetValues(typeof(InsightType)).Length);
        }

        // ── Edge Cases ──────────────────────────────────────────────

        [TestMethod]
        public void BuildSummary_SingleRental_CorrectAverages()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10, RentalStatus.Returned, returnDaysAgo: 3),
            };

            var summary = CustomerActivityService.BuildSummary(rentals);

            Assert.AreEqual(1, summary.TotalRentals);
            Assert.AreEqual(1, summary.ReturnedRentals);
            Assert.AreEqual(7.0, summary.AverageRentalDays); // 10 - 3 = 7 days
            Assert.AreEqual(100, summary.OnTimeReturnRate);
        }

        [TestMethod]
        public void BuildGenreBreakdown_SortedByCount()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 2, "Comedy", 10),
                MakeRental(2, 1, 1, "Action", 15),
                MakeRental(3, 1, 6, "Action 2", 20),
                MakeRental(4, 1, 3, "Drama", 25),
                MakeRental(5, 1, 1, "Action", 30),
            };
            var lookup = MakeMovieLookup();

            var breakdown = CustomerActivityService.BuildGenreBreakdown(rentals, lookup);

            // Action (3) > Comedy (1) = Drama (1)
            Assert.AreEqual(Genre.Action, breakdown[0].Genre);
            Assert.AreEqual(3, breakdown[0].RentalCount);
        }

        [TestMethod]
        public void CalculateLoyaltyScore_CapsFrequencyAt30()
        {
            var customer = MakeCustomer(tier: MembershipType.Basic, memberMonthsAgo: null);
            var fewRentals = new List<Rental>();
            for (int i = 0; i < 30; i++)
                fewRentals.Add(MakeRental(i + 1, 1, 1, "M1", i + 1));

            var moreRentals = new List<Rental>();
            for (int i = 0; i < 50; i++)
                moreRentals.Add(MakeRental(i + 1, 1, 1, "M1", i + 1));

            var score30 = CustomerActivityService.CalculateLoyaltyScore(fewRentals, customer);
            var score50 = CustomerActivityService.CalculateLoyaltyScore(moreRentals, customer);

            // After 30, frequency is capped; but spending still adds points
            Assert.IsTrue(score50 >= score30);
        }
    }
}
