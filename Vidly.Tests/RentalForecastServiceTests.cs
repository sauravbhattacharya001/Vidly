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
    public class RentalForecastServiceTests
    {
        private InMemoryRentalRepository _rentalRepo;
        private InMemoryMovieRepository _movieRepo;
        private RentalForecastService _service;

        [TestInitialize]
        public void Setup()
        {
            _rentalRepo = new InMemoryRentalRepository();
            _movieRepo = new InMemoryMovieRepository();
            _service = new RentalForecastService(_rentalRepo, _movieRepo);
        }

        private Movie AddMovie(string name, Genre genre = Genre.Action)
        {
            var movie = new Movie { Name = name, Genre = genre, Rating = 4 };
            _movieRepo.Add(movie);
            return movie;
        }

        private Rental AddRental(int movieId, DateTime date, int customerId = 1)
        {
            var rental = new Rental
            {
                MovieId = movieId,
                CustomerId = customerId,
                RentalDate = date,
                DueDate = date.AddDays(7),
                ReturnDate = date.AddDays(5),
                DailyRate = 3.99m,
                Status = RentalStatus.Returned
            };
            _rentalRepo.Add(rental);
            return rental;
        }

        // ── Constructor ──────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RentalForecastService(null, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RentalForecastService(_rentalRepo, null);
        }

        // ── Day-of-Week Distribution ─────────────────────────────────

        [TestMethod]
        public void GetDayOfWeekDistribution_ReturnsAllSevenDays()
        {
            var result = _service.GetDayOfWeekDistribution();
            Assert.AreEqual(7, result.Days.Count);
            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
                Assert.IsTrue(result.Days.ContainsKey(day));
        }

        [TestMethod]
        public void GetDayOfWeekDistribution_PeakDayHasHighestCount()
        {
            var movie = AddMovie("ForecastTest");
            for (int i = 0; i < 20; i++)
                AddRental(movie.Id, new DateTime(2025, 1, 3).AddDays(i * 7));

            var result = _service.GetDayOfWeekDistribution();
            var peakCount = result.Days[result.PeakDay].Count;
            foreach (var day in result.Days.Values)
                Assert.IsTrue(peakCount >= day.Count);
        }

        [TestMethod]
        public void GetDayOfWeekDistribution_TotalMatchesSum()
        {
            var result = _service.GetDayOfWeekDistribution();
            var sumCounts = result.Days.Values.Sum(d => d.Count);
            Assert.AreEqual(result.TotalRentals, sumCounts);
        }

        [TestMethod]
        public void DayOfWeekDistribution_Empty_AllZeros()
        {
            var empty = DayOfWeekDistribution.Empty();
            Assert.AreEqual(7, empty.Days.Count);
            Assert.AreEqual(0, empty.TotalRentals);
            foreach (var d in empty.Days.Values)
                Assert.AreEqual(0, d.Count);
        }

        // ── Monthly Trends ───────────────────────────────────────────

        [TestMethod]
        public void GetMonthlyTrends_AddedRentalsAppear()
        {
            var movie = AddMovie("TrendTest");
            AddRental(movie.Id, new DateTime(2026, 11, 15));
            AddRental(movie.Id, new DateTime(2026, 11, 20));
            AddRental(movie.Id, new DateTime(2026, 12, 10));

            var result = _service.GetMonthlyTrends();
            var nov = result.FirstOrDefault(t => t.Year == 2026 && t.Month == 11);
            var dec = result.FirstOrDefault(t => t.Year == 2026 && t.Month == 12);
            Assert.IsNotNull(nov);
            Assert.IsNotNull(dec);
            Assert.AreEqual(2, nov.RentalCount);
            Assert.AreEqual(1, dec.RentalCount);
        }

        [TestMethod]
        public void GetMonthlyTrends_OrderedChronologically()
        {
            var result = _service.GetMonthlyTrends();
            for (int i = 1; i < result.Count; i++)
            {
                var prev = result[i - 1].Year * 100 + result[i - 1].Month;
                var curr = result[i].Year * 100 + result[i].Month;
                Assert.IsTrue(curr > prev);
            }
        }

        [TestMethod]
        public void GetMonthlyTrends_RevenueIsPositive()
        {
            var movie = AddMovie("RevTest");
            AddRental(movie.Id, new DateTime(2026, 10, 1));

            var result = _service.GetMonthlyTrends();
            var oct = result.First(t => t.Year == 2026 && t.Month == 10);
            Assert.IsTrue(oct.Revenue > 0);
            Assert.IsTrue(oct.AverageRevenuePerRental > 0);
        }

        // ── Genre Popularity ─────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetGenrePopularity_ZeroDays_Throws()
        {
            _service.GetGenrePopularity(0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetGenrePopularity_NegativeDays_Throws()
        {
            _service.GetGenrePopularity(-5);
        }

        [TestMethod]
        public void GetGenrePopularity_OrderedByTotalRentalsDesc()
        {
            var result = _service.GetGenrePopularity();
            for (int i = 1; i < result.Count; i++)
                Assert.IsTrue(result[i - 1].TotalRentals >= result[i].TotalRentals);
        }

        [TestMethod]
        public void GetGenrePopularity_SharePercentsSumToAbout100()
        {
            var result = _service.GetGenrePopularity();
            if (result.Count > 0)
            {
                var totalShare = result.Sum(g => g.SharePercent);
                Assert.IsTrue(totalShare >= 98.0 && totalShare <= 102.0,
                    $"Total share was {totalShare}");
            }
        }

        [TestMethod]
        public void GetGenrePopularity_RecentRentalsLeOrEqualTotal()
        {
            var result = _service.GetGenrePopularity();
            foreach (var g in result)
                Assert.IsTrue(g.RecentRentals <= g.TotalRentals);
        }

        [TestMethod]
        public void GetGenrePopularity_TrendIsValid()
        {
            var movie = AddMovie("TrendVal", Genre.Romance);
            for (int i = 0; i < 5; i++)
                AddRental(movie.Id, DateTime.Today.AddDays(-i));

            var result = _service.GetGenrePopularity();
            foreach (var g in result)
                Assert.IsTrue(
                    g.Trend == TrendDirection.Rising ||
                    g.Trend == TrendDirection.Stable ||
                    g.Trend == TrendDirection.Declining);
        }

        // ── Movie Velocity ───────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetMovieVelocity_NegativeTopN_Throws()
        {
            _service.GetMovieVelocity(-1);
        }

        [TestMethod]
        public void GetMovieVelocity_OrderedByVelocityDesc()
        {
            var result = _service.GetMovieVelocity(0);
            for (int i = 1; i < result.Count; i++)
                Assert.IsTrue(result[i - 1].RentalsPerMonth >= result[i].RentalsPerMonth);
        }

        [TestMethod]
        public void GetMovieVelocity_RespectsTopN()
        {
            for (int i = 0; i < 5; i++)
            {
                var m = AddMovie($"VelMovie{i}");
                AddRental(m.Id, DateTime.Today.AddDays(-i * 10));
            }
            var result = _service.GetMovieVelocity(2);
            Assert.IsTrue(result.Count <= 2);
        }

        [TestMethod]
        public void GetMovieVelocity_ZeroTopN_ReturnsMore()
        {
            var limited = _service.GetMovieVelocity(1);
            var unlimited = _service.GetMovieVelocity(0);
            Assert.IsTrue(unlimited.Count >= limited.Count);
        }

        [TestMethod]
        public void GetMovieVelocity_AverageGapCalculation()
        {
            var movie = AddMovie("GapCalc");
            AddRental(movie.Id, new DateTime(2025, 6, 1));
            AddRental(movie.Id, new DateTime(2025, 6, 11));
            AddRental(movie.Id, new DateTime(2025, 6, 21));

            var result = _service.GetMovieVelocity(0);
            var entry = result.First(v => v.MovieName == "GapCalc");
            Assert.AreEqual(10.0, entry.AverageDaysBetweenRentals);
        }

        [TestMethod]
        public void GetMovieVelocity_SingleRentalZeroGap()
        {
            var movie = AddMovie("SingleVel");
            AddRental(movie.Id, new DateTime(2025, 3, 1));

            var result = _service.GetMovieVelocity(0);
            var entry = result.First(v => v.MovieName == "SingleVel");
            Assert.AreEqual(0.0, entry.AverageDaysBetweenRentals);
        }

        [TestMethod]
        public void GetMovieVelocity_DaysSinceLastRental()
        {
            var movie = AddMovie("DaysAgo");
            var daysAgo = 25;
            AddRental(movie.Id, DateTime.Today.AddDays(-daysAgo));

            var result = _service.GetMovieVelocity(0);
            var entry = result.First(v => v.MovieName == "DaysAgo");
            Assert.AreEqual(daysAgo, entry.DaysSinceLastRental);
        }

        [TestMethod]
        public void GetMovieVelocity_IncludesMovieName()
        {
            var movie = AddMovie("NamedVel");
            AddRental(movie.Id, DateTime.Today);

            var result = _service.GetMovieVelocity(0);
            Assert.IsTrue(result.Any(v => v.MovieName == "NamedVel"));
        }

        [TestMethod]
        public void GetMovieVelocity_MultipleRentals()
        {
            var movie = AddMovie("MultiRental");
            for (int i = 0; i < 8; i++)
                AddRental(movie.Id, DateTime.Today.AddDays(-i * 5), customerId: i + 100);

            var result = _service.GetMovieVelocity(0);
            var entry = result.First(v => v.MovieName == "MultiRental");
            Assert.AreEqual(8, entry.TotalRentals);
            Assert.IsTrue(entry.RentalsPerMonth > 0);
        }

        // ── Demand Forecast ──────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ForecastDemand_ZeroDays_Throws()
        {
            _service.ForecastDemand(0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ForecastDemand_Over365_Throws()
        {
            _service.ForecastDemand(400);
        }

        [TestMethod]
        public void ForecastDemand_DefaultIs7Days()
        {
            var result = _service.ForecastDemand();
            Assert.AreEqual(7, result.Count);
        }

        [TestMethod]
        public void ForecastDemand_StartsTomorrow()
        {
            var result = _service.ForecastDemand();
            Assert.AreEqual(DateTime.Today.AddDays(1), result[0].Date);
        }

        [TestMethod]
        public void ForecastDemand_CustomStartDate()
        {
            var start = new DateTime(2026, 6, 1);
            var result = _service.ForecastDemand(3, start);
            Assert.AreEqual(start, result[0].Date);
            Assert.AreEqual(3, result.Count);
        }

        [TestMethod]
        public void ForecastDemand_ConsecutiveDates()
        {
            var result = _service.ForecastDemand(5);
            for (int i = 1; i < result.Count; i++)
                Assert.AreEqual(result[i - 1].Date.AddDays(1), result[i].Date);
        }

        [TestMethod]
        public void ForecastDemand_ConfidenceNonNegative()
        {
            var result = _service.ForecastDemand(30);
            foreach (var f in result)
                Assert.IsTrue(f.Confidence >= 0, $"Confidence was {f.Confidence}");
        }

        [TestMethod]
        public void ForecastDemand_ConfidenceDecreasesOrStays()
        {
            var movie = AddMovie("DecayTest");
            for (int i = 0; i < 60; i++)
                AddRental(movie.Id, DateTime.Today.AddDays(-i));

            var result = _service.ForecastDemand(14);
            Assert.IsTrue(result.First().Confidence >= result.Last().Confidence);
        }

        [TestMethod]
        public void ForecastDemand_PredictedNonNegative()
        {
            var result = _service.ForecastDemand(7);
            foreach (var f in result)
                Assert.IsTrue(f.PredictedRentals >= 0);
        }

        [TestMethod]
        public void ForecastDemand_OneDayWorks()
        {
            var result = _service.ForecastDemand(1);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void ForecastDemand_365DaysWorks()
        {
            var result = _service.ForecastDemand(365);
            Assert.AreEqual(365, result.Count);
        }

        // ── Inventory Recommendations ────────────────────────────────

        [TestMethod]
        public void GetInventoryRecommendations_ReturnsNonNull()
        {
            var result = _service.GetInventoryRecommendations();
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.HighDemandMovies);
            Assert.IsNotNull(result.DeadStockMovies);
            Assert.IsNotNull(result.NeverRentedMovies);
            Assert.IsNotNull(result.GenreGaps);
        }

        [TestMethod]
        public void GetInventoryRecommendations_NeverRentedDetection()
        {
            var movie = AddMovie("NeverRented42");

            var result = _service.GetInventoryRecommendations();
            var found = result.NeverRentedMovies
                .FirstOrDefault(m => m.MovieName == "NeverRented42");
            Assert.IsNotNull(found);
            Assert.AreEqual(RecommendedAction.NeedsPromotion, found.Action);
        }

        [TestMethod]
        public void GetInventoryRecommendations_DeadStockDetection()
        {
            var movie = AddMovie("DeadStock42");
            AddRental(movie.Id, DateTime.Today.AddDays(-150));

            var result = _service.GetInventoryRecommendations();
            var found = result.DeadStockMovies
                .FirstOrDefault(m => m.MovieName == "DeadStock42");
            Assert.IsNotNull(found, "Movie rented 150 days ago should be dead stock");
            Assert.AreEqual(RecommendedAction.ConsiderRetiring, found.Action);
        }

        [TestMethod]
        public void GetInventoryRecommendations_HasTimestamp()
        {
            var result = _service.GetInventoryRecommendations();
            Assert.IsTrue(result.GeneratedAt <= DateTime.Now);
            Assert.IsTrue(result.GeneratedAt > DateTime.Now.AddMinutes(-1));
        }

        [TestMethod]
        public void GetInventoryRecommendations_HighDemandIsStockMore()
        {
            var result = _service.GetInventoryRecommendations();
            foreach (var m in result.HighDemandMovies)
                Assert.AreEqual(RecommendedAction.StockMore, m.Action);
        }

        // ── Summary Report ───────────────────────────────────────────

        [TestMethod]
        public void GetForecastSummary_ContainsHeader()
        {
            var result = _service.GetForecastSummary();
            Assert.IsTrue(result.Contains("RENTAL DEMAND FORECAST"));
        }

        [TestMethod]
        public void GetForecastSummary_ContainsDayPattern()
        {
            var result = _service.GetForecastSummary();
            Assert.IsTrue(result.Contains("Day-of-Week Pattern"));
        }

        [TestMethod]
        public void GetForecastSummary_CustomDays()
        {
            var result = _service.GetForecastSummary(14);
            Assert.IsTrue(result.Contains("14-Day Forecast"));
        }

        [TestMethod]
        public void GetForecastSummary_NotEmpty()
        {
            var result = _service.GetForecastSummary();
            Assert.IsTrue(result.Length > 50);
        }

        // ── Edge Cases ───────────────────────────────────────────────

        [TestMethod]
        public void AllMethodsRunWithoutErrors()
        {
            _service.GetDayOfWeekDistribution();
            _service.GetMonthlyTrends();
            _service.GetGenrePopularity();
            _service.GetMovieVelocity();
            _service.ForecastDemand();
            _service.GetInventoryRecommendations();
            _service.GetForecastSummary();
        }
    }
}
