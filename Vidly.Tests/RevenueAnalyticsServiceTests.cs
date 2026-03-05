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
    public class RevenueAnalyticsServiceTests
    {
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private RevenueAnalyticsService _service;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();

            InMemoryRentalRepository.Reset();
            InMemoryMovieRepository.ResetEmpty();
            InMemoryCustomerRepository.Reset();

            _service = new RevenueAnalyticsService(_rentalRepo, _movieRepo, _customerRepo);
        }

        private Movie AddMovie(int id, string name, Genre genre, decimal dailyRate = 3.99m)
        {
            var movie = new Movie { Id = id, Name = name, Genre = genre, DailyRate = dailyRate };
            _movieRepo.Add(movie);
            return movie;
        }

        private Customer AddCustomer(int id, string name, MembershipType membership = MembershipType.Basic)
        {
            var customer = new Customer { Id = id, Name = name, MembershipType = membership };
            _customerRepo.Add(customer);
            return customer;
        }

        private Rental AddRental(int movieId, int customerId, DateTime rentalDate,
            decimal dailyRate = 3.99m, decimal lateFee = 0m,
            DateTime? returnDate = null, RentalStatus status = RentalStatus.Returned)
        {
            var rental = new Rental
            {
                Id = _rentalRepo.GetAll().Count + 100,
                MovieId = movieId,
                CustomerId = customerId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(7),
                ReturnDate = returnDate ?? rentalDate.AddDays(3),
                DailyRate = dailyRate,
                LateFee = lateFee,
                Status = status,
            };
            _rentalRepo.Add(rental);
            return rental;
        }

        // ── Constructor Tests ───────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RevenueAnalyticsService(null, _movieRepo, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RevenueAnalyticsService(_rentalRepo, null, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new RevenueAnalyticsService(_rentalRepo, _movieRepo, null);
        }

        // ── GetReport Tests ─────────────────────────────────────────

        [TestMethod]
        public void GetReport_EmptyPeriod_ReturnsZeros()
        {
            var start = new DateTime(2025, 1, 1);
            var end = new DateTime(2025, 1, 31);

            var report = _service.GetReport(start, end);

            Assert.AreEqual(0, report.TotalRentals);
            Assert.AreEqual(0m, report.TotalRevenue);
            Assert.AreEqual(0m, report.AverageRevenuePerRental);
            Assert.AreEqual(start, report.PeriodStart);
            Assert.AreEqual(end, report.PeriodEnd);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetReport_EndBeforeStart_Throws()
        {
            _service.GetReport(new DateTime(2025, 2, 1), new DateTime(2025, 1, 1));
        }

        [TestMethod]
        public void GetReport_SingleRental_CorrectTotals()
        {
            AddMovie(1, "TestMovie", Genre.Action);
            AddCustomer(1, "TestCustomer");
            AddRental(1, 1, new DateTime(2025, 1, 15), dailyRate: 5.00m, lateFee: 2.00m);

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.AreEqual(1, report.TotalRentals);
            Assert.IsTrue(report.TotalRevenue > 0m);
            Assert.AreEqual(2.00m, report.LateFeeRevenue);
        }

        [TestMethod]
        public void GetReport_MultipleRentals_SumsCorrectly()
        {
            AddMovie(1, "Action Movie", Genre.Action, 5.00m);
            AddMovie(2, "Comedy Movie", Genre.Comedy, 3.00m);
            AddCustomer(1, "Customer A");
            AddCustomer(2, "Customer B");

            AddRental(1, 1, new DateTime(2025, 1, 10), dailyRate: 5.00m);
            AddRental(2, 2, new DateTime(2025, 1, 15), dailyRate: 3.00m);
            AddRental(1, 2, new DateTime(2025, 1, 20), dailyRate: 5.00m);

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.AreEqual(3, report.TotalRentals);
            Assert.IsTrue(report.TotalRevenue > 0);
            Assert.IsTrue(report.AverageRevenuePerRental > 0);
        }

        [TestMethod]
        public void GetReport_FiltersByDateRange()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 15));
            AddRental(1, 1, new DateTime(2025, 2, 15));

            var janReport = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));
            var febReport = _service.GetReport(
                new DateTime(2025, 2, 1), new DateTime(2025, 2, 28));

            Assert.AreEqual(1, janReport.TotalRentals);
            Assert.AreEqual(1, febReport.TotalRentals);
        }

        [TestMethod]
        public void GetReport_TracksCompletedAndActive()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 10), status: RentalStatus.Returned);
            AddRental(1, 1, new DateTime(2025, 1, 15), status: RentalStatus.Active,
                returnDate: null);

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.AreEqual(2, report.TotalRentals);
            Assert.AreEqual(1, report.CompletedRentals);
        }

        // ── Genre Breakdown Tests ───────────────────────────────────

        [TestMethod]
        public void GetReport_GenreBreakdown_GroupsCorrectly()
        {
            AddMovie(1, "ActionMovie", Genre.Action, 5.00m);
            AddMovie(2, "ComedyMovie", Genre.Comedy, 3.00m);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 10), dailyRate: 5.00m);
            AddRental(1, 1, new DateTime(2025, 1, 12), dailyRate: 5.00m);
            AddRental(2, 1, new DateTime(2025, 1, 15), dailyRate: 3.00m);

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.IsTrue(report.RevenueByGenre.Count >= 2);
            var action = report.RevenueByGenre.Find(g => g.Genre == Genre.Action);
            Assert.IsNotNull(action);
            Assert.AreEqual(2, action.RentalCount);
        }

        [TestMethod]
        public void GetReport_GenreBreakdown_SharePercentsAddTo100()
        {
            AddMovie(1, "A", Genre.Action, 5.00m);
            AddMovie(2, "C", Genre.Comedy, 3.00m);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 10), dailyRate: 5.00m);
            AddRental(2, 1, new DateTime(2025, 1, 15), dailyRate: 3.00m);

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            double totalShare = report.RevenueByGenre.Sum(g => g.SharePercent);
            Assert.IsTrue(Math.Abs(totalShare - 100.0) < 1.0,
                "Genre shares should approximate 100%: " + totalShare);
        }

        // ── Membership Breakdown Tests ──────────────────────────────

        [TestMethod]
        public void GetReport_MembershipBreakdown_GroupsCorrectly()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "Premium", MembershipType.Gold);
            AddCustomer(2, "Standard", MembershipType.Basic);

            AddRental(1, 1, new DateTime(2025, 1, 10));
            AddRental(1, 2, new DateTime(2025, 1, 15));

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.IsTrue(report.RevenueByMembership.Count >= 2);
        }

        [TestMethod]
        public void GetReport_MembershipBreakdown_CountsUniqueCustomers()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "Customer1", MembershipType.Gold);

            AddRental(1, 1, new DateTime(2025, 1, 10));
            AddRental(1, 1, new DateTime(2025, 1, 15));

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            var premium = report.RevenueByMembership
                .Find(m => m.Membership == MembershipType.Gold);
            Assert.IsNotNull(premium);
            Assert.AreEqual(1, premium.CustomerCount);
            Assert.AreEqual(2, premium.RentalCount);
        }

        // ── Monthly Trend Tests ─────────────────────────────────────

        [TestMethod]
        public void GetReport_MonthlyTrend_OrderedChronologically()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 3, 15));
            AddRental(1, 1, new DateTime(2025, 1, 15));
            AddRental(1, 1, new DateTime(2025, 2, 15));

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 3, 31));

            Assert.AreEqual(3, report.MonthlyTrend.Count);
            Assert.AreEqual(1, report.MonthlyTrend[0].Month);
            Assert.AreEqual(2, report.MonthlyTrend[1].Month);
            Assert.AreEqual(3, report.MonthlyTrend[2].Month);
        }

        [TestMethod]
        public void GetReport_MonthlyTrend_CalculatesGrowth()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 15), dailyRate: 5.00m);
            AddRental(1, 1, new DateTime(2025, 2, 15), dailyRate: 5.00m);
            AddRental(1, 1, new DateTime(2025, 2, 20), dailyRate: 5.00m);

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 2, 28));

            Assert.AreEqual(2, report.MonthlyTrend.Count);
            Assert.AreEqual(0.0m, report.MonthlyTrend[0].GrowthPercent);
            Assert.IsTrue(report.MonthlyTrend[1].GrowthPercent > 0,
                "Feb should show growth over Jan");
        }

        // ── Top Customers/Movies Tests ──────────────────────────────

        [TestMethod]
        public void GetReport_TopCustomers_RankedByRevenue()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "BigSpender");
            AddCustomer(2, "SmallSpender");

            AddRental(1, 1, new DateTime(2025, 1, 10), dailyRate: 10.00m);
            AddRental(1, 1, new DateTime(2025, 1, 15), dailyRate: 10.00m);
            AddRental(1, 2, new DateTime(2025, 1, 20), dailyRate: 3.00m);

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31), topCount: 2);

            Assert.AreEqual(2, report.TopCustomers.Count);
            Assert.AreEqual(1, report.TopCustomers[0].CustomerId);
        }

        [TestMethod]
        public void GetReport_TopMovies_RankedByRevenue()
        {
            AddMovie(1, "PopularMovie", Genre.Action, 10.00m);
            AddMovie(2, "NicheMovie", Genre.Drama, 2.00m);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 10), dailyRate: 10.00m);
            AddRental(1, 1, new DateTime(2025, 1, 15), dailyRate: 10.00m);
            AddRental(2, 1, new DateTime(2025, 1, 20), dailyRate: 2.00m);

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31), topCount: 2);

            Assert.AreEqual(2, report.TopMovies.Count);
            Assert.AreEqual(1, report.TopMovies[0].MovieId);
        }

        [TestMethod]
        public void GetReport_TopCount_LimitsResults()
        {
            AddMovie(1, "M1", Genre.Action);
            AddMovie(2, "M2", Genre.Comedy);
            AddMovie(3, "M3", Genre.Drama);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 10));
            AddRental(2, 1, new DateTime(2025, 1, 15));
            AddRental(3, 1, new DateTime(2025, 1, 20));

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31), topCount: 1);

            Assert.AreEqual(1, report.TopMovies.Count);
            Assert.AreEqual(1, report.TopCustomers.Count);
        }

        // ── GetRecentReport Tests ───────────────────────────────────

        [TestMethod]
        public void GetRecentReport_ReturnsCorrectPeriod()
        {
            var asOf = new DateTime(2025, 1, 31);
            var report = _service.GetRecentReport(30, asOf);

            Assert.AreEqual(asOf.AddDays(-30), report.PeriodStart);
            Assert.AreEqual(asOf, report.PeriodEnd);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetRecentReport_ZeroDays_Throws()
        {
            _service.GetRecentReport(0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetRecentReport_NegativeDays_Throws()
        {
            _service.GetRecentReport(-5);
        }

        // ── ComparePeriods Tests ────────────────────────────────────

        [TestMethod]
        public void ComparePeriods_GrowthScenario()
        {
            AddMovie(1, "M", Genre.Action, 5.00m);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 15), dailyRate: 5.00m);
            AddRental(1, 1, new DateTime(2025, 2, 10), dailyRate: 5.00m);
            AddRental(1, 1, new DateTime(2025, 2, 20), dailyRate: 5.00m);

            var comparison = _service.ComparePeriods(
                new DateTime(2025, 2, 1), new DateTime(2025, 2, 28),
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.IsTrue(comparison.RevenueChange > 0m,
                "Feb should have more revenue than Jan");
            Assert.IsTrue(comparison.RevenueChangePercent > 0,
                "Revenue growth should be positive");
            Assert.AreEqual(1, comparison.RentalCountChange);
        }

        [TestMethod]
        public void ComparePeriods_DeclineScenario()
        {
            AddMovie(1, "M", Genre.Action, 5.00m);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 10), dailyRate: 5.00m);
            AddRental(1, 1, new DateTime(2025, 1, 20), dailyRate: 5.00m);
            AddRental(1, 1, new DateTime(2025, 2, 15), dailyRate: 5.00m);

            var comparison = _service.ComparePeriods(
                new DateTime(2025, 2, 1), new DateTime(2025, 2, 28),
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.IsTrue(comparison.RevenueChange < 0m);
            Assert.IsTrue(comparison.RevenueChangePercent < 0);
        }

        [TestMethod]
        public void ComparePeriods_NoPreviousRevenue_100PercentGrowth()
        {
            AddMovie(1, "M", Genre.Action, 5.00m);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 2, 15), dailyRate: 5.00m);

            var comparison = _service.ComparePeriods(
                new DateTime(2025, 2, 1), new DateTime(2025, 2, 28),
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.AreEqual(100.0, comparison.RevenueChangePercent);
        }

        // ── CompareMonthOverMonth Tests ─────────────────────────────

        [TestMethod]
        public void CompareMonthOverMonth_CorrectPeriods()
        {
            var comparison = _service.CompareMonthOverMonth(2025, 2);

            Assert.AreEqual(new DateTime(2025, 2, 1), comparison.Current.PeriodStart);
            Assert.AreEqual(new DateTime(2025, 2, 28), comparison.Current.PeriodEnd);
            Assert.AreEqual(new DateTime(2025, 1, 1), comparison.Previous.PeriodStart);
        }

        // ── ForecastRevenue Tests ───────────────────────────────────

        [TestMethod]
        public void ForecastRevenue_NoData_ReturnsZero()
        {
            var forecast = _service.ForecastRevenue(30);

            Assert.AreEqual(0m, forecast.ProjectedRevenue);
            Assert.AreEqual(0, forecast.ProjectedRentals);
            Assert.AreEqual("no_data", forecast.Method);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ForecastRevenue_ZeroDays_Throws()
        {
            _service.ForecastRevenue(0);
        }

        [TestMethod]
        public void ForecastRevenue_SingleMonth_UsesAverage()
        {
            AddMovie(1, "M", Genre.Action, 5.00m);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 10), dailyRate: 5.00m);
            AddRental(1, 1, new DateTime(2025, 1, 20), dailyRate: 5.00m);

            var forecast = _service.ForecastRevenue(30,
                asOf: new DateTime(2025, 1, 31));

            Assert.IsTrue(forecast.ProjectedRevenue > 0m);
            Assert.AreEqual("average", forecast.Method);
            Assert.IsTrue(forecast.ConfidenceLow <= forecast.ProjectedRevenue);
            Assert.IsTrue(forecast.ConfidenceHigh >= forecast.ProjectedRevenue);
        }

        [TestMethod]
        public void ForecastRevenue_MultipleMonths_UsesRegression()
        {
            AddMovie(1, "M", Genre.Action, 5.00m);
            AddCustomer(1, "C");

            for (int m = 1; m <= 6; m++)
            {
                for (int i = 0; i < m + 2; i++)
                {
                    AddRental(1, 1, new DateTime(2025, m, 5 + i), dailyRate: 5.00m);
                }
            }

            var forecast = _service.ForecastRevenue(30,
                asOf: new DateTime(2025, 6, 30));

            Assert.IsTrue(forecast.ProjectedRevenue > 0m);
            Assert.AreEqual("linear_regression", forecast.Method);
            Assert.IsTrue(forecast.ConfidenceLow <= forecast.ProjectedRevenue);
            Assert.IsTrue(forecast.ConfidenceHigh >= forecast.ProjectedRevenue);
        }

        [TestMethod]
        public void ForecastRevenue_ForecastPeriodCorrect()
        {
            var asOf = new DateTime(2025, 6, 30);
            var forecast = _service.ForecastRevenue(60, asOf);

            Assert.AreEqual(asOf, forecast.ForecastStart);
            Assert.AreEqual(asOf.AddDays(60), forecast.ForecastEnd);
        }

        // ── GetPeakRevenueDay Tests ─────────────────────────────────

        [TestMethod]
        public void GetPeakRevenueDay_FindsHighestDay()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 10), dailyRate: 3.00m);
            AddRental(1, 1, new DateTime(2025, 1, 15), dailyRate: 10.00m);
            AddRental(1, 1, new DateTime(2025, 1, 15), dailyRate: 8.00m);
            AddRental(1, 1, new DateTime(2025, 1, 20), dailyRate: 5.00m);

            var peak = _service.GetPeakRevenueDay(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.AreEqual(new DateTime(2025, 1, 15), peak.Key);
            Assert.IsTrue(peak.Value > 0);
        }

        [TestMethod]
        public void GetPeakRevenueDay_Empty_ReturnsZero()
        {
            var peak = _service.GetPeakRevenueDay(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.AreEqual(0m, peak.Value);
        }

        // ── GetRevenueByDayOfWeek Tests ─────────────────────────────

        [TestMethod]
        public void GetRevenueByDayOfWeek_ReturnsAllDays()
        {
            var result = _service.GetRevenueByDayOfWeek(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.AreEqual(7, result.Count);
            Assert.IsTrue(result.ContainsKey(DayOfWeek.Monday));
            Assert.IsTrue(result.ContainsKey(DayOfWeek.Sunday));
        }

        [TestMethod]
        public void GetRevenueByDayOfWeek_CorrectDayAssignment()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "C");

            // Jan 15, 2025 = Wednesday
            AddRental(1, 1, new DateTime(2025, 1, 15), dailyRate: 10.00m);

            var result = _service.GetRevenueByDayOfWeek(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.IsTrue(result[DayOfWeek.Wednesday] > 0m);
            Assert.AreEqual(0m, result[DayOfWeek.Monday]);
        }

        // ── Average Revenue Per Day Tests ───────────────────────────

        [TestMethod]
        public void GetReport_AverageRevenuePerDay_CorrectCalculation()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 10), dailyRate: 5.00m, lateFee: 1.00m);

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            // 30 days in period
            Assert.IsTrue(report.AverageRevenuePerDay > 0m);
            Assert.IsTrue(report.AverageRevenuePerDay < report.TotalRevenue);
        }

        // ── Edge Cases ──────────────────────────────────────────────

        [TestMethod]
        public void GetReport_RentalsOutsidePeriod_Excluded()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2024, 12, 15));
            AddRental(1, 1, new DateTime(2025, 2, 15));

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.AreEqual(0, report.TotalRentals);
        }

        [TestMethod]
        public void GetReport_SameDayPeriod_Works()
        {
            AddMovie(1, "M", Genre.Action);
            AddCustomer(1, "C");

            AddRental(1, 1, new DateTime(2025, 1, 15));

            var report = _service.GetReport(
                new DateTime(2025, 1, 15), new DateTime(2025, 1, 15));

            Assert.AreEqual(1, report.TotalRentals);
        }

        [TestMethod]
        public void GetReport_MovieWithoutGenre_SkippedInGenreBreakdown()
        {
            var movie = new Movie { Id = 99, Name = "NoGenre", Genre = null };
            _movieRepo.Add(movie);
            AddCustomer(1, "C");

            AddRental(99, 1, new DateTime(2025, 1, 15));

            var report = _service.GetReport(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));

            Assert.AreEqual(1, report.TotalRentals);
            Assert.AreEqual(0, report.RevenueByGenre.Count);
        }
    }
}
