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
    public class RentalHistoryServiceTests
    {
        #region Test Helpers

        private class TestMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();
            private int _nextId = 1;

            public void Add(Movie movie)
            {
                if (movie.Id == 0) movie.Id = _nextId++;
                _movies[movie.Id] = movie;
            }

            public Movie GetById(int id) => _movies.TryGetValue(id, out var m) ? m : null;
            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList().AsReadOnly();
            public void Update(Movie movie) { _movies[movie.Id] = movie; }
            public void Remove(int id) { _movies.Remove(id); }
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) => new List<Movie>().AsReadOnly();
            public Movie GetRandom() => _movies.Values.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) => _movies.Values.ToList().AsReadOnly();
        }

        private class TestRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            private int _nextId = 1;

            public void Add(Rental rental)
            {
                if (rental.Id == 0) rental.Id = _nextId++;
                _rentals.Add(rental);
            }

            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals.AsReadOnly();
            public void Update(Rental rental) { var i = _rentals.FindIndex(r => r.Id == rental.Id); if (i >= 0) _rentals[i] = rental; }
            public void Remove(int id) { _rentals.RemoveAll(r => r.Id == id); }
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) => _rentals.Where(r => r.CustomerId == customerId && r.Status != RentalStatus.Returned).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByCustomer(int customerId) =>
                _rentals.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) => _rentals.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() => _rentals.Where(r => r.Status == RentalStatus.Overdue).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) => _rentals.ToList().AsReadOnly();
            public Rental ReturnRental(int rentalId) { var r = GetById(rentalId); if (r != null) r.Status = RentalStatus.Returned; return r; }
            public bool IsMovieRentedOut(int movieId) => _rentals.Any(r => r.MovieId == movieId && r.Status != RentalStatus.Returned);
            public Rental Checkout(Rental rental) { Add(rental); return rental; }

            public Rental Checkout(Rental rental, int maxConcurrentRentals)
            {
                return Checkout(rental);
            }
            public RentalStats GetStats() => new RentalStats { TotalRentals = _rentals.Count };
        }

        private class TestCustomerRepository : ICustomerRepository
        {
            private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
            private int _nextId = 1;

            public void Add(Customer c) { if (c.Id == 0) c.Id = _nextId++; _customers[c.Id] = c; }
            public Customer GetById(int id) => _customers.TryGetValue(id, out var c) ? c : null;
            public IReadOnlyList<Customer> GetAll() => _customers.Values.ToList().AsReadOnly();
            public void Update(Customer c) { _customers[c.Id] = c; }
            public void Remove(int id) { _customers.Remove(id); }
            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType) => _customers.Values.ToList().AsReadOnly();
            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) => new List<Customer>().AsReadOnly();
            public CustomerStats GetStats() => new CustomerStats { TotalCustomers = _customers.Count };
        }

        private TestMovieRepository _movieRepo;
        private TestRentalRepository _rentalRepo;
        private TestCustomerRepository _customerRepo;
        private RentalHistoryService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new TestMovieRepository();
            _rentalRepo = new TestRentalRepository();
            _customerRepo = new TestCustomerRepository();
            _service = new RentalHistoryService(_rentalRepo, _customerRepo, _movieRepo);
        }

        private void AddMovie(int id, string name, Genre genre, int rating = 4)
        {
            _movieRepo.Add(new Movie { Id = id, Name = name, Genre = genre, Rating = rating });
        }

        private void AddCustomer(int id, string name)
        {
            _customerRepo.Add(new Customer { Id = id, Name = name, MembershipType = MembershipType.Basic });
        }

        private Rental AddRental(int customerId, int movieId, DateTime rentalDate, int rentalDays = 7, RentalStatus status = RentalStatus.Active, DateTime? returnDate = null, decimal dailyRate = 3.99m, decimal lateFee = 0m)
        {
            var r = new Rental
            {
                CustomerId = customerId,
                MovieId = movieId,
                MovieName = _movieRepo.GetById(movieId)?.Name,
                CustomerName = _customerRepo.GetById(customerId)?.Name,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(rentalDays),
                ReturnDate = returnDate,
                Status = status,
                DailyRate = dailyRate,
                LateFee = lateFee
            };
            _rentalRepo.Add(r);
            return r;
        }

        private void SeedBasicData()
        {
            AddMovie(1, "Shrek", Genre.Animation);
            AddMovie(2, "The Godfather", Genre.Drama, 5);
            AddMovie(3, "Toy Story", Genre.Animation, 5);
            AddMovie(4, "Die Hard", Genre.Action, 4);
            AddMovie(5, "The Ring", Genre.Horror, 3);
            AddCustomer(1, "John Smith");
            AddCustomer(2, "Jane Doe");
            AddCustomer(3, "Bob Wilson");
        }

        #endregion

        #region Constructor Tests

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new RentalHistoryService(null, _customerRepo, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new RentalHistoryService(_rentalRepo, null, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new RentalHistoryService(_rentalRepo, _customerRepo, null);
        }

        #endregion

        #region GetRentalHistory Tests

        [TestMethod]
        public void GetRentalHistory_NoRentals_ReturnsEmpty()
        {
            var result = _service.GetRentalHistory(null, null, null, null, null);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetRentalHistory_AllNullFilters_ReturnsAll()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-5));
            AddRental(2, 2, DateTime.Today.AddDays(-3));

            var result = _service.GetRentalHistory(null, null, null, null, null);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void GetRentalHistory_FilterByCustomer()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10));
            AddRental(2, 2, DateTime.Today.AddDays(-5));

            var result = _service.GetRentalHistory(1, null, null, null, null);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].CustomerId);
        }

        [TestMethod]
        public void GetRentalHistory_FilterByMovie()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10));
            AddRental(2, 2, DateTime.Today.AddDays(-5));

            var result = _service.GetRentalHistory(null, 2, null, null, null);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(2, result[0].MovieId);
        }

        [TestMethod]
        public void GetRentalHistory_FilterByDateRange()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-30));
            AddRental(1, 2, DateTime.Today.AddDays(-5));

            var result = _service.GetRentalHistory(null, null, DateTime.Today.AddDays(-10), DateTime.Today, null);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void GetRentalHistory_FilterByStatus()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-5));
            AddRental(2, 2, DateTime.Today.AddDays(-3));

            var result = _service.GetRentalHistory(null, null, null, null, RentalStatus.Active);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(RentalStatus.Active, result[0].Status);
        }

        [TestMethod]
        public void GetRentalHistory_OrderedByDateDescending()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-20));
            AddRental(1, 2, DateTime.Today.AddDays(-5));
            AddRental(1, 3, DateTime.Today.AddDays(-10));

            var result = _service.GetRentalHistory(null, null, null, null, null);
            Assert.IsTrue(result[0].RentalDate >= result[1].RentalDate);
            Assert.IsTrue(result[1].RentalDate >= result[2].RentalDate);
        }

        [TestMethod]
        public void GetRentalHistory_ComputesFields()
        {
            SeedBasicData();
            var rentalDate = DateTime.Today.AddDays(-10);
            var returnDate = DateTime.Today.AddDays(-3);
            AddRental(1, 1, rentalDate, rentalDays: 5, status: RentalStatus.Returned, returnDate: returnDate, lateFee: 3.0m);

            var result = _service.GetRentalHistory(null, null, null, null, null);
            Assert.AreEqual(1, result.Count);
            var entry = result[0];
            Assert.AreEqual("Shrek", entry.MovieName);
            Assert.AreEqual(Genre.Animation, entry.MovieGenre);
            Assert.IsTrue(entry.WasLate);
            Assert.IsTrue(entry.RentalDurationDays > 0);
        }

        [TestMethod]
        public void GetRentalHistory_MultipleFilters()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-5));
            AddRental(1, 2, DateTime.Today.AddDays(-3));
            AddRental(2, 1, DateTime.Today.AddDays(-2));

            var result = _service.GetRentalHistory(1, null, null, null, RentalStatus.Active);
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1, result[0].CustomerId);
        }

        #endregion

        #region GetCustomerTimeline Tests

        [TestMethod]
        public void GetCustomerTimeline_NoRentals_ReturnsEmpty()
        {
            SeedBasicData();
            var result = _service.GetCustomerTimeline(1);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetCustomerTimeline_SingleRental_HasRentedEvent()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var result = _service.GetCustomerTimeline(1);
            Assert.IsTrue(result.Any(e => e.EventType == TimelineEventType.Rented));
        }

        [TestMethod]
        public void GetCustomerTimeline_ReturnedRental_HasBothEvents()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-5));

            var result = _service.GetCustomerTimeline(1);
            Assert.IsTrue(result.Any(e => e.EventType == TimelineEventType.Rented));
            Assert.IsTrue(result.Any(e => e.EventType == TimelineEventType.Returned));
        }

        [TestMethod]
        public void GetCustomerTimeline_LateFee_HasLateFeeEvent()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10), rentalDays: 5, status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-2), lateFee: 4.5m);

            var result = _service.GetCustomerTimeline(1);
            var lateFeeEvent = result.FirstOrDefault(e => e.EventType == TimelineEventType.LateFee);
            Assert.IsNotNull(lateFeeEvent);
            Assert.AreEqual(4.5m, lateFeeEvent.Amount);
        }

        [TestMethod]
        public void GetCustomerTimeline_Overdue_HasOverdueWarning()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-20), rentalDays: 7, status: RentalStatus.Overdue);

            var result = _service.GetCustomerTimeline(1);
            Assert.IsTrue(result.Any(e => e.EventType == TimelineEventType.OverdueWarning));
        }

        [TestMethod]
        public void GetCustomerTimeline_OrderedChronologically()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-20), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-15));
            AddRental(1, 2, DateTime.Today.AddDays(-10), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-5));

            var result = _service.GetCustomerTimeline(1);
            for (int i = 1; i < result.Count; i++)
            {
                Assert.IsTrue(result[i].Date >= result[i - 1].Date || result[i].EventType >= result[i - 1].EventType);
            }
        }

        [TestMethod]
        public void GetCustomerTimeline_OnlyShowsRequestedCustomer()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10));
            AddRental(2, 2, DateTime.Today.AddDays(-5));

            var result = _service.GetCustomerTimeline(1);
            Assert.IsTrue(result.All(e => e.MovieName == "Shrek"));
        }

        #endregion

        #region GetPopularTimes Tests

        [TestMethod]
        public void GetPopularTimes_NoRentals_ReturnsEmptyResult()
        {
            var result = _service.GetPopularTimes();
            Assert.AreEqual(0, result.TotalRentals);
            Assert.IsNull(result.BusiestDay);
            Assert.IsNull(result.BusiestHour);
        }

        [TestMethod]
        public void GetPopularTimes_SingleRental_SetsBusiestDay()
        {
            SeedBasicData();
            var date = new DateTime(2025, 6, 2, 14, 0, 0); // Monday
            AddRental(1, 1, date);

            var result = _service.GetPopularTimes();
            Assert.AreEqual(1, result.TotalRentals);
            Assert.AreEqual(DayOfWeek.Monday, result.BusiestDay);
            Assert.AreEqual(14, result.BusiestHour);
        }

        [TestMethod]
        public void GetPopularTimes_MultipleRentals_FindsBusiest()
        {
            SeedBasicData();
            // 2 on Monday, 1 on Tuesday
            AddRental(1, 1, new DateTime(2025, 6, 2, 10, 0, 0)); // Monday
            AddRental(1, 2, new DateTime(2025, 6, 9, 10, 0, 0)); // Monday
            AddRental(2, 3, new DateTime(2025, 6, 3, 15, 0, 0)); // Tuesday

            var result = _service.GetPopularTimes();
            Assert.AreEqual(3, result.TotalRentals);
            Assert.AreEqual(DayOfWeek.Monday, result.BusiestDay);
            Assert.AreEqual(10, result.BusiestHour);
        }

        #endregion

        #region GetRetentionMetrics Tests

        [TestMethod]
        public void GetRetentionMetrics_NoRentals_ReturnsDefaults()
        {
            SeedBasicData();
            var result = _service.GetRetentionMetrics();
            Assert.AreEqual(0, result.ReturnRate);
            Assert.AreEqual(0, result.RepeatRentalRate);
            Assert.AreEqual(0, result.AverageGapDays);
        }

        [TestMethod]
        public void GetRetentionMetrics_AllReturned_FullReturnRate()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-20), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-15));
            AddRental(2, 2, DateTime.Today.AddDays(-10), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-5));

            var result = _service.GetRetentionMetrics();
            Assert.AreEqual(1.0, result.ReturnRate, 0.01);
        }

        [TestMethod]
        public void GetRetentionMetrics_RepeatCustomers()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-20));
            AddRental(1, 2, DateTime.Today.AddDays(-10));
            AddRental(2, 3, DateTime.Today.AddDays(-5));

            var result = _service.GetRetentionMetrics();
            Assert.AreEqual(1, result.RepeatCustomers);
            Assert.AreEqual(0.5, result.RepeatRentalRate, 0.01);
        }

        [TestMethod]
        public void GetRetentionMetrics_AverageGap()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-20));
            AddRental(1, 2, DateTime.Today.AddDays(-10));

            var result = _service.GetRetentionMetrics();
            Assert.AreEqual(10.0, result.AverageGapDays, 0.1);
        }

        [TestMethod]
        public void GetRetentionMetrics_ChurnRiskLevels()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));   // Low
            AddRental(2, 2, DateTime.Today.AddDays(-60));  // Medium
            AddRental(3, 3, DateTime.Today.AddDays(-120)); // High

            var result = _service.GetRetentionMetrics();
            Assert.AreEqual(3, result.ChurnRisks.Count);

            var c1 = result.ChurnRisks.First(c => c.CustomerId == 1);
            var c2 = result.ChurnRisks.First(c => c.CustomerId == 2);
            var c3 = result.ChurnRisks.First(c => c.CustomerId == 3);
            Assert.AreEqual("Low", c1.RiskLevel);
            Assert.AreEqual("Medium", c2.RiskLevel);
            Assert.AreEqual("High", c3.RiskLevel);
        }

        [TestMethod]
        public void GetRetentionMetrics_CustomerNames()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var result = _service.GetRetentionMetrics();
            Assert.AreEqual("John Smith", result.ChurnRisks[0].CustomerName);
        }

        #endregion

        #region GetInventoryForecast Tests

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetInventoryForecast_NegativeDays_Throws()
        {
            _service.GetInventoryForecast(-1);
        }

        [TestMethod]
        public void GetInventoryForecast_NoMovies_EmptyPredictions()
        {
            var result = _service.GetInventoryForecast(7);
            Assert.AreEqual(0, result.Predictions.Count);
        }

        [TestMethod]
        public void GetInventoryForecast_AllAvailable()
        {
            SeedBasicData();
            var result = _service.GetInventoryForecast(7);
            Assert.IsTrue(result.Predictions.All(p => p.CurrentlyAvailable));
        }

        [TestMethod]
        public void GetInventoryForecast_RentedOut_NotAvailable()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-2));

            var result = _service.GetInventoryForecast(7);
            var shrek = result.Predictions.First(p => p.MovieId == 1);
            Assert.IsFalse(shrek.CurrentlyAvailable);
            Assert.IsNotNull(shrek.EstimatedAvailableDate);
        }

        [TestMethod]
        public void GetInventoryForecast_UsesAverageDuration()
        {
            SeedBasicData();
            // Two past rentals of 5 and 7 days
            AddRental(1, 1, DateTime.Today.AddDays(-30), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-25)); // 5 days
            AddRental(2, 1, DateTime.Today.AddDays(-20), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-13)); // 7 days
            // Current rental
            AddRental(3, 1, DateTime.Today.AddDays(-1));

            var result = _service.GetInventoryForecast(7);
            var shrek = result.Predictions.First(p => p.MovieId == 1);
            Assert.IsFalse(shrek.CurrentlyAvailable);
            Assert.IsTrue(shrek.AverageRentalDurationDays > 0);
        }

        [TestMethod]
        public void GetInventoryForecast_ZeroDaysAhead()
        {
            SeedBasicData();
            var result = _service.GetInventoryForecast(0);
            Assert.AreEqual(0, result.DaysAhead);
            Assert.AreEqual(5, result.Predictions.Count);
        }

        #endregion

        #region GetLoyaltyScore Tests

        [TestMethod]
        public void GetLoyaltyScore_NoRentals_ZeroScore()
        {
            SeedBasicData();
            var result = _service.GetLoyaltyScore(1);
            Assert.AreEqual(0, result.Score);
            Assert.AreEqual("Bronze", result.Tier);
        }

        [TestMethod]
        public void GetLoyaltyScore_FrequentCustomer_HighFrequencyPoints()
        {
            SeedBasicData();
            for (int i = 0; i < 10; i++)
                AddRental(1, 1, DateTime.Today.AddDays(-i));

            var result = _service.GetLoyaltyScore(1);
            Assert.AreEqual(30, result.Breakdown.FrequencyPoints); // 10 * 3 = 30 (max)
        }

        [TestMethod]
        public void GetLoyaltyScore_RecentRental_HighRecencyPoints()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-3));

            var result = _service.GetLoyaltyScore(1);
            Assert.AreEqual(25, result.Breakdown.RecencyPoints);
        }

        [TestMethod]
        public void GetLoyaltyScore_OldRental_LowRecencyPoints()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-200));

            var result = _service.GetLoyaltyScore(1);
            Assert.AreEqual(0, result.Breakdown.RecencyPoints);
        }

        [TestMethod]
        public void GetLoyaltyScore_AllOnTime_MaxOnTimePoints()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10), rentalDays: 14, status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-5));

            var result = _service.GetLoyaltyScore(1);
            Assert.AreEqual(25, result.Breakdown.OnTimePoints);
        }

        [TestMethod]
        public void GetLoyaltyScore_AllLate_ZeroOnTimePoints()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-20), rentalDays: 3, status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-5));

            var result = _service.GetLoyaltyScore(1);
            Assert.AreEqual(0, result.Breakdown.OnTimePoints);
        }

        [TestMethod]
        public void GetLoyaltyScore_HighSpend_SpendPoints()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5), dailyRate: 20m);

            var result = _service.GetLoyaltyScore(1);
            Assert.IsTrue(result.Breakdown.SpendPoints > 0);
        }

        [TestMethod]
        public void GetLoyaltyScore_Tiers_Bronze()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-200));
            var result = _service.GetLoyaltyScore(1);
            Assert.AreEqual("Bronze", result.Tier);
        }

        [TestMethod]
        public void GetLoyaltyScore_Tiers_Silver()
        {
            SeedBasicData();
            // ~40-59 points needed for Silver: 3 rentals (9 freq) + 60-day recency (15) + on-time (25) = 49
            for (int i = 0; i < 3; i++)
                AddRental(1, 1, DateTime.Today.AddDays(-50 - i), rentalDays: 30, status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-45 - i), dailyRate: 1m);

            var result = _service.GetLoyaltyScore(1);
            Assert.IsTrue(result.Score >= 40 && result.Score < 60, $"Expected Silver tier range, got score {result.Score}");
            Assert.AreEqual("Silver", result.Tier);
        }

        [TestMethod]
        public void GetLoyaltyScore_ScoreMaxIs100()
        {
            SeedBasicData();
            for (int i = 0; i < 20; i++)
                AddRental(1, 1, DateTime.Today.AddDays(-i), dailyRate: 50m, rentalDays: 30, status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-i));

            var result = _service.GetLoyaltyScore(1);
            Assert.IsTrue(result.Score <= 100);
        }

        [TestMethod]
        public void GetLoyaltyScore_UnknownCustomer_ReturnsResult()
        {
            var result = _service.GetLoyaltyScore(999);
            Assert.AreEqual(0, result.Score);
            Assert.AreEqual("Unknown", result.CustomerName);
        }

        [TestMethod]
        public void GetLoyaltyScore_ActiveRentalsNoReturns_NeutralOnTime()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-3));

            var result = _service.GetLoyaltyScore(1);
            Assert.AreEqual(12, result.Breakdown.OnTimePoints);
        }

        #endregion

        #region GetSeasonalTrends Tests

        [TestMethod]
        public void GetSeasonalTrends_NoRentals_ReturnsEmpty()
        {
            var result = _service.GetSeasonalTrends();
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetSeasonalTrends_GroupsByGenreAndMonth()
        {
            SeedBasicData();
            AddRental(1, 1, new DateTime(2025, 6, 1)); // Animation, June
            AddRental(1, 3, new DateTime(2025, 6, 15)); // Animation, June
            AddRental(2, 2, new DateTime(2025, 7, 1)); // Drama, July

            var result = _service.GetSeasonalTrends();
            var animJune = result.FirstOrDefault(t => t.Genre == Genre.Animation && t.Month == 6);
            Assert.IsNotNull(animJune);
            Assert.AreEqual(2, animJune.Count);
        }

        [TestMethod]
        public void GetSeasonalTrends_OrderedByMonth()
        {
            SeedBasicData();
            AddRental(1, 1, new DateTime(2025, 12, 1));
            AddRental(2, 2, new DateTime(2025, 1, 1));

            var result = _service.GetSeasonalTrends();
            Assert.IsTrue(result.First().Month <= result.Last().Month);
        }

        [TestMethod]
        public void GetSeasonalTrends_SkipsMoviesWithNoGenre()
        {
            _movieRepo.Add(new Movie { Id = 10, Name = "No Genre Movie" });
            AddCustomer(1, "Test");
            AddRental(1, 10, DateTime.Today.AddDays(-5));

            var result = _service.GetSeasonalTrends();
            Assert.AreEqual(0, result.Count);
        }

        #endregion

        #region GenerateReport Tests

        [TestMethod]
        public void GenerateReport_Summary_HasTitle()
        {
            var result = _service.GenerateReport(ReportType.Summary);
            Assert.AreEqual("Rental Summary Report", result.Title);
            Assert.AreEqual(ReportType.Summary, result.Type);
        }

        [TestMethod]
        public void GenerateReport_Summary_HasOverviewSection()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var result = _service.GenerateReport(ReportType.Summary);
            Assert.IsTrue(result.Sections.Any(s => s.Heading == "Overview"));
        }

        [TestMethod]
        public void GenerateReport_Detailed_HasMultipleSections()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var result = _service.GenerateReport(ReportType.Detailed);
            Assert.IsTrue(result.Sections.Count >= 3);
        }

        [TestMethod]
        public void GenerateReport_CustomerFocused_HasTopCustomers()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var result = _service.GenerateReport(ReportType.CustomerFocused);
            Assert.IsTrue(result.Sections.Any(s => s.Heading == "Top Customers"));
        }

        [TestMethod]
        public void GenerateReport_MovieFocused_HasTopMovies()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var result = _service.GenerateReport(ReportType.MovieFocused);
            Assert.IsTrue(result.Sections.Any(s => s.Heading == "Top Movies"));
        }

        [TestMethod]
        public void GenerateReport_Summary_Empty_NoError()
        {
            var result = _service.GenerateReport(ReportType.Summary);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Sections.Count > 0);
        }

        [TestMethod]
        public void GenerateReport_HasGeneratedAt()
        {
            var before = DateTime.Now;
            var result = _service.GenerateReport(ReportType.Summary);
            Assert.IsTrue(result.GeneratedAt >= before);
        }

        [TestMethod]
        public void GenerateReport_Detailed_IncludesRetention()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));
            AddRental(1, 2, DateTime.Today.AddDays(-3));

            var result = _service.GenerateReport(ReportType.Detailed);
            Assert.IsTrue(result.Sections.Any(s => s.Heading == "Retention"));
        }

        [TestMethod]
        public void GenerateReport_MovieFocused_HasSeasonalTrends()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var result = _service.GenerateReport(ReportType.MovieFocused);
            Assert.IsTrue(result.Sections.Any(s => s.Heading == "Seasonal Trends"));
        }

        [TestMethod]
        public void GenerateReport_CustomerFocused_ShowsSpend()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5), dailyRate: 10m);

            var result = _service.GenerateReport(ReportType.CustomerFocused);
            var topSection = result.Sections.First(s => s.Heading == "Top Customers");
            Assert.IsTrue(topSection.Content.Contains("spent"));
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public void GetRentalHistory_NonExistentCustomer_ReturnsEmpty()
        {
            SeedBasicData();
            var result = _service.GetRentalHistory(999, null, null, null, null);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetCustomerTimeline_NonExistentCustomer_ReturnsEmpty()
        {
            var result = _service.GetCustomerTimeline(999);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetLoyaltyScore_CustomerIdZero_Works()
        {
            var result = _service.GetLoyaltyScore(0);
            Assert.AreEqual(0, result.Score);
        }

        [TestMethod]
        public void GetRetentionMetrics_SingleCustomerSingleRental_NoRepeat()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-5));

            var result = _service.GetRetentionMetrics();
            Assert.AreEqual(0, result.RepeatCustomers);
            Assert.AreEqual(0.0, result.RepeatRentalRate);
        }

        [TestMethod]
        public void GetInventoryForecast_ReturnedMovie_Available()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-10), status: RentalStatus.Returned, returnDate: DateTime.Today.AddDays(-5));

            var result = _service.GetInventoryForecast(7);
            var shrek = result.Predictions.First(p => p.MovieId == 1);
            Assert.IsTrue(shrek.CurrentlyAvailable);
        }

        [TestMethod]
        public void GetSeasonalTrends_MultipleYears_AggregatesByMonth()
        {
            SeedBasicData();
            AddRental(1, 1, new DateTime(2024, 6, 1)); // Animation, June 2024
            AddRental(2, 3, new DateTime(2025, 6, 1)); // Animation, June 2025

            var result = _service.GetSeasonalTrends();
            var animJune = result.First(t => t.Genre == Genre.Animation && t.Month == 6);
            Assert.AreEqual(2, animJune.Count);
        }

        [TestMethod]
        public void GetRentalHistory_WasLate_ActiveOverdue()
        {
            SeedBasicData();
            AddRental(1, 1, DateTime.Today.AddDays(-20), rentalDays: 5);

            var result = _service.GetRentalHistory(null, null, null, null, null);
            Assert.IsTrue(result[0].WasLate);
        }

        [TestMethod]
        public void GetPopularTimes_PopulatesDayOfWeekDictionary()
        {
            SeedBasicData();
            AddRental(1, 1, new DateTime(2025, 6, 2)); // Monday
            AddRental(2, 2, new DateTime(2025, 6, 3)); // Tuesday

            var result = _service.GetPopularTimes();
            Assert.AreEqual(2, result.RentalsByDayOfWeek.Count);
            Assert.IsTrue(result.RentalsByDayOfWeek.ContainsKey(DayOfWeek.Monday));
            Assert.IsTrue(result.RentalsByDayOfWeek.ContainsKey(DayOfWeek.Tuesday));
        }

        #endregion
    }
}
