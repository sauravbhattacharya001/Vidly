using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Xunit;

namespace Vidly.Tests
{
    /// <summary>
    /// Tests for CustomerActivityService — activity reports, loyalty scoring,
    /// genre breakdown, monthly activity, and insights.
    /// </summary>
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

        [Fact]
        public void BuildSummary_EmptyRentals_ReturnsDefaults()
        {
            var summary = CustomerActivityService.BuildSummary(new List<Rental>());

            Assert.Equal(0, summary.TotalRentals);
            Assert.Equal(0, summary.ActiveRentals);
            Assert.Equal(0, summary.OverdueRentals);
            Assert.Equal(0, summary.ReturnedRentals);
            Assert.Equal(0m, summary.TotalSpent);
            Assert.Equal(0m, summary.TotalLateFees);
            Assert.Equal(0, summary.AverageRentalDays);
            Assert.Equal(0m, summary.AverageSpentPerRental);
        }

        [Fact]
        public void BuildSummary_NullRentals_ReturnsDefaults()
        {
            var summary = CustomerActivityService.BuildSummary(null);
            Assert.Equal(0, summary.TotalRentals);
        }

        [Fact]
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

            Assert.Equal(4, summary.TotalRentals);
            Assert.Equal(1, summary.ActiveRentals);
            Assert.Equal(1, summary.OverdueRentals);
            Assert.Equal(2, summary.ReturnedRentals);
        }

        [Fact]
        public void BuildSummary_CalculatesLateFees()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10, RentalStatus.Returned, lateFee: 3.00m),
                MakeRental(2, 1, 2, "M2", 20, RentalStatus.Returned, lateFee: 1.50m),
            };

            var summary = CustomerActivityService.BuildSummary(rentals);
            Assert.Equal(4.50m, summary.TotalLateFees);
        }

        [Fact]
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
            Assert.Equal(8.0, summary.AverageRentalDays);
        }

        [Fact]
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
            Assert.Equal(66.7, summary.OnTimeReturnRate);
        }

        [Fact]
        public void BuildSummary_TracksFirstAndLastRental()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 30),
                MakeRental(2, 1, 2, "M2", 5),
            };

            var summary = CustomerActivityService.BuildSummary(rentals);
            Assert.Equal(DateTime.Today.AddDays(-30), summary.FirstRentalDate);
            Assert.Equal(DateTime.Today.AddDays(-5), summary.LastRentalDate);
        }

        // ── BuildGenreBreakdown Tests ──────────────────────────────

        [Fact]
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

            Assert.Equal(2, breakdown.Count);
            Assert.Equal(Genre.Action, breakdown[0].Genre);
            Assert.Equal(2, breakdown[0].RentalCount);
            Assert.Equal(Genre.Comedy, breakdown[1].Genre);
            Assert.Equal(1, breakdown[1].RentalCount);
        }

        [Fact]
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

            Assert.Equal(75.0, breakdown[0].Percentage); // 3/4
            Assert.Equal(25.0, breakdown[1].Percentage); // 1/4
        }

        [Fact]
        public void BuildGenreBreakdown_SkipsMoviesWithoutGenre()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 8, "No Genre", 10), // movie 8 has no genre
                MakeRental(2, 1, 1, "Action", 15),
            };
            var lookup = MakeMovieLookup();

            var breakdown = CustomerActivityService.BuildGenreBreakdown(rentals, lookup);

            Assert.Single(breakdown);
            Assert.Equal(Genre.Action, breakdown[0].Genre);
        }

        [Fact]
        public void BuildGenreBreakdown_SkipsUnknownMovies()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 999, "Unknown", 10), // not in lookup
            };
            var lookup = MakeMovieLookup();

            var breakdown = CustomerActivityService.BuildGenreBreakdown(rentals, lookup);
            Assert.Empty(breakdown);
        }

        [Fact]
        public void BuildGenreBreakdown_AccumulatesTotalSpent()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10),
                MakeRental(2, 1, 6, "M6", 15),
            };
            var lookup = MakeMovieLookup();

            var breakdown = CustomerActivityService.BuildGenreBreakdown(rentals, lookup);
            Assert.Single(breakdown);
            // Both are Action, each TotalCost = 5 * 3.99 = 19.95
            Assert.True(breakdown[0].TotalSpent > 0);
        }

        [Fact]
        public void BuildGenreBreakdown_EmptyRentals_ReturnsEmpty()
        {
            var breakdown = CustomerActivityService.BuildGenreBreakdown(
                new List<Rental>(), MakeMovieLookup());
            Assert.Empty(breakdown);
        }

        // ── BuildMonthlyActivity Tests ─────────────────────────────

        [Fact]
        public void BuildMonthlyActivity_Returns6Months()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 5),
            };

            var monthly = CustomerActivityService.BuildMonthlyActivity(rentals);

            Assert.Equal(6, monthly.Count);
        }

        [Fact]
        public void BuildMonthlyActivity_CurrentMonthHasRentals()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 1), // yesterday
                MakeRental(2, 1, 2, "M2", 2), // 2 days ago
            };

            var monthly = CustomerActivityService.BuildMonthlyActivity(rentals);
            var currentMonth = monthly.Last();

            Assert.Equal(DateTime.Today.Month, currentMonth.Month);
            Assert.Equal(2, currentMonth.RentalCount);
        }

        [Fact]
        public void BuildMonthlyActivity_EmptyRentals_AllZeros()
        {
            var monthly = CustomerActivityService.BuildMonthlyActivity(new List<Rental>());

            Assert.Equal(6, monthly.Count);
            Assert.All(monthly, m => Assert.Equal(0, m.RentalCount));
        }

        [Fact]
        public void BuildMonthlyActivity_HasMonthNames()
        {
            var monthly = CustomerActivityService.BuildMonthlyActivity(new List<Rental>());

            Assert.All(monthly, m =>
            {
                Assert.False(string.IsNullOrEmpty(m.MonthName));
                Assert.True(m.Year > 0);
                Assert.InRange(m.Month, 1, 12);
            });
        }

        // ── CalculateLoyaltyScore Tests ────────────────────────────

        [Fact]
        public void CalculateLoyaltyScore_NoRentals_ReturnsZero()
        {
            var customer = MakeCustomer();
            var score = CustomerActivityService.CalculateLoyaltyScore(new List<Rental>(), customer);
            Assert.Equal(0, score);
        }

        [Fact]
        public void CalculateLoyaltyScore_NullRentals_ReturnsZero()
        {
            var customer = MakeCustomer();
            var score = CustomerActivityService.CalculateLoyaltyScore(null, customer);
            Assert.Equal(0, score);
        }

        [Fact]
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

            Assert.True(scoreMany > scoreFew);
        }

        [Fact]
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

            Assert.True(scorePlatinum > scoreBasic);
        }

        [Fact]
        public void CalculateLoyaltyScore_MaxIs100()
        {
            var customer = MakeCustomer(tier: MembershipType.Platinum, memberMonthsAgo: 24);
            var rentals = new List<Rental>();
            for (int i = 0; i < 50; i++)
                rentals.Add(MakeRental(i + 1, 1, 1, "M1", i + 1));

            var score = CustomerActivityService.CalculateLoyaltyScore(rentals, customer);
            Assert.InRange(score, 0, 100);
        }

        [Fact]
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

            Assert.True(scoreOnTime > scoreLate);
        }

        [Fact]
        public void CalculateLoyaltyScore_NoMemberSince_SkipsAccountAge()
        {
            var customer = MakeCustomer(memberMonthsAgo: null);
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10),
            };

            var score = CustomerActivityService.CalculateLoyaltyScore(rentals, customer);
            Assert.True(score > 0); // still gets points from other factors
        }

        // ── GenerateInsights Tests ─────────────────────────────────

        [Fact]
        public void GenerateInsights_NoRentals_SuggestsMovies()
        {
            var customer = MakeCustomer();
            var insights = CustomerActivityService.GenerateInsights(
                new List<Rental>(), customer, MakeMovieLookup());

            Assert.Single(insights);
            Assert.Equal("No rentals yet", insights[0].Title);
            Assert.Equal(InsightType.Info, insights[0].Type);
        }

        [Fact]
        public void GenerateInsights_OverdueRentals_ShowsWarning()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 5, RentalStatus.Overdue),
            };

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup());

            Assert.Contains(insights, i => i.Title.Contains("overdue") && i.Type == InsightType.Warning);
        }

        [Fact]
        public void GenerateInsights_MultipleOverdue_ShowsCount()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 5, RentalStatus.Overdue),
                MakeRental(2, 1, 2, "M2", 10, RentalStatus.Overdue),
            };

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup());

            Assert.Contains(insights, i => i.Title.Contains("2 overdue"));
        }

        [Fact]
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
                rentals, customer, MakeMovieLookup());

            Assert.Contains(insights, i =>
                i.Title.Contains("Perfect") && i.Type == InsightType.Positive);
        }

        [Fact]
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
                rentals, customer, MakeMovieLookup());

            Assert.Contains(insights, i =>
                i.Title.Contains("late") && i.Type == InsightType.Warning);
        }

        [Fact]
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
                rentals, customer, MakeMovieLookup());

            Assert.Contains(insights, i => i.Title.Contains("Action"));
        }

        [Fact]
        public void GenerateInsights_HighSpender_ShowsPositive()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>();
            for (int i = 0; i < 30; i++)
                rentals.Add(MakeRental(i + 1, 1, 1, "M1", i + 1));

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup());

            Assert.Contains(insights, i =>
                i.Title.Contains("High-value") && i.Type == InsightType.Positive);
        }

        [Fact]
        public void GenerateInsights_BasicWithManyRentals_SuggestsUpgrade()
        {
            var customer = MakeCustomer(tier: MembershipType.Basic);
            var rentals = new List<Rental>();
            for (int i = 0; i < 6; i++)
                rentals.Add(MakeRental(i + 1, 1, i + 1, $"M{i}", i + 1));

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup());

            Assert.Contains(insights, i => i.Title.Contains("Upgrade"));
        }

        [Fact]
        public void GenerateInsights_InactiveCustomer_ShowsWarning()
        {
            var customer = MakeCustomer();
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 45, RentalStatus.Returned), // 45 days ago
            };

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup());

            Assert.Contains(insights, i =>
                i.Title.Contains("Inactive") && i.Type == InsightType.Warning);
        }

        [Fact]
        public void GenerateInsights_GoldMember_NoUpgradeSuggestion()
        {
            var customer = MakeCustomer(tier: MembershipType.Gold);
            var rentals = new List<Rental>();
            for (int i = 0; i < 10; i++)
                rentals.Add(MakeRental(i + 1, 1, 1, "M1", i + 1));

            var insights = CustomerActivityService.GenerateInsights(
                rentals, customer, MakeMovieLookup());

            Assert.DoesNotContain(insights, i => i.Title.Contains("Upgrade"));
        }

        // ── ActivityReport Model Tests ──────────────────────────────

        [Fact]
        public void ActivityReport_DefaultsAreInitialized()
        {
            var report = new CustomerActivityReport();
            Assert.NotNull(report.RentalHistory);
            Assert.NotNull(report.Summary);
            Assert.NotNull(report.GenreBreakdown);
            Assert.NotNull(report.MonthlyActivity);
            Assert.NotNull(report.Insights);
        }

        [Fact]
        public void ActivitySummary_DefaultsAreZero()
        {
            var summary = new ActivitySummary();
            Assert.Equal(0, summary.TotalRentals);
            Assert.Equal(0m, summary.TotalSpent);
            Assert.Equal(0m, summary.TotalLateFees);
            Assert.Equal(0, summary.AverageRentalDays);
        }

        [Fact]
        public void GenreActivity_DefaultsAreZero()
        {
            var activity = new GenreActivity();
            Assert.Equal(0, activity.RentalCount);
            Assert.Equal(0m, activity.TotalSpent);
            Assert.Equal(0, activity.Percentage);
        }

        [Fact]
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

            Assert.Equal(2026, entry.Year);
            Assert.Equal("Feb 2026", entry.MonthName);
            Assert.Equal(5, entry.RentalCount);
            Assert.Equal(19.95m, entry.TotalSpent);
        }

        [Fact]
        public void ActivityInsight_HasAllProperties()
        {
            var insight = new ActivityInsight
            {
                Icon = "⚠️",
                Title = "Test",
                Description = "Description",
                Type = InsightType.Warning
            };

            Assert.Equal("⚠️", insight.Icon);
            Assert.Equal("Test", insight.Title);
            Assert.Equal(InsightType.Warning, insight.Type);
        }

        [Fact]
        public void InsightType_HasAllValues()
        {
            Assert.Equal(3, Enum.GetValues(typeof(InsightType)).Length);
        }

        // ── Edge Cases ──────────────────────────────────────────────

        [Fact]
        public void BuildSummary_SingleRental_CorrectAverages()
        {
            var rentals = new List<Rental>
            {
                MakeRental(1, 1, 1, "M1", 10, RentalStatus.Returned, returnDaysAgo: 3),
            };

            var summary = CustomerActivityService.BuildSummary(rentals);

            Assert.Equal(1, summary.TotalRentals);
            Assert.Equal(1, summary.ReturnedRentals);
            Assert.Equal(7.0, summary.AverageRentalDays); // 10 - 3 = 7 days
            Assert.Equal(100, summary.OnTimeReturnRate);
        }

        [Fact]
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
            Assert.Equal(Genre.Action, breakdown[0].Genre);
            Assert.Equal(3, breakdown[0].RentalCount);
        }

        [Fact]
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
            Assert.True(score50 >= score30);
        }
    }
}
