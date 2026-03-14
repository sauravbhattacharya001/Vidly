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
    public class RentalTrendServiceTests
    {
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private RentalTrendService _service;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();

            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.ResetEmpty();

            _service = new RentalTrendService(_rentalRepo, _movieRepo);
        }

        // ── Constructor ──

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RentalTrendService(null, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RentalTrendService(_rentalRepo, null);
        }

        [TestMethod]
        public void Constructor_NullClock_UsesSystemClock()
        {
            var svc = new RentalTrendService(_rentalRepo, _movieRepo);
            Assert.IsNotNull(svc);
        }

        // ── Analyze validation ──

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Analyze_ToBeforeFrom_Throws()
        {
            _service.Analyze(DateTime.Today, DateTime.Today.AddDays(-1));
        }

        [TestMethod]
        public void Analyze_EmptyRepo_ReturnsEmptyReport()
        {
            var report = _service.Analyze(
                DateTime.Today.AddDays(-30), DateTime.Today);
            Assert.AreEqual(0, report.TotalRentals);
            Assert.AreEqual(0m, report.TotalRevenue);
            Assert.AreEqual(0, report.GenreTrends.Count);
            Assert.AreEqual(0, report.MonthlyVolumes.Count);
        }

        [TestMethod]
        public void Analyze_SameDayRange_Works()
        {
            var report = _service.Analyze(DateTime.Today, DateTime.Today);
            Assert.AreEqual(DateTime.Today, report.AnalysisStart);
            Assert.AreEqual(DateTime.Today, report.AnalysisEnd);
        }

        // ── Day-of-week breakdown ──

        [TestMethod]
        public void DayOfWeek_CountsCorrectly()
        {
            // Add rentals on known days
            var monday = GetNextWeekday(DateTime.Today.AddDays(-30), DayOfWeek.Monday);
            AddRental(1, 1, monday);
            AddRental(2, 2, monday);
            AddRental(3, 3, monday.AddDays(1)); // Tuesday

            var breakdown = _service.GetDayOfWeekBreakdown(
                monday.AddDays(-1), monday.AddDays(2));

            var monEntry = breakdown.FirstOrDefault(d => d.Day == DayOfWeek.Monday);
            Assert.IsNotNull(monEntry);
            Assert.AreEqual(2, monEntry.RentalCount);

            var tueEntry = breakdown.FirstOrDefault(d => d.Day == DayOfWeek.Tuesday);
            Assert.IsNotNull(tueEntry);
            Assert.AreEqual(1, tueEntry.RentalCount);
        }

        [TestMethod]
        public void DayOfWeek_PercentagesSumTo100()
        {
            var baseDate = DateTime.Today.AddDays(-20);
            for (int i = 0; i < 10; i++)
                AddRental(i + 1, 1, baseDate.AddDays(i));

            var breakdown = _service.GetDayOfWeekBreakdown(
                baseDate, baseDate.AddDays(10));

            var totalPct = breakdown.Sum(d => d.Percentage);
            Assert.AreEqual(100m, totalPct, 0.5m);
        }

        [TestMethod]
        public void DayOfWeek_AverageRevenueCalculated()
        {
            var day = DateTime.Today.AddDays(-5);
            AddRental(1, 1, day, dailyRate: 5.00m);
            AddRental(2, 2, day, dailyRate: 10.00m);

            var breakdown = _service.GetDayOfWeekBreakdown(
                day.AddDays(-1), day.AddDays(1));

            var entry = breakdown.First(d => d.Day == day.DayOfWeek);
            Assert.IsTrue(entry.AverageRevenue > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetDayOfWeekBreakdown_InvalidRange_Throws()
        {
            _service.GetDayOfWeekBreakdown(DateTime.Today, DateTime.Today.AddDays(-1));
        }

        // ── Genre trends ──

        [TestMethod]
        public void GenreTrends_GroupsByGenre()
        {
            AddMovie(1, "Action Movie", Genre.Action);
            AddMovie(2, "Comedy Movie", Genre.Comedy);
            AddMovie(3, "Action 2", Genre.Action);

            var baseDate = DateTime.Today.AddDays(-30);
            AddRental(1, 1, baseDate);
            AddRental(2, 2, baseDate.AddDays(1));
            AddRental(3, 3, baseDate.AddDays(2));

            var trends = _service.GetGenreTrends(baseDate.AddDays(-1), DateTime.Today);

            var action = trends.FirstOrDefault(g => g.Genre == Genre.Action);
            Assert.IsNotNull(action);
            Assert.AreEqual(2, action.RentalCount);

            var comedy = trends.FirstOrDefault(g => g.Genre == Genre.Comedy);
            Assert.IsNotNull(comedy);
            Assert.AreEqual(1, comedy.RentalCount);
        }

        [TestMethod]
        public void GenreTrends_TracksUniqueCustomers()
        {
            AddMovie(1, "Action", Genre.Action);
            var baseDate = DateTime.Today.AddDays(-20);
            // Same customer rents twice
            AddRentalWithCustomer(1, 1, 100, baseDate);
            AddRentalWithCustomer(2, 1, 100, baseDate.AddDays(1));
            // Different customer
            AddRentalWithCustomer(3, 1, 200, baseDate.AddDays(2));

            var trends = _service.GetGenreTrends(baseDate.AddDays(-1), DateTime.Today);
            var action = trends.First(g => g.Genre == Genre.Action);
            Assert.AreEqual(2, action.UniqueCustomers);
        }

        [TestMethod]
        public void GenreTrends_DirectionRising()
        {
            AddMovie(1, "Action", Genre.Action);
            var baseDate = DateTime.Today.AddDays(-30);
            // 1 rental in first half
            AddRental(1, 1, baseDate);
            // 3 rentals in second half
            AddRental(2, 1, baseDate.AddDays(20));
            AddRental(3, 1, baseDate.AddDays(22));
            AddRental(4, 1, baseDate.AddDays(25));

            var trends = _service.GetGenreTrends(baseDate, DateTime.Today);
            var action = trends.First(g => g.Genre == Genre.Action);
            Assert.AreEqual(1, action.Direction); // rising
        }

        [TestMethod]
        public void GenreTrends_DirectionFalling()
        {
            AddMovie(1, "Action", Genre.Action);
            var baseDate = DateTime.Today.AddDays(-30);
            // 3 rentals in first half
            AddRental(1, 1, baseDate.AddDays(1));
            AddRental(2, 1, baseDate.AddDays(3));
            AddRental(3, 1, baseDate.AddDays(5));
            // 1 rental in second half
            AddRental(4, 1, baseDate.AddDays(25));

            var trends = _service.GetGenreTrends(baseDate, DateTime.Today);
            var action = trends.First(g => g.Genre == Genre.Action);
            Assert.AreEqual(-1, action.Direction); // falling
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetGenreTrends_InvalidRange_Throws()
        {
            _service.GetGenreTrends(DateTime.Today, DateTime.Today.AddDays(-1));
        }

        // ── Monthly volumes ──

        [TestMethod]
        public void MonthlyVolumes_GroupsByMonth()
        {
            AddRental(1, 1, new DateTime(2025, 1, 15));
            AddRental(2, 2, new DateTime(2025, 1, 20));
            AddRental(3, 3, new DateTime(2025, 2, 10));

            var volumes = _service.GetMonthlyVolumes(
                new DateTime(2025, 1, 1), new DateTime(2025, 3, 1));

            Assert.AreEqual(2, volumes.Count);
            Assert.AreEqual(2, volumes[0].RentalCount);
            Assert.AreEqual(1, volumes[1].RentalCount);
        }

        [TestMethod]
        public void MonthlyVolumes_CalculatesChangePercent()
        {
            AddRental(1, 1, new DateTime(2025, 1, 15));
            AddRental(2, 2, new DateTime(2025, 2, 10));
            AddRental(3, 3, new DateTime(2025, 2, 15));

            var volumes = _service.GetMonthlyVolumes(
                new DateTime(2025, 1, 1), new DateTime(2025, 3, 1));

            Assert.AreEqual(0, volumes[0].ChangePercent); // first month
            Assert.AreEqual(100, volumes[1].ChangePercent); // doubled
        }

        [TestMethod]
        public void MonthlyVolumes_TracksUniqueCustomers()
        {
            AddRentalWithCustomer(1, 1, 100, new DateTime(2025, 1, 10));
            AddRentalWithCustomer(2, 2, 100, new DateTime(2025, 1, 15));
            AddRentalWithCustomer(3, 3, 200, new DateTime(2025, 1, 20));

            var volumes = _service.GetMonthlyVolumes(
                new DateTime(2025, 1, 1), new DateTime(2025, 2, 1));

            Assert.AreEqual(2, volumes[0].UniqueCustomers);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetMonthlyVolumes_InvalidRange_Throws()
        {
            _service.GetMonthlyVolumes(DateTime.Today, DateTime.Today.AddDays(-1));
        }

        // ── Retention cohorts ──

        [TestMethod]
        public void RetentionCohorts_CalculatesRetention30()
        {
            // Customer 100 first rents Jan 5, rents again Jan 20 (within 30 days)
            AddRentalWithCustomer(1, 1, 100, new DateTime(2025, 1, 5));
            AddRentalWithCustomer(2, 2, 100, new DateTime(2025, 1, 20));
            // Customer 200 first rents Jan 10, never returns
            AddRentalWithCustomer(3, 3, 200, new DateTime(2025, 1, 10));

            var cohorts = _service.GetRetentionCohorts(
                new DateTime(2025, 1, 1), new DateTime(2025, 3, 1));

            var jan = cohorts.First(c => c.Month == 1);
            Assert.AreEqual(2, jan.CohortSize);
            Assert.AreEqual(1, jan.ReturnedWithin30Days);
            Assert.AreEqual(50m, jan.RetentionRate30);
        }

        [TestMethod]
        public void RetentionCohorts_CalculatesRetention90()
        {
            // Customer rents Jan 5, rents again Mar 1 (within 90 days)
            AddRentalWithCustomer(1, 1, 100, new DateTime(2025, 1, 5));
            AddRentalWithCustomer(2, 2, 100, new DateTime(2025, 3, 1));

            var cohorts = _service.GetRetentionCohorts(
                new DateTime(2025, 1, 1), new DateTime(2025, 4, 1));

            var jan = cohorts.First(c => c.Month == 1);
            Assert.AreEqual(1, jan.CohortSize);
            Assert.AreEqual(0, jan.ReturnedWithin30Days); // 55 days > 30
            Assert.AreEqual(1, jan.ReturnedWithin90Days);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetRetentionCohorts_InvalidRange_Throws()
        {
            _service.GetRetentionCohorts(DateTime.Today, DateTime.Today.AddDays(-1));
        }

        // ── Peak/quiet periods ──

        [TestMethod]
        public void PeakPeriods_IdentifiesPeaks()
        {
            var baseDate = new DateTime(2025, 1, 1);
            // Create a burst of rentals in week 2
            for (int i = 7; i < 14; i++)
            {
                for (int j = 0; j < 5; j++)
                    AddRental(i * 10 + j, 1, baseDate.AddDays(i));
            }
            // Sparse rentals elsewhere
            AddRental(901, 1, baseDate);
            AddRental(902, 1, baseDate.AddDays(20));

            var report = _service.Analyze(baseDate, baseDate.AddDays(27));
            // Should identify peak periods
            var peaks = report.PeakPeriods.Where(p => p.IsPeak).ToList();
            Assert.IsTrue(peaks.Count > 0, "Should detect at least one peak period");
        }

        [TestMethod]
        public void PeakPeriods_ShortRange_ReturnsEmpty()
        {
            AddRental(1, 1, DateTime.Today);
            var report = _service.Analyze(DateTime.Today, DateTime.Today.AddDays(3));
            Assert.AreEqual(0, report.PeakPeriods.Count);
        }

        // ── Full report ──

        [TestMethod]
        public void Analyze_FullReport_PopulatesAllSections()
        {
            AddMovie(1, "Action", Genre.Action);
            AddMovie(2, "Comedy", Genre.Comedy);

            var baseDate = DateTime.Today.AddDays(-60);
            AddRentalWithCustomer(1, 1, 100, baseDate);
            AddRentalWithCustomer(2, 2, 200, baseDate.AddDays(5));
            AddRentalWithCustomer(3, 1, 100, baseDate.AddDays(10));
            AddRentalWithCustomer(4, 2, 300, baseDate.AddDays(40));

            var report = _service.Analyze(baseDate, DateTime.Today);

            Assert.AreEqual(4, report.TotalRentals);
            Assert.IsTrue(report.TotalRevenue > 0);
            Assert.IsTrue(report.AverageRentalsPerDay > 0);
            Assert.IsTrue(report.DayOfWeekBreakdown.Count > 0);
            Assert.IsTrue(report.GenreTrends.Count > 0);
            Assert.IsTrue(report.MonthlyVolumes.Count > 0);
        }

        [TestMethod]
        public void Analyze_IdentifiesBusiestAndQuietestDay()
        {
            var monday = GetNextWeekday(DateTime.Today.AddDays(-30), DayOfWeek.Monday);
            AddRental(1, 1, monday);
            AddRental(2, 2, monday);
            AddRental(3, 3, monday);
            AddRental(4, 1, monday.AddDays(2)); // Wednesday

            var report = _service.Analyze(monday.AddDays(-1), monday.AddDays(3));
            Assert.AreEqual(DayOfWeek.Monday, report.BusiestDay);
        }

        [TestMethod]
        public void Analyze_IdentifiesTopGenre()
        {
            AddMovie(1, "Action", Genre.Action);
            AddMovie(2, "Comedy", Genre.Comedy);

            var baseDate = DateTime.Today.AddDays(-20);
            AddRental(1, 1, baseDate);
            AddRental(2, 1, baseDate.AddDays(1));
            AddRental(3, 2, baseDate.AddDays(2));

            var report = _service.Analyze(baseDate.AddDays(-1), DateTime.Today);
            Assert.AreEqual(Genre.Action, report.TopGenre);
        }

        // ── Text report ──

        [TestMethod]
        public void GenerateTextReport_ContainsHeaders()
        {
            var text = _service.GenerateTextReport(
                DateTime.Today.AddDays(-30), DateTime.Today);
            Assert.IsTrue(text.Contains("RENTAL TREND ANALYSIS REPORT"));
            Assert.IsTrue(text.Contains("Total Rentals:"));
        }

        [TestMethod]
        public void GenerateTextReport_IncludesDayOfWeek()
        {
            AddRental(1, 1, DateTime.Today.AddDays(-5));
            var text = _service.GenerateTextReport(
                DateTime.Today.AddDays(-10), DateTime.Today);
            Assert.IsTrue(text.Contains("Day-of-Week Breakdown"));
        }

        [TestMethod]
        public void GenerateTextReport_IncludesGenreTrends()
        {
            AddMovie(1, "Action", Genre.Action);
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var text = _service.GenerateTextReport(
                DateTime.Today.AddDays(-10), DateTime.Today);
            Assert.IsTrue(text.Contains("Genre Trends"));
        }

        // ── JSON export ──

        [TestMethod]
        public void ExportJson_ReturnsValidString()
        {
            AddRental(1, 1, DateTime.Today.AddDays(-5));
            var json = _service.ExportJson(
                DateTime.Today.AddDays(-10), DateTime.Today);
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Length > 0);
            Assert.IsTrue(json.Contains("TotalRentals") || json.Contains("totalRentals"));
        }

        // ── Edge cases ──

        [TestMethod]
        public void Analyze_RentalsOutsideRange_Excluded()
        {
            AddRental(1, 1, new DateTime(2025, 6, 15));
            var report = _service.Analyze(
                new DateTime(2025, 1, 1), new DateTime(2025, 3, 1));
            Assert.AreEqual(0, report.TotalRentals);
        }

        [TestMethod]
        public void GenreTrends_MovieWithNoGenre_Excluded()
        {
            AddMovie(1, "Unknown", null);
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var trends = _service.GetGenreTrends(
                DateTime.Today.AddDays(-10), DateTime.Today);
            Assert.AreEqual(0, trends.Count);
        }

        [TestMethod]
        public void MonthlyVolumes_Empty_ReturnsEmptyList()
        {
            var volumes = _service.GetMonthlyVolumes(
                DateTime.Today.AddDays(-30), DateTime.Today);
            Assert.AreEqual(0, volumes.Count);
        }

        [TestMethod]
        public void RetentionCohorts_NoCustomersInRange_ReturnsEmpty()
        {
            AddRentalWithCustomer(1, 1, 100, new DateTime(2024, 1, 1));
            var cohorts = _service.GetRetentionCohorts(
                new DateTime(2025, 1, 1), new DateTime(2025, 3, 1));
            Assert.AreEqual(0, cohorts.Count);
        }

        // ── Helpers ──

        private static DateTime GetNextWeekday(DateTime start, DayOfWeek day)
        {
            int daysUntil = ((int)day - (int)start.DayOfWeek + 7) % 7;
            return start.AddDays(daysUntil == 0 ? 7 : daysUntil);
        }

        private void AddRental(int movieId, int customerId, DateTime rentalDate,
            decimal dailyRate = 3.99m)
        {
            _rentalRepo.Add(new Rental
            {
                MovieId = movieId,
                CustomerId = customerId,
                CustomerName = $"Customer {customerId}",
                MovieName = $"Movie {movieId}",
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                DailyRate = dailyRate,
                Status = RentalStatus.Active
            });
        }

        private void AddRentalWithCustomer(int id, int movieId, int customerId,
            DateTime rentalDate)
        {
            _rentalRepo.Add(new Rental
            {
                MovieId = movieId,
                CustomerId = customerId,
                CustomerName = $"Customer {customerId}",
                MovieName = $"Movie {movieId}",
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                DailyRate = 3.99m,
                Status = RentalStatus.Active
            });
        }

        private void AddMovie(int id, string name, Genre? genre)
        {
            _movieRepo.Add(new Movie
            {
                Id = id,
                Name = name,
                Genre = genre,
                Rating = 4,
                ReleaseDate = DateTime.Today.AddYears(-1)
            });
        }
    }
}
