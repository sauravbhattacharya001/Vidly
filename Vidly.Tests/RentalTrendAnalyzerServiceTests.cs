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
    public class RentalTrendAnalyzerServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryRentalRepository _rentalRepo;
        private RentalTrendAnalyzerService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _rentalRepo = new InMemoryRentalRepository();

            // Seed movies across genres
            _movieRepo.Add(new Movie { Id = 1, Name = "Action Hero", Genre = Genre.Action, Rating = 4 });
            _movieRepo.Add(new Movie { Id = 2, Name = "Romantic Sunset", Genre = Genre.Romance, Rating = 3 });
            _movieRepo.Add(new Movie { Id = 3, Name = "Space Wars", Genre = Genre.Action, Rating = 5 });
            _movieRepo.Add(new Movie { Id = 4, Name = "Laugh Riot", Genre = Genre.Comedy, Rating = 3 });
            _movieRepo.Add(new Movie { Id = 5, Name = "Dark Mystery", Genre = Genre.Thriller, Rating = 4 });

            // Seed customers
            _customerRepo.Add(new Customer { Id = 1, Name = "Alice" });
            _customerRepo.Add(new Customer { Id = 2, Name = "Bob" });
            _customerRepo.Add(new Customer { Id = 3, Name = "Carol" });

            _service = new RentalTrendAnalyzerService(_rentalRepo, _movieRepo, _customerRepo);
        }

        private Rental MakeRental(int id, int customerId, int movieId, DateTime date, decimal rate = 2.99m)
        {
            return new Rental
            {
                Id = id,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = date,
                DueDate = date.AddDays(3),
                DailyRate = rate,
                Status = RentalStatus.Returned,
                ReturnDate = date.AddDays(2)
            };
        }

        // ── Constructor ─────────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RentalTrendAnalyzerService(null, _movieRepo, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RentalTrendAnalyzerService(_rentalRepo, null, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new RentalTrendAnalyzerService(_rentalRepo, _movieRepo, null);
        }

        // ── Volume time-series ──────────────────────────────────────────

        [TestMethod]
        public void GetVolumeTimeSeries_EmptyRange_ReturnsEmptyBuckets()
        {
            var result = _service.GetVolumeTimeSeries(
                new DateTime(2026, 1, 1), new DateTime(2026, 3, 1));
            Assert.IsNotNull(result);
            Assert.AreEqual(TrendGranularity.Monthly, result.Granularity);
            Assert.IsTrue(result.Buckets.All(b => b.RentalCount == 0));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetVolumeTimeSeries_InvalidRange_Throws()
        {
            _service.GetVolumeTimeSeries(new DateTime(2026, 3, 1), new DateTime(2026, 1, 1));
        }

        [TestMethod]
        public void GetVolumeTimeSeries_RisingTrend_DetectsRising()
        {
            // Few rentals in Jan, many in Feb
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 1, 10)));
            _rentalRepo.Add(MakeRental(2, 1, 2, new DateTime(2026, 2, 5)));
            _rentalRepo.Add(MakeRental(3, 2, 3, new DateTime(2026, 2, 10)));
            _rentalRepo.Add(MakeRental(4, 3, 4, new DateTime(2026, 2, 15)));
            _rentalRepo.Add(MakeRental(5, 1, 5, new DateTime(2026, 2, 20)));

            var result = _service.GetVolumeTimeSeries(
                new DateTime(2026, 1, 1), new DateTime(2026, 3, 1));

            Assert.AreEqual(TrendDirection.Rising, result.OverallDirection);
            Assert.IsTrue(result.ChangePercent > 0);
        }

        [TestMethod]
        public void GetVolumeTimeSeries_FallingTrend_DetectsFalling()
        {
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 1, 5)));
            _rentalRepo.Add(MakeRental(2, 2, 2, new DateTime(2026, 1, 10)));
            _rentalRepo.Add(MakeRental(3, 3, 3, new DateTime(2026, 1, 15)));
            _rentalRepo.Add(MakeRental(4, 1, 4, new DateTime(2026, 1, 20)));

            var result = _service.GetVolumeTimeSeries(
                new DateTime(2026, 1, 1), new DateTime(2026, 3, 1));

            Assert.AreEqual(TrendDirection.Declining, result.OverallDirection);
        }

        [TestMethod]
        public void GetVolumeTimeSeries_DailyGranularity_CreatesCorrectBuckets()
        {
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 1, 1)));

            var result = _service.GetVolumeTimeSeries(
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 4),
                TrendGranularity.Daily);

            Assert.AreEqual(3, result.Buckets.Count);
            Assert.AreEqual(1, result.Buckets[0].RentalCount);
        }

        [TestMethod]
        public void GetVolumeTimeSeries_WeeklyGranularity_Works()
        {
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 1, 3)));
            _rentalRepo.Add(MakeRental(2, 2, 2, new DateTime(2026, 1, 10)));

            var result = _service.GetVolumeTimeSeries(
                new DateTime(2026, 1, 1), new DateTime(2026, 1, 15),
                TrendGranularity.Weekly);

            Assert.AreEqual(2, result.Buckets.Count);
        }

        [TestMethod]
        public void GetVolumeTimeSeries_TracksRevenue()
        {
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 1, 5), 3.99m));
            _rentalRepo.Add(MakeRental(2, 2, 2, new DateTime(2026, 1, 10), 4.99m));

            var result = _service.GetVolumeTimeSeries(
                new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            Assert.AreEqual(3.99m + 4.99m, result.Buckets[0].Revenue);
        }

        // ── Genre momentum ──────────────────────────────────────────────

        [TestMethod]
        public void GetGenreMomentum_DetectsRisingGenre()
        {
            // Action rising: 0 in prev period, 3 in current
            var asOf = new DateTime(2026, 3, 1);
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 2, 10)));
            _rentalRepo.Add(MakeRental(2, 2, 3, new DateTime(2026, 2, 15)));
            _rentalRepo.Add(MakeRental(3, 3, 1, new DateTime(2026, 2, 20)));

            var result = _service.GetGenreMomentum(asOf, 30);
            var action = result.FirstOrDefault(g => g.Genre == "Action");

            Assert.IsNotNull(action);
            Assert.AreEqual(TrendDirection.Rising, action.Direction);
            Assert.AreEqual(3, action.CurrentPeriodRentals);
        }

        [TestMethod]
        public void GetGenreMomentum_DetectsFallingGenre()
        {
            var asOf = new DateTime(2026, 3, 1);
            // Romance popular in Jan, gone in Feb
            _rentalRepo.Add(MakeRental(1, 1, 2, new DateTime(2026, 1, 10)));
            _rentalRepo.Add(MakeRental(2, 2, 2, new DateTime(2026, 1, 15)));
            _rentalRepo.Add(MakeRental(3, 3, 2, new DateTime(2026, 1, 20)));

            var result = _service.GetGenreMomentum(asOf, 30);
            var romance = result.FirstOrDefault(g => g.Genre == "Romance");

            Assert.IsNotNull(romance);
            Assert.AreEqual(TrendDirection.Declining, romance.Direction);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetGenreMomentum_ZeroPeriod_Throws()
        {
            _service.GetGenreMomentum(DateTime.Now, 0);
        }

        [TestMethod]
        public void GetGenreMomentum_TracksRankChange()
        {
            var asOf = new DateTime(2026, 3, 1);
            // Previous: Comedy #1, Action #2
            _rentalRepo.Add(MakeRental(1, 1, 4, new DateTime(2026, 1, 10)));
            _rentalRepo.Add(MakeRental(2, 2, 4, new DateTime(2026, 1, 15)));
            _rentalRepo.Add(MakeRental(3, 3, 1, new DateTime(2026, 1, 20)));
            // Current: Action #1, Comedy #2
            _rentalRepo.Add(MakeRental(4, 1, 1, new DateTime(2026, 2, 10)));
            _rentalRepo.Add(MakeRental(5, 2, 3, new DateTime(2026, 2, 15)));
            _rentalRepo.Add(MakeRental(6, 3, 4, new DateTime(2026, 2, 20)));

            var result = _service.GetGenreMomentum(asOf, 30);
            var action = result.FirstOrDefault(g => g.Genre == "Action");

            Assert.IsNotNull(action);
            Assert.IsTrue(action.RankChange > 0); // moved up
        }

        // ── Peak periods ────────────────────────────────────────────────

        [TestMethod]
        public void GetPeakPeriods_IdentifiesPeakDay()
        {
            // All rentals on a Monday
            var monday = new DateTime(2026, 1, 5); // Monday
            _rentalRepo.Add(MakeRental(1, 1, 1, monday.AddHours(10)));
            _rentalRepo.Add(MakeRental(2, 2, 2, monday.AddHours(14)));
            _rentalRepo.Add(MakeRental(3, 3, 3, monday.AddHours(18)));

            var result = _service.GetPeakPeriods(
                new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            Assert.AreEqual(DayOfWeek.Monday, result.PeakDay);
        }

        [TestMethod]
        public void GetPeakPeriods_Returns168Cells()
        {
            var result = _service.GetPeakPeriods(
                new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            Assert.AreEqual(7 * 24, result.Cells.Count); // 168 cells
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetPeakPeriods_InvalidRange_Throws()
        {
            _service.GetPeakPeriods(new DateTime(2026, 3, 1), new DateTime(2026, 1, 1));
        }

        [TestMethod]
        public void GetPeakPeriods_IntensityNormalized()
        {
            var monday = new DateTime(2026, 1, 5);
            _rentalRepo.Add(MakeRental(1, 1, 1, monday.AddHours(14)));

            var result = _service.GetPeakPeriods(
                new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            var peakCell = result.Cells.First(c => c.DayOfWeek == DayOfWeek.Monday && c.Hour == 14);
            Assert.AreEqual(1.0, peakCell.Intensity);
        }

        [TestMethod]
        public void GetPeakPeriods_ComputesWeekdayWeekendAvg()
        {
            // 3 rentals on Monday, 1 on Saturday
            var monday = new DateTime(2026, 1, 5);
            var saturday = new DateTime(2026, 1, 10);
            _rentalRepo.Add(MakeRental(1, 1, 1, monday.AddHours(10)));
            _rentalRepo.Add(MakeRental(2, 2, 2, monday.AddHours(14)));
            _rentalRepo.Add(MakeRental(3, 3, 3, monday.AddHours(18)));
            _rentalRepo.Add(MakeRental(4, 1, 4, saturday.AddHours(12)));

            var result = _service.GetPeakPeriods(
                new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            Assert.IsTrue(result.WeekdayAvg > result.WeekendAvg);
        }

        // ── Trending movies ─────────────────────────────────────────────

        [TestMethod]
        public void GetTrendingMovies_DetectsNewlyPopular()
        {
            var asOf = new DateTime(2026, 3, 1);
            // Movie 5 not rented in previous period, 3 times in current
            _rentalRepo.Add(MakeRental(1, 1, 5, new DateTime(2026, 2, 18)));
            _rentalRepo.Add(MakeRental(2, 2, 5, new DateTime(2026, 2, 20)));
            _rentalRepo.Add(MakeRental(3, 3, 5, new DateTime(2026, 2, 25)));

            var (trending, declining) = _service.GetTrendingMovies(asOf, 14, 5);

            Assert.IsTrue(trending.Any(t => t.MovieId == 5));
            Assert.AreEqual("Dark Mystery", trending.First(t => t.MovieId == 5).MovieName);
        }

        [TestMethod]
        public void GetTrendingMovies_DetectsDeclining()
        {
            var asOf = new DateTime(2026, 3, 1);
            // Movie 1 popular in prior period, none in current
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 2, 1)));
            _rentalRepo.Add(MakeRental(2, 2, 1, new DateTime(2026, 2, 5)));
            _rentalRepo.Add(MakeRental(3, 3, 1, new DateTime(2026, 2, 10)));

            var (trending, declining) = _service.GetTrendingMovies(asOf, 14, 5);

            Assert.IsTrue(declining.Any(d => d.MovieId == 1));
        }

        [TestMethod]
        public void GetTrendingMovies_SignalStrength_Strong()
        {
            var asOf = new DateTime(2026, 3, 1);
            _rentalRepo.Add(MakeRental(1, 1, 3, new DateTime(2026, 2, 1)));
            _rentalRepo.Add(MakeRental(2, 1, 3, new DateTime(2026, 2, 20)));
            _rentalRepo.Add(MakeRental(3, 2, 3, new DateTime(2026, 2, 22)));
            _rentalRepo.Add(MakeRental(4, 3, 3, new DateTime(2026, 2, 25)));

            var (trending, _) = _service.GetTrendingMovies(asOf, 14, 5);
            var movie3 = trending.FirstOrDefault(t => t.MovieId == 3);

            Assert.IsNotNull(movie3);
            Assert.AreEqual(TrendSignalStrength.Strong, movie3.Signal);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetTrendingMovies_ZeroPeriod_Throws()
        {
            _service.GetTrendingMovies(DateTime.Now, 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetTrendingMovies_ZeroTopN_Throws()
        {
            _service.GetTrendingMovies(DateTime.Now, 14, 0);
        }

        // ── Customer segments ───────────────────────────────────────────

        [TestMethod]
        public void GetCustomerSegments_CategorizesCorrectly()
        {
            var from = new DateTime(2026, 1, 1);
            var to = new DateTime(2026, 2, 1);

            // Alice: 9 rentals in 1 month = Heavy
            for (int i = 1; i <= 9; i++)
                _rentalRepo.Add(MakeRental(i, 1, (i % 5) + 1, from.AddDays(i)));

            // Bob: 2 rentals = Light
            _rentalRepo.Add(MakeRental(20, 2, 1, from.AddDays(5)));
            _rentalRepo.Add(MakeRental(21, 2, 2, from.AddDays(15)));

            var result = _service.GetCustomerSegments(from, to);

            var heavy = result.FirstOrDefault(s => s.Segment == "Heavy");
            Assert.IsNotNull(heavy);
            Assert.AreEqual(1, heavy.CustomerCount); // Alice

            var light = result.FirstOrDefault(s => s.Segment == "Light");
            Assert.IsNotNull(light);
            Assert.AreEqual(1, light.CustomerCount); // Bob
        }

        [TestMethod]
        public void GetCustomerSegments_DetectsLapsed()
        {
            // Carol has a rental before the period but none during
            _rentalRepo.Add(MakeRental(1, 3, 1, new DateTime(2025, 12, 1)));

            var result = _service.GetCustomerSegments(
                new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            var lapsed = result.FirstOrDefault(s => s.Segment == "Lapsed");
            Assert.IsNotNull(lapsed);
            Assert.AreEqual(1, lapsed.CustomerCount);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetCustomerSegments_InvalidRange_Throws()
        {
            _service.GetCustomerSegments(new DateTime(2026, 3, 1), new DateTime(2026, 1, 1));
        }

        [TestMethod]
        public void GetCustomerSegments_ComputesAverages()
        {
            var from = new DateTime(2026, 1, 1);
            var to = new DateTime(2026, 2, 1);

            _rentalRepo.Add(MakeRental(1, 1, 1, from.AddDays(1), 3.99m));
            _rentalRepo.Add(MakeRental(2, 1, 2, from.AddDays(5), 4.99m));

            var result = _service.GetCustomerSegments(from, to);
            var light = result.FirstOrDefault(s => s.Segment == "Light");

            Assert.IsNotNull(light);
            Assert.AreEqual(2.0, light.AvgRentalsPerCustomer);
        }

        // ── Full report ─────────────────────────────────────────────────

        [TestMethod]
        public void GenerateReport_ReturnsAllSections()
        {
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 1, 10)));
            _rentalRepo.Add(MakeRental(2, 2, 2, new DateTime(2026, 2, 15)));

            var report = _service.GenerateReport(
                new DateTime(2026, 1, 1), new DateTime(2026, 3, 1));

            Assert.IsNotNull(report.Volume);
            Assert.IsNotNull(report.GenreMomentum);
            Assert.IsNotNull(report.PeakPeriods);
            Assert.IsNotNull(report.TrendingMovies);
            Assert.IsNotNull(report.DecliningMovies);
            Assert.IsNotNull(report.CustomerSegments);
            Assert.IsNotNull(report.Insights);
            Assert.IsTrue(report.Insights.Count > 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GenerateReport_InvalidRange_Throws()
        {
            _service.GenerateReport(new DateTime(2026, 3, 1), new DateTime(2026, 1, 1));
        }

        [TestMethod]
        public void GenerateReport_SetsMetadata()
        {
            var from = new DateTime(2026, 1, 1);
            var to = new DateTime(2026, 3, 1);

            var report = _service.GenerateReport(from, to);

            Assert.AreEqual(from, report.AnalysisStart);
            Assert.AreEqual(to, report.AnalysisEnd);
            Assert.IsTrue(report.GeneratedAt <= DateTime.Now);
        }

        [TestMethod]
        public void GenerateReport_InsightsIncludePeakTime()
        {
            var monday = new DateTime(2026, 1, 5);
            _rentalRepo.Add(MakeRental(1, 1, 1, monday.AddHours(14)));

            var report = _service.GenerateReport(
                new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            Assert.IsTrue(report.Insights.Any(i => i.Contains("Peak rental time")));
        }

        [TestMethod]
        public void GenerateReport_InsightsDetectLapsed()
        {
            _rentalRepo.Add(MakeRental(1, 3, 1, new DateTime(2025, 12, 1)));

            var report = _service.GenerateReport(
                new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            Assert.IsTrue(report.Insights.Any(i => i.Contains("lapsed")));
        }

        [TestMethod]
        public void GetVolumeTimeSeries_QuarterlyGranularity_Works()
        {
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 1, 15)));
            _rentalRepo.Add(MakeRental(2, 2, 2, new DateTime(2026, 6, 15)));

            var result = _service.GetVolumeTimeSeries(
                new DateTime(2026, 1, 1), new DateTime(2026, 7, 1),
                TrendGranularity.Quarterly);

            Assert.AreEqual(2, result.Buckets.Count);
        }

        [TestMethod]
        public void GetVolumeTimeSeries_YearlyGranularity_Works()
        {
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2025, 6, 15)));
            _rentalRepo.Add(MakeRental(2, 2, 2, new DateTime(2026, 6, 15)));

            var result = _service.GetVolumeTimeSeries(
                new DateTime(2025, 1, 1), new DateTime(2027, 1, 1),
                TrendGranularity.Yearly);

            Assert.AreEqual(2, result.Buckets.Count);
        }

        [TestMethod]
        public void GetTrendingMovies_RespectsTopN()
        {
            var asOf = new DateTime(2026, 3, 1);
            for (int i = 1; i <= 5; i++)
                _rentalRepo.Add(MakeRental(i, 1, i, new DateTime(2026, 2, 20)));

            var (trending, _) = _service.GetTrendingMovies(asOf, 14, 2);
            Assert.IsTrue(trending.Count <= 2);
        }

        [TestMethod]
        public void GetGenreMomentum_ReturnsAllGenres()
        {
            var asOf = new DateTime(2026, 3, 1);
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 2, 10)));

            var result = _service.GetGenreMomentum(asOf, 30);

            // Should include all genres from movies, not just rented ones
            Assert.IsTrue(result.Count >= 1);
        }

        [TestMethod]
        public void GetPeakPeriods_EmptyRentals_ReturnsZeroIntensity()
        {
            var result = _service.GetPeakPeriods(
                new DateTime(2026, 1, 1), new DateTime(2026, 2, 1));

            Assert.IsTrue(result.Cells.All(c => c.Intensity == 0));
        }

        [TestMethod]
        public void GetVolumeTimeSeries_StableTrend_DetectsStable()
        {
            // Equal rentals in both halves
            _rentalRepo.Add(MakeRental(1, 1, 1, new DateTime(2026, 1, 15)));
            _rentalRepo.Add(MakeRental(2, 2, 2, new DateTime(2026, 2, 15)));

            var result = _service.GetVolumeTimeSeries(
                new DateTime(2026, 1, 1), new DateTime(2026, 3, 1));

            Assert.AreEqual(TrendDirection.Stable, result.OverallDirection);
        }

        [TestMethod]
        public void GenerateReport_WithTrendingMovie_InsightsIncludeHottest()
        {
            var asOf = new DateTime(2026, 3, 1);
            // Movie 5 trending
            _rentalRepo.Add(MakeRental(1, 1, 5, new DateTime(2026, 2, 18)));
            _rentalRepo.Add(MakeRental(2, 2, 5, new DateTime(2026, 2, 20)));
            _rentalRepo.Add(MakeRental(3, 3, 5, new DateTime(2026, 2, 25)));

            var report = _service.GenerateReport(
                new DateTime(2026, 1, 1), asOf);

            Assert.IsTrue(report.Insights.Any(i => i.Contains("Hottest title")));
        }

        [TestMethod]
        public void GetCustomerSegments_ModerateSegment()
        {
            var from = new DateTime(2026, 1, 1);
            var to = new DateTime(2026, 2, 1);

            // Alice: 5 rentals in 1 month = Moderate
            for (int i = 1; i <= 5; i++)
                _rentalRepo.Add(MakeRental(i, 1, (i % 5) + 1, from.AddDays(i * 3)));

            var result = _service.GetCustomerSegments(from, to);
            var moderate = result.FirstOrDefault(s => s.Segment == "Moderate");
            Assert.IsNotNull(moderate);
            Assert.AreEqual(1, moderate.CustomerCount);
        }
    }
}
