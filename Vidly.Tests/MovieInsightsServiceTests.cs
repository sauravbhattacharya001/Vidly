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
    public class MovieInsightsServiceTests
    {
        #region Test Helpers

        private class TestMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();

            public void Add(Movie movie) { _movies[movie.Id] = movie; }
            public Movie GetById(int id) => _movies.TryGetValue(id, out var m) ? m : null;
            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList().AsReadOnly();
            public void Update(Movie movie) { _movies[movie.Id] = movie; }
            public void Remove(int id) { _movies.Remove(id); }
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) => new List<Movie>().AsReadOnly();
            public Movie GetRandom() => _movies.Values.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) => new List<Movie>().AsReadOnly();
        }

        private class TestRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();

            public void Add(Rental rental) { _rentals.Add(rental); }
            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals.AsReadOnly();
            public void Update(Rental rental) { }
            public void Remove(int id) { }
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) => new List<Rental>().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) => _rentals.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() => new List<Rental>().AsReadOnly();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) => new List<Rental>().AsReadOnly();
            public Rental ReturnRental(int rentalId) => null;
            public bool IsMovieRentedOut(int movieId) => false;
            public Rental Checkout(Rental rental) { Add(rental); return rental; }

            public Rental Checkout(Rental rental, int maxConcurrentRentals)
            {
                return Checkout(rental);
            }
            public RentalStats GetStats() => new RentalStats();
        }

        private class TestCustomerRepository : ICustomerRepository
        {
            private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();

            public void Add(Customer customer) { _customers[customer.Id] = customer; }
            public Customer GetById(int id) => _customers.TryGetValue(id, out var c) ? c : null;
            public IReadOnlyList<Customer> GetAll() => _customers.Values.ToList().AsReadOnly();
            public void Update(Customer customer) { _customers[customer.Id] = customer; }
            public void Remove(int id) { _customers.Remove(id); }
            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType) => _customers.Values.ToList().AsReadOnly();
            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) => new List<Customer>().AsReadOnly();
            public CustomerStats GetStats() => new CustomerStats { TotalCustomers = _customers.Count };
        }

        private Movie CreateMovie(int id, string name, Genre genre = Genre.Action, int? rating = 4)
        {
            return new Movie { Id = id, Name = name, Genre = genre, Rating = rating };
        }

        private Customer CreateCustomer(int id, string name, MembershipType tier = MembershipType.Basic)
        {
            return new Customer { Id = id, Name = name, MembershipType = tier, MemberSince = DateTime.Today.AddMonths(-6) };
        }

        private Rental CreateRental(int id, int customerId, int movieId,
            DateTime? rentalDate = null, DateTime? dueDate = null,
            DateTime? returnDate = null, RentalStatus status = RentalStatus.Active,
            decimal dailyRate = 2.99m, decimal lateFee = 0m)
        {
            var rDate = rentalDate ?? DateTime.Today.AddDays(-7);
            return new Rental
            {
                Id = id,
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = rDate,
                DueDate = dueDate ?? rDate.AddDays(7),
                ReturnDate = returnDate,
                Status = status,
                DailyRate = dailyRate,
                LateFee = lateFee,
            };
        }

        #endregion

        // ══════════════════════════════════════════════
        // Constructor Tests (3)
        // ══════════════════════════════════════════════

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepository_ThrowsArgumentNullException()
        {
            new MovieInsightsService(null, new TestRentalRepository(), new TestCustomerRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepository_ThrowsArgumentNullException()
        {
            new MovieInsightsService(new TestMovieRepository(), null, new TestCustomerRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepository_ThrowsArgumentNullException()
        {
            new MovieInsightsService(new TestMovieRepository(), new TestRentalRepository(), null);
        }

        // ══════════════════════════════════════════════
        // GetInsight Tests (5)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void GetInsight_NonExistentMovieId_ReturnsNull()
        {
            var movieRepo = new TestMovieRepository();
            var rentalRepo = new TestRentalRepository();
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.GetInsight(999);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetInsight_ExistingMovie_ReturnsCorrectMetadata()
        {
            var movieRepo = new TestMovieRepository();
            var movie = CreateMovie(1, "Test Movie", Genre.Comedy, 3);
            movie.ReleaseDate = new DateTime(2020, 5, 15);
            movieRepo.Add(movie);
            var rentalRepo = new TestRentalRepository();
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.GetInsight(1);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.MovieId);
            Assert.AreEqual("Test Movie", result.MovieName);
            Assert.AreEqual(Genre.Comedy, result.Genre);
            Assert.AreEqual(3, result.Rating);
            Assert.AreEqual(new DateTime(2020, 5, 15), result.ReleaseDate);
        }

        [TestMethod]
        public void GetInsight_MovieWithNoRentals_ReturnsEmptyRentalSummary()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "No Rentals Movie"));
            var rentalRepo = new TestRentalRepository();
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.GetInsight(1);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.RentalSummary.TotalRentals);
            Assert.AreEqual(0, result.RentalSummary.ActiveRentals);
            Assert.AreEqual(0, result.RentalSummary.UniqueCustomers);
        }

        [TestMethod]
        public void GetInsight_MovieWithRentals_ReturnsPopulatedRentalSummary()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Popular Movie"));
            var rentalRepo = new TestRentalRepository();
            var rDate = new DateTime(2025, 1, 10);
            rentalRepo.Add(CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(5), status: RentalStatus.Returned));
            rentalRepo.Add(CreateRental(2, 20, 1, rentalDate: rDate.AddDays(10), status: RentalStatus.Active));
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.GetInsight(1);

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.RentalSummary.TotalRentals);
            Assert.AreEqual(1, result.RentalSummary.ActiveRentals);
            Assert.AreEqual(1, result.RentalSummary.ReturnedRentals);
        }

        [TestMethod]
        public void GetInsight_ReturnsPerformanceScore()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Scored Movie", Genre.Action, 5));
            var rentalRepo = new TestRentalRepository();
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.GetInsight(1);

            Assert.IsNotNull(result.PerformanceScore);
            Assert.IsNotNull(result.PerformanceScore.Grade);
        }

        // ══════════════════════════════════════════════
        // BuildRentalSummary Tests (8)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void BuildRentalSummary_EmptyList_ReturnsZeroSummary()
        {
            var result = MovieInsightsService.BuildRentalSummary(new List<Rental>());

            Assert.AreEqual(0, result.TotalRentals);
            Assert.AreEqual(0, result.ActiveRentals);
            Assert.AreEqual(0, result.ReturnedRentals);
            Assert.AreEqual(0, result.OverdueRentals);
            Assert.AreEqual(0, result.UniqueCustomers);
            Assert.AreEqual(0, result.RepeatRenters);
            Assert.AreEqual(0, result.AverageRentalDays);
        }

        [TestMethod]
        public void BuildRentalSummary_NullList_ReturnsZeroSummary()
        {
            var result = MovieInsightsService.BuildRentalSummary(null);

            Assert.AreEqual(0, result.TotalRentals);
            Assert.AreEqual(0, result.ActiveRentals);
        }

        [TestMethod]
        public void BuildRentalSummary_CountsStatusesCorrectly()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, status: RentalStatus.Active),
                CreateRental(2, 20, 1, status: RentalStatus.Active),
                CreateRental(3, 30, 1, status: RentalStatus.Returned),
                CreateRental(4, 40, 1, status: RentalStatus.Overdue),
            };

            var result = MovieInsightsService.BuildRentalSummary(rentals);

            Assert.AreEqual(4, result.TotalRentals);
            Assert.AreEqual(2, result.ActiveRentals);
            Assert.AreEqual(1, result.ReturnedRentals);
            Assert.AreEqual(1, result.OverdueRentals);
        }

        [TestMethod]
        public void BuildRentalSummary_CalculatesUniqueCustomers()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1),
                CreateRental(2, 20, 1),
                CreateRental(3, 10, 1), // repeat customer
            };

            var result = MovieInsightsService.BuildRentalSummary(rentals);

            Assert.AreEqual(2, result.UniqueCustomers);
        }

        [TestMethod]
        public void BuildRentalSummary_CalculatesRepeatRenters()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1),
                CreateRental(2, 10, 1), // customer 10 rents again
                CreateRental(3, 20, 1),
                CreateRental(4, 30, 1),
                CreateRental(5, 30, 1), // customer 30 rents again
            };

            var result = MovieInsightsService.BuildRentalSummary(rentals);

            Assert.AreEqual(2, result.RepeatRenters); // customers 10 and 30
        }

        [TestMethod]
        public void BuildRentalSummary_CalculatesAverageRentalDays()
        {
            var rDate1 = new DateTime(2025, 1, 1);
            var rDate2 = new DateTime(2025, 2, 1);
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: rDate1, returnDate: rDate1.AddDays(5), status: RentalStatus.Returned),
                CreateRental(2, 20, 1, rentalDate: rDate2, returnDate: rDate2.AddDays(10), status: RentalStatus.Returned),
                CreateRental(3, 30, 1, status: RentalStatus.Active), // no return date, not counted
            };

            var result = MovieInsightsService.BuildRentalSummary(rentals);

            Assert.AreEqual(7.5, result.AverageRentalDays); // (5+10)/2
        }

        [TestMethod]
        public void BuildRentalSummary_SetsFirstAndLastRentalDates()
        {
            var date1 = new DateTime(2025, 1, 1);
            var date2 = new DateTime(2025, 3, 15);
            var date3 = new DateTime(2025, 6, 20);
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: date2),
                CreateRental(2, 20, 1, rentalDate: date1),
                CreateRental(3, 30, 1, rentalDate: date3),
            };

            var result = MovieInsightsService.BuildRentalSummary(rentals);

            Assert.AreEqual(date1, result.FirstRentalDate);
            Assert.AreEqual(date3, result.LastRentalDate);
        }

        [TestMethod]
        public void BuildRentalSummary_SingleRental_AllStatsCorrect()
        {
            var rDate = new DateTime(2025, 5, 1);
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(3), status: RentalStatus.Returned),
            };

            var result = MovieInsightsService.BuildRentalSummary(rentals);

            Assert.AreEqual(1, result.TotalRentals);
            Assert.AreEqual(0, result.ActiveRentals);
            Assert.AreEqual(1, result.ReturnedRentals);
            Assert.AreEqual(0, result.OverdueRentals);
            Assert.AreEqual(1, result.UniqueCustomers);
            Assert.AreEqual(0, result.RepeatRenters);
            Assert.AreEqual(3.0, result.AverageRentalDays);
            Assert.AreEqual(rDate, result.FirstRentalDate);
            Assert.AreEqual(rDate, result.LastRentalDate);
        }

        // ══════════════════════════════════════════════
        // BuildRevenue Tests (6)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void BuildRevenue_EmptyList_ReturnsZeroRevenue()
        {
            var result = MovieInsightsService.BuildRevenue(new List<Rental>());

            Assert.AreEqual(0m, result.TotalRevenue);
            Assert.AreEqual(0m, result.BaseRevenue);
            Assert.AreEqual(0m, result.LateFeeRevenue);
            Assert.AreEqual(0m, result.AverageRevenuePerRental);
            Assert.AreEqual(0, result.LateFeePercentage);
        }

        [TestMethod]
        public void BuildRevenue_SingleRental_CalculatesCorrectly()
        {
            // Rental: 5 days at $2/day, $3 late fee
            // TotalCost = (5 * 2) + 3 = 13
            var rDate = new DateTime(2025, 1, 1);
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(5),
                    status: RentalStatus.Returned, dailyRate: 2m, lateFee: 3m),
            };

            var result = MovieInsightsService.BuildRevenue(rentals);

            Assert.AreEqual(13m, result.TotalRevenue);
            Assert.AreEqual(10m, result.BaseRevenue);
            Assert.AreEqual(3m, result.LateFeeRevenue);
        }

        [TestMethod]
        public void BuildRevenue_MultipleRentals_SumsCorrectly()
        {
            var rDate1 = new DateTime(2025, 1, 1);
            var rDate2 = new DateTime(2025, 2, 1);
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: rDate1, returnDate: rDate1.AddDays(5),
                    status: RentalStatus.Returned, dailyRate: 2m, lateFee: 0m),
                CreateRental(2, 20, 1, rentalDate: rDate2, returnDate: rDate2.AddDays(3),
                    status: RentalStatus.Returned, dailyRate: 4m, lateFee: 2m),
            };

            var result = MovieInsightsService.BuildRevenue(rentals);

            // Rental 1: 5*2 + 0 = 10
            // Rental 2: 3*4 + 2 = 14
            Assert.AreEqual(24m, result.TotalRevenue);
            Assert.AreEqual(22m, result.BaseRevenue);
            Assert.AreEqual(2m, result.LateFeeRevenue);
        }

        [TestMethod]
        public void BuildRevenue_LateFeePercentageCalculation()
        {
            var rDate = new DateTime(2025, 1, 1);
            var rentals = new List<Rental>
            {
                // TotalCost = 10*1 + 10 = 20, lateFee = 10
                CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(10),
                    status: RentalStatus.Returned, dailyRate: 1m, lateFee: 10m),
            };

            var result = MovieInsightsService.BuildRevenue(rentals);

            // LateFeePercentage = (10/20)*100 = 50%
            Assert.AreEqual(50.0, result.LateFeePercentage);
        }

        [TestMethod]
        public void BuildRevenue_AverageRevenuePerRental()
        {
            var rDate1 = new DateTime(2025, 1, 1);
            var rDate2 = new DateTime(2025, 2, 1);
            var rentals = new List<Rental>
            {
                // TotalCost = 5*2 + 0 = 10
                CreateRental(1, 10, 1, rentalDate: rDate1, returnDate: rDate1.AddDays(5),
                    status: RentalStatus.Returned, dailyRate: 2m, lateFee: 0m),
                // TotalCost = 5*4 + 0 = 20
                CreateRental(2, 20, 1, rentalDate: rDate2, returnDate: rDate2.AddDays(5),
                    status: RentalStatus.Returned, dailyRate: 4m, lateFee: 0m),
            };

            var result = MovieInsightsService.BuildRevenue(rentals);

            Assert.AreEqual(15m, result.AverageRevenuePerRental); // 30 / 2
        }

        [TestMethod]
        public void BuildRevenue_NoLateFees_LateFeePercentageIsZero()
        {
            var rDate = new DateTime(2025, 1, 1);
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(3),
                    status: RentalStatus.Returned, dailyRate: 5m, lateFee: 0m),
            };

            var result = MovieInsightsService.BuildRevenue(rentals);

            Assert.AreEqual(0, result.LateFeePercentage);
            Assert.AreEqual(0m, result.LateFeeRevenue);
        }

        // ══════════════════════════════════════════════
        // BuildDemographics Tests (5)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void BuildDemographics_EmptyRentals_ReturnsEmptyDemographics()
        {
            var result = MovieInsightsService.BuildDemographics(
                new List<Rental>(), new Dictionary<int, Customer>());

            Assert.AreEqual(0, result.TotalUniqueCustomers);
            Assert.AreEqual(0, result.TierDistribution.Count);
            Assert.IsNull(result.DominantTier);
        }

        [TestMethod]
        public void BuildDemographics_CorrectlyCountsTierDistribution()
        {
            var customers = new Dictionary<int, Customer>
            {
                [10] = CreateCustomer(10, "Alice", MembershipType.Gold),
                [20] = CreateCustomer(20, "Bob", MembershipType.Basic),
                [30] = CreateCustomer(30, "Charlie", MembershipType.Gold),
            };
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1),
                CreateRental(2, 20, 1),
                CreateRental(3, 30, 1),
            };

            var result = MovieInsightsService.BuildDemographics(rentals, customers);

            Assert.AreEqual(2, result.TierDistribution["Gold"]);
            Assert.AreEqual(1, result.TierDistribution["Basic"]);
        }

        [TestMethod]
        public void BuildDemographics_IdentifiesDominantTier()
        {
            var customers = new Dictionary<int, Customer>
            {
                [10] = CreateCustomer(10, "Alice", MembershipType.Silver),
                [20] = CreateCustomer(20, "Bob", MembershipType.Silver),
                [30] = CreateCustomer(30, "Charlie", MembershipType.Gold),
            };
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1),
                CreateRental(2, 20, 1),
                CreateRental(3, 30, 1),
            };

            var result = MovieInsightsService.BuildDemographics(rentals, customers);

            Assert.AreEqual("Silver", result.DominantTier);
        }

        [TestMethod]
        public void BuildDemographics_OnlyCountsUniqueCustomers()
        {
            var customers = new Dictionary<int, Customer>
            {
                [10] = CreateCustomer(10, "Alice", MembershipType.Basic),
            };
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1),
                CreateRental(2, 10, 1), // same customer rents again
                CreateRental(3, 10, 1), // and again
            };

            var result = MovieInsightsService.BuildDemographics(rentals, customers);

            Assert.AreEqual(1, result.TotalUniqueCustomers);
            Assert.AreEqual(1, result.TierDistribution["Basic"]);
        }

        [TestMethod]
        public void BuildDemographics_UnknownCustomerIdSkippedGracefully()
        {
            var customers = new Dictionary<int, Customer>
            {
                [10] = CreateCustomer(10, "Alice", MembershipType.Gold),
            };
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1),
                CreateRental(2, 999, 1), // unknown customer
            };

            var result = MovieInsightsService.BuildDemographics(rentals, customers);

            // 2 unique IDs attempted, but only 1 found in lookup
            Assert.AreEqual(2, result.TotalUniqueCustomers);
            Assert.AreEqual(1, result.TierDistribution["Gold"]);
            Assert.AreEqual("Gold", result.DominantTier);
        }

        // ══════════════════════════════════════════════
        // BuildMonthlyTrend Tests (5)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void BuildMonthlyTrend_EmptyRentals_ReturnsEmptyList()
        {
            var result = MovieInsightsService.BuildMonthlyTrend(new List<Rental>());

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void BuildMonthlyTrend_GroupsByMonthCorrectly()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: new DateTime(2025, 1, 5), returnDate: new DateTime(2025, 1, 6), dailyRate: 1m),
                CreateRental(2, 20, 1, rentalDate: new DateTime(2025, 2, 10), returnDate: new DateTime(2025, 2, 11), dailyRate: 1m),
            };

            var result = MovieInsightsService.BuildMonthlyTrend(rentals);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("2025-01", result[0].Month);
            Assert.AreEqual("2025-02", result[1].Month);
        }

        [TestMethod]
        public void BuildMonthlyTrend_SortedChronologically()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: new DateTime(2025, 6, 1), returnDate: new DateTime(2025, 6, 2), dailyRate: 1m),
                CreateRental(2, 20, 1, rentalDate: new DateTime(2025, 1, 1), returnDate: new DateTime(2025, 1, 2), dailyRate: 1m),
                CreateRental(3, 30, 1, rentalDate: new DateTime(2025, 3, 1), returnDate: new DateTime(2025, 3, 2), dailyRate: 1m),
            };

            var result = MovieInsightsService.BuildMonthlyTrend(rentals);

            Assert.AreEqual("2025-01", result[0].Month);
            Assert.AreEqual("2025-03", result[1].Month);
            Assert.AreEqual("2025-06", result[2].Month);
        }

        [TestMethod]
        public void BuildMonthlyTrend_SumsRevenuePerMonth()
        {
            var rentals = new List<Rental>
            {
                // TotalCost for each: (1 day * 5) + 0 = 5
                CreateRental(1, 10, 1, rentalDate: new DateTime(2025, 3, 1), returnDate: new DateTime(2025, 3, 2), dailyRate: 5m, lateFee: 0m),
                CreateRental(2, 20, 1, rentalDate: new DateTime(2025, 3, 15), returnDate: new DateTime(2025, 3, 16), dailyRate: 5m, lateFee: 0m),
            };

            var result = MovieInsightsService.BuildMonthlyTrend(rentals);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(10m, result[0].Revenue); // 5 + 5
        }

        [TestMethod]
        public void BuildMonthlyTrend_MultipleRentalsInSameMonthAggregate()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: new DateTime(2025, 4, 1), returnDate: new DateTime(2025, 4, 2), dailyRate: 1m),
                CreateRental(2, 20, 1, rentalDate: new DateTime(2025, 4, 10), returnDate: new DateTime(2025, 4, 11), dailyRate: 1m),
                CreateRental(3, 30, 1, rentalDate: new DateTime(2025, 4, 20), returnDate: new DateTime(2025, 4, 21), dailyRate: 1m),
            };

            var result = MovieInsightsService.BuildMonthlyTrend(rentals);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3, result[0].RentalCount);
            Assert.AreEqual(2025, result[0].Year);
            Assert.AreEqual(4, result[0].MonthNumber);
        }

        // ══════════════════════════════════════════════
        // ComputePerformanceScore Tests (8)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void ComputePerformanceScore_NoRentals_PopularityIsZero()
        {
            var movie = CreateMovie(1, "Unpopular", rating: 3);
            var movieRentals = new List<Rental>();
            // Some other movie has rentals
            var allRentals = new List<Rental>
            {
                CreateRental(1, 10, 2, rentalDate: new DateTime(2025, 1, 1), returnDate: new DateTime(2025, 1, 2)),
            }.AsReadOnly();
            var allMovies = new List<Movie> { movie, CreateMovie(2, "Other") }.AsReadOnly();

            // Pre-compute global maximums (allRentals has 1 rental for movie 2)
            var result = MovieInsightsService.ComputePerformanceScore(movieRentals, movie, 1, allRentals[0].TotalCost);

            Assert.AreEqual(0, result.Popularity);
        }

        [TestMethod]
        public void ComputePerformanceScore_TopRentedMovie_PopularityIs100()
        {
            var movie = CreateMovie(1, "Most Popular", rating: 4);
            var rDate = new DateTime(2025, 1, 1);
            var movieRentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(1)),
                CreateRental(2, 20, 1, rentalDate: rDate.AddDays(5), returnDate: rDate.AddDays(6)),
                CreateRental(3, 30, 1, rentalDate: rDate.AddDays(10), returnDate: rDate.AddDays(11)),
            };
            // movie 2 has only 1 rental
            var otherRental = CreateRental(4, 40, 2, rentalDate: rDate.AddDays(2), returnDate: rDate.AddDays(3));
            var allRentals = movieRentals.Concat(new[] { otherRental }).ToList().AsReadOnly();
            var allMovies = new List<Movie> { movie, CreateMovie(2, "Other") }.AsReadOnly();

            // movie 1 has 3 rentals (top), so maxRentalCount=3
            decimal maxRev = 0;
            foreach (var r in allRentals) maxRev = Math.Max(maxRev, r.TotalCost);
            var rentalGroups = allRentals.GroupBy(r => r.MovieId);
            int maxCount = rentalGroups.Max(g => g.Count());
            decimal maxRevenue = rentalGroups.Max(g => g.Sum(r => r.TotalCost));
            var result = MovieInsightsService.ComputePerformanceScore(movieRentals, movie, maxCount, maxRevenue);

            Assert.AreEqual(100, result.Popularity);
        }

        [TestMethod]
        public void ComputePerformanceScore_RevenueRelativeToTop()
        {
            var movie = CreateMovie(1, "Mid Revenue", rating: 4);
            var rDate = new DateTime(2025, 1, 1);
            // Movie 1: 1 rental, TotalCost = 5*2 + 0 = 10
            var movieRentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(5), dailyRate: 2m, lateFee: 0m),
            };
            // Movie 2: 1 rental, TotalCost = 5*4 + 0 = 20 (top revenue)
            var otherRental = CreateRental(2, 20, 2, rentalDate: rDate, returnDate: rDate.AddDays(5), dailyRate: 4m, lateFee: 0m);
            var allRentals = movieRentals.Concat(new[] { otherRental }).ToList().AsReadOnly();
            var allMovies = new List<Movie> { movie, CreateMovie(2, "Other") }.AsReadOnly();

            // Compute from allRentals: movie2 has top revenue (20)
            var groups = allRentals.GroupBy(r => r.MovieId);
            int maxCnt = groups.Max(g => g.Count());
            decimal topRev = groups.Max(g => g.Sum(r => r.TotalCost));
            var result = MovieInsightsService.ComputePerformanceScore(movieRentals, movie, maxCnt, topRev);

            Assert.AreEqual(50, result.Revenue); // 10/20 * 100
        }

        [TestMethod]
        public void ComputePerformanceScore_RetentionWithRepeatRenters()
        {
            var movie = CreateMovie(1, "Repeat Movie", rating: 4);
            var rDate = new DateTime(2025, 1, 1);
            // Customer 10 rents twice, customer 20 rents once → retention = 1/2 * 100 = 50
            var movieRentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(1)),
                CreateRental(2, 10, 1, rentalDate: rDate.AddDays(5), returnDate: rDate.AddDays(6)),
                CreateRental(3, 20, 1, rentalDate: rDate.AddDays(10), returnDate: rDate.AddDays(11)),
            };
            var allRentals = movieRentals.ToList().AsReadOnly();
            var allMovies = new List<Movie> { movie }.AsReadOnly();

            // Only one movie, so it has max rentals and max revenue
            decimal topRevenue = movieRentals.Sum(r => r.TotalCost);
            var result = MovieInsightsService.ComputePerformanceScore(movieRentals, movie, movieRentals.Count, topRevenue);

            Assert.AreEqual(50, result.Retention);
        }

        [TestMethod]
        public void ComputePerformanceScore_RatingScaledCorrectly()
        {
            var movie = CreateMovie(1, "Mid Rating", rating: 3);
            var movieRentals = new List<Rental>();

            var result = MovieInsightsService.ComputePerformanceScore(movieRentals, movie, 0, 0m);

            Assert.AreEqual(60, result.Rating); // 3 * 20
        }

        [TestMethod]
        public void ComputePerformanceScore_NullRatingDefaultsTo50()
        {
            var movie = CreateMovie(1, "No Rating", rating: null);
            var movieRentals = new List<Rental>();

            var result = MovieInsightsService.ComputePerformanceScore(movieRentals, movie, 0, 0m);

            Assert.AreEqual(50, result.Rating);
        }

        [TestMethod]
        public void ComputePerformanceScore_GradeBoundaries()
        {
            // A movie with rating 5 and no rentals: ratingScore = 100
            // popularity=0, revenue=0, retention=0, rating=100
            // overall = 0*0.35 + 0*0.30 + 0*0.20 + 100*0.15 = 15 → "F"
            var movieF = CreateMovie(1, "F Movie", rating: 5);
            var resultF = MovieInsightsService.ComputePerformanceScore(
                new List<Rental>(), movieF, 0, 0m);
            Assert.AreEqual("F", resultF.Grade);

            // Verify GetGrade directly for boundaries
            Assert.AreEqual("A", MovieInsightsService.GetGrade(95));
            Assert.AreEqual("B", MovieInsightsService.GetGrade(85));
            Assert.AreEqual("C", MovieInsightsService.GetGrade(75));
            Assert.AreEqual("D", MovieInsightsService.GetGrade(65));
            Assert.AreEqual("F", MovieInsightsService.GetGrade(50));
        }

        [TestMethod]
        public void ComputePerformanceScore_OverallIsWeightedAverage()
        {
            // Movie is the only movie and only one with rentals
            // popularity = 100 (1/1), revenue = 100 (top), retention = 0 (1 customer, no repeat), rating = 80 (4*20)
            var movie = CreateMovie(1, "Weighted", rating: 4);
            var rDate = new DateTime(2025, 1, 1);
            var movieRentals = new List<Rental>
            {
                CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(5)),
            };
            var allRentals = movieRentals.ToList().AsReadOnly();
            var allMovies = new List<Movie> { movie }.AsReadOnly();

            // Only one movie, so it has max rentals and revenue
            decimal topRev = movieRentals.Sum(r => r.TotalCost);
            var result = MovieInsightsService.ComputePerformanceScore(movieRentals, movie, movieRentals.Count, topRev);

            // 100*0.35 + 100*0.30 + 0*0.20 + 80*0.15 = 35 + 30 + 0 + 12 = 77
            Assert.AreEqual(77, result.Overall);
            Assert.AreEqual("C", result.Grade);
        }

        // ══════════════════════════════════════════════
        // GetAllInsights Tests (3)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void GetAllInsights_ReturnsInsightsForAllMovies()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Movie A"));
            movieRepo.Add(CreateMovie(2, "Movie B"));
            movieRepo.Add(CreateMovie(3, "Movie C"));
            var rentalRepo = new TestRentalRepository();
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.GetAllInsights();

            Assert.AreEqual(3, result.Count);
        }

        [TestMethod]
        public void GetAllInsights_SortedByPerformanceScoreDescending()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Low Rated", rating: 1));
            movieRepo.Add(CreateMovie(2, "High Rated", rating: 5));
            movieRepo.Add(CreateMovie(3, "Mid Rated", rating: 3));
            var rentalRepo = new TestRentalRepository();
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.GetAllInsights();

            // With no rentals, only rating matters: 5>3>1
            Assert.IsTrue(result[0].PerformanceScore.Overall >= result[1].PerformanceScore.Overall);
            Assert.IsTrue(result[1].PerformanceScore.Overall >= result[2].PerformanceScore.Overall);
            Assert.AreEqual("High Rated", result[0].MovieName);
            Assert.AreEqual("Low Rated", result[2].MovieName);
        }

        [TestMethod]
        public void GetAllInsights_IncludesMoviesWithNoRentals()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Has Rentals"));
            movieRepo.Add(CreateMovie(2, "No Rentals"));
            var rentalRepo = new TestRentalRepository();
            var rDate = new DateTime(2025, 1, 1);
            rentalRepo.Add(CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(1)));
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.GetAllInsights();

            Assert.AreEqual(2, result.Count);
            var noRentals = result.First(i => i.MovieName == "No Rentals");
            Assert.AreEqual(0, noRentals.RentalSummary.TotalRentals);
        }

        // ══════════════════════════════════════════════
        // Compare Tests (5)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void Compare_NonExistentMovie_ReturnsNull()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Exists"));
            var rentalRepo = new TestRentalRepository();
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.Compare(1, 999);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void Compare_CorrectlyIdentifiesRevenueWinner()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Cheap", rating: 3));
            movieRepo.Add(CreateMovie(2, "Expensive", rating: 3));
            var rentalRepo = new TestRentalRepository();
            var rDate = new DateTime(2025, 1, 1);
            // Movie 1: TotalCost = 5*1 + 0 = 5
            rentalRepo.Add(CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(5), dailyRate: 1m));
            // Movie 2: TotalCost = 5*3 + 0 = 15
            rentalRepo.Add(CreateRental(2, 20, 2, rentalDate: rDate, returnDate: rDate.AddDays(5), dailyRate: 3m));
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.Compare(1, 2);

            Assert.IsNotNull(result);
            Assert.AreEqual("Expensive", result.RevenueWinner);
        }

        [TestMethod]
        public void Compare_CorrectlyIdentifiesPopularityWinner()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Popular", rating: 3));
            movieRepo.Add(CreateMovie(2, "Less Popular", rating: 3));
            var rentalRepo = new TestRentalRepository();
            var rDate = new DateTime(2025, 1, 1);
            rentalRepo.Add(CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(1)));
            rentalRepo.Add(CreateRental(2, 20, 1, rentalDate: rDate.AddDays(2), returnDate: rDate.AddDays(3)));
            rentalRepo.Add(CreateRental(3, 30, 1, rentalDate: rDate.AddDays(4), returnDate: rDate.AddDays(5)));
            rentalRepo.Add(CreateRental(4, 40, 2, rentalDate: rDate, returnDate: rDate.AddDays(1)));
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.Compare(1, 2);

            Assert.AreEqual("Popular", result.PopularityWinner);
        }

        [TestMethod]
        public void Compare_GeneratesCorrectVerdictWhenAWins()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Winner", rating: 5));
            movieRepo.Add(CreateMovie(2, "Loser", rating: 1));
            var rentalRepo = new TestRentalRepository();
            var rDate = new DateTime(2025, 1, 1);
            // Movie 1 has more rentals and more revenue
            rentalRepo.Add(CreateRental(1, 10, 1, rentalDate: rDate, returnDate: rDate.AddDays(5), dailyRate: 10m));
            rentalRepo.Add(CreateRental(2, 20, 1, rentalDate: rDate.AddDays(6), returnDate: rDate.AddDays(10), dailyRate: 10m));
            // Movie 2 has 1 small rental
            rentalRepo.Add(CreateRental(3, 30, 2, rentalDate: rDate, returnDate: rDate.AddDays(1), dailyRate: 1m));
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.Compare(1, 2);

            Assert.IsTrue(result.OverallVerdict.Contains("Winner outperforms Loser"));
        }

        [TestMethod]
        public void Compare_TieProducesPerformSimilarlyVerdict()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Alpha", rating: 3));
            movieRepo.Add(CreateMovie(2, "Beta", rating: 3));
            var rentalRepo = new TestRentalRepository();
            // No rentals → everything is tied
            var customerRepo = new TestCustomerRepository();
            var service = new MovieInsightsService(movieRepo, rentalRepo, customerRepo);

            var result = service.Compare(1, 2);

            Assert.IsTrue(result.OverallVerdict.Contains("perform similarly"));
        }

        // ══════════════════════════════════════════════
        // GetGrade Tests (5)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void GetGrade_95_ReturnsA()
        {
            Assert.AreEqual("A", MovieInsightsService.GetGrade(95));
        }

        [TestMethod]
        public void GetGrade_85_ReturnsB()
        {
            Assert.AreEqual("B", MovieInsightsService.GetGrade(85));
        }

        [TestMethod]
        public void GetGrade_75_ReturnsC()
        {
            Assert.AreEqual("C", MovieInsightsService.GetGrade(75));
        }

        [TestMethod]
        public void GetGrade_65_ReturnsD()
        {
            Assert.AreEqual("D", MovieInsightsService.GetGrade(65));
        }

        [TestMethod]
        public void GetGrade_50_ReturnsF()
        {
            Assert.AreEqual("F", MovieInsightsService.GetGrade(50));
        }

        // ══════════════════════════════════════════════
        // GetComparisonVerdict Tests (2)
        // ══════════════════════════════════════════════

        [TestMethod]
        public void GetComparisonVerdict_AWins2Of3_ReturnsAOutperformsB()
        {
            var a = new MovieInsight
            {
                MovieName = "Alpha",
                Revenue = new RevenueBreakdown { TotalRevenue = 100 },
                RentalSummary = new RentalSummary { TotalRentals = 10 },
                PerformanceScore = new PerformanceScore { Overall = 80 },
            };
            var b = new MovieInsight
            {
                MovieName = "Beta",
                Revenue = new RevenueBreakdown { TotalRevenue = 50 },
                RentalSummary = new RentalSummary { TotalRentals = 5 },
                PerformanceScore = new PerformanceScore { Overall = 90 }, // B wins this one
            };

            var result = MovieInsightsService.GetComparisonVerdict(a, b);

            Assert.AreEqual("Alpha outperforms Beta overall.", result);
        }

        [TestMethod]
        public void GetComparisonVerdict_Equal_ReturnsPerformSimilarly()
        {
            var a = new MovieInsight
            {
                MovieName = "Alpha",
                Revenue = new RevenueBreakdown { TotalRevenue = 50 },
                RentalSummary = new RentalSummary { TotalRentals = 5 },
                PerformanceScore = new PerformanceScore { Overall = 70 },
            };
            var b = new MovieInsight
            {
                MovieName = "Beta",
                Revenue = new RevenueBreakdown { TotalRevenue = 50 },
                RentalSummary = new RentalSummary { TotalRentals = 5 },
                PerformanceScore = new PerformanceScore { Overall = 70 },
            };

            var result = MovieInsightsService.GetComparisonVerdict(a, b);

            Assert.AreEqual("Alpha and Beta perform similarly.", result);
        }
    }
}
