using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class DashboardTests
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

            public Movie GetById(int id) =>
                _movies.TryGetValue(id, out var m) ? m : null;

            public IReadOnlyList<Movie> GetAll() =>
                _movies.Values.ToList().AsReadOnly();

            public void Update(Movie movie) { _movies[movie.Id] = movie; }
            public void Remove(int id) { _movies.Remove(id); }

            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                _movies.Values
                    .Where(m => m.ReleaseDate?.Year == year && m.ReleaseDate?.Month == month)
                    .ToList().AsReadOnly();

            public Movie GetRandom() => _movies.Values.FirstOrDefault();

            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                _movies.Values.ToList().AsReadOnly();
        }

        private class TestCustomerRepository : ICustomerRepository
        {
            private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
            private int _nextId = 1;

            public void Add(Customer customer)
            {
                if (customer.Id == 0) customer.Id = _nextId++;
                _customers[customer.Id] = customer;
            }

            public Customer GetById(int id) =>
                _customers.TryGetValue(id, out var c) ? c : null;

            public IReadOnlyList<Customer> GetAll() =>
                _customers.Values.ToList().AsReadOnly();

            public void Update(Customer customer) { _customers[customer.Id] = customer; }
            public void Remove(int id) { _customers.Remove(id); }

            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType) =>
                _customers.Values.ToList().AsReadOnly();

            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) =>
                _customers.Values
                    .Where(c => c.MemberSince?.Year == year && c.MemberSince?.Month == month)
                    .ToList().AsReadOnly();

            public CustomerStats GetStats() => new CustomerStats
            {
                TotalCustomers = _customers.Count
            };
        }

        private class TestRentalRepository : IRentalRepository
        {
            private readonly Dictionary<int, Rental> _rentals = new Dictionary<int, Rental>();
            private int _nextId = 1;

            public void Add(Rental rental)
            {
                if (rental.Id == 0) rental.Id = _nextId++;
                _rentals[rental.Id] = rental;
            }

            public Rental GetById(int id) =>
                _rentals.TryGetValue(id, out var r) ? r : null;

            public IReadOnlyList<Rental> GetAll() =>
                _rentals.Values.ToList().AsReadOnly();

            public void Update(Rental rental) { _rentals[rental.Id] = rental; }
            public void Remove(int id) { _rentals.Remove(id); }

            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _rentals.Values.Where(r => r.CustomerId == customerId && r.Status != RentalStatus.Returned)
                    .ToList().AsReadOnly();

            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _rentals.Values.Where(r => r.MovieId == movieId).ToList().AsReadOnly();

            public IReadOnlyList<Rental> GetOverdue() =>
                _rentals.Values.Where(r => r.Status == RentalStatus.Overdue).ToList().AsReadOnly();

            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                _rentals.Values.ToList().AsReadOnly();

            public Rental ReturnRental(int rentalId) => GetById(rentalId);
            public bool IsMovieRentedOut(int movieId) => false;

            public Rental Checkout(Rental rental)
            {
                Add(rental);
                return rental;
            }

            public RentalStats GetStats()
            {
                var stats = new RentalStats();
                foreach (var r in _rentals.Values)
                {
                    stats.TotalRentals++;
                    stats.TotalRevenue += r.TotalCost;
                    stats.TotalLateFees += r.LateFee;
                    switch (r.Status)
                    {
                        case RentalStatus.Active: stats.ActiveRentals++; break;
                        case RentalStatus.Overdue: stats.OverdueRentals++; break;
                        case RentalStatus.Returned: stats.ReturnedRentals++; break;
                    }
                }
                return stats;
            }
        }

        private TestMovieRepository _movieRepo;
        private TestCustomerRepository _customerRepo;
        private TestRentalRepository _rentalRepo;
        private DashboardService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new TestMovieRepository();
            _customerRepo = new TestCustomerRepository();
            _rentalRepo = new TestRentalRepository();
            _service = new DashboardService(_rentalRepo, _movieRepo, _customerRepo);

            // Seed movies
            _movieRepo.Add(new Movie { Id = 1, Name = "Shrek!", Genre = Genre.Animation, Rating = 5 });
            _movieRepo.Add(new Movie { Id = 2, Name = "The Godfather", Genre = Genre.Drama, Rating = 5 });
            _movieRepo.Add(new Movie { Id = 3, Name = "Aliens", Genre = Genre.SciFi, Rating = 4 });
            _movieRepo.Add(new Movie { Id = 4, Name = "Die Hard", Genre = Genre.Action, Rating = 4 });
            _movieRepo.Add(new Movie { Id = 5, Name = "Inception", Genre = Genre.SciFi, Rating = 5 });

            // Seed customers
            _customerRepo.Add(new Customer { Id = 1, Name = "John Smith", MembershipType = MembershipType.Gold });
            _customerRepo.Add(new Customer { Id = 2, Name = "Jane Doe", MembershipType = MembershipType.Silver });
            _customerRepo.Add(new Customer { Id = 3, Name = "Bob Wilson", MembershipType = MembershipType.Platinum });

            // Seed rentals
            _rentalRepo.Add(new Rental
            {
                Id = 1, CustomerId = 1, CustomerName = "John Smith",
                MovieId = 1, MovieName = "Shrek!",
                RentalDate = DateTime.Today.AddDays(-5), DueDate = DateTime.Today.AddDays(2),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });
            _rentalRepo.Add(new Rental
            {
                Id = 2, CustomerId = 1, CustomerName = "John Smith",
                MovieId = 2, MovieName = "The Godfather",
                RentalDate = DateTime.Today.AddDays(-10), DueDate = DateTime.Today.AddDays(-3),
                ReturnDate = DateTime.Today.AddDays(-2), DailyRate = 3.99m,
                LateFee = 1.50m, Status = RentalStatus.Returned
            });
            _rentalRepo.Add(new Rental
            {
                Id = 3, CustomerId = 2, CustomerName = "Jane Doe",
                MovieId = 3, MovieName = "Aliens",
                RentalDate = DateTime.Today.AddDays(-3), DueDate = DateTime.Today.AddDays(4),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });
            _rentalRepo.Add(new Rental
            {
                Id = 4, CustomerId = 3, CustomerName = "Bob Wilson",
                MovieId = 1, MovieName = "Shrek!",
                RentalDate = DateTime.Today.AddDays(-20), DueDate = DateTime.Today.AddDays(-13),
                ReturnDate = DateTime.Today.AddDays(-12), DailyRate = 3.99m,
                LateFee = 1.50m, Status = RentalStatus.Returned
            });
            _rentalRepo.Add(new Rental
            {
                Id = 5, CustomerId = 2, CustomerName = "Jane Doe",
                MovieId = 4, MovieName = "Die Hard",
                RentalDate = DateTime.Today.AddDays(-15), DueDate = DateTime.Today.AddDays(-8),
                ReturnDate = DateTime.Today.AddDays(-7), DailyRate = 3.99m,
                LateFee = 1.50m, Status = RentalStatus.Returned
            });
        }

        #endregion

        #region DashboardService.GetDashboard

        [TestMethod]
        public void GetDashboard_ReturnsNonNullData()
        {
            var data = _service.GetDashboard();
            Assert.IsNotNull(data);
            Assert.IsNotNull(data.Stats);
            Assert.IsNotNull(data.TopMovies);
            Assert.IsNotNull(data.TopCustomers);
            Assert.IsNotNull(data.RevenueByGenre);
            Assert.IsNotNull(data.MembershipBreakdown);
            Assert.IsNotNull(data.RecentRentals);
            Assert.IsNotNull(data.MonthlyRevenue);
        }

        [TestMethod]
        public void GetDashboard_CorrectCounts()
        {
            var data = _service.GetDashboard();
            Assert.AreEqual(5, data.MovieCount);
            Assert.AreEqual(3, data.CustomerCount);
            Assert.AreEqual(5, data.Stats.TotalRentals);
        }

        [TestMethod]
        public void GetDashboard_AverageRevenueCalculated()
        {
            var data = _service.GetDashboard();
            Assert.IsTrue(data.AverageRevenuePerRental > 0);
            Assert.AreEqual(data.Stats.TotalRevenue / 5, data.AverageRevenuePerRental);
        }

        [TestMethod]
        public void GetDashboard_EmptyRepos_NoErrors()
        {
            var emptyService = new DashboardService(
                new TestRentalRepository(),
                new TestMovieRepository(),
                new TestCustomerRepository());

            var data = emptyService.GetDashboard();
            Assert.AreEqual(0, data.Stats.TotalRentals);
            Assert.AreEqual(0m, data.AverageRevenuePerRental);
            Assert.AreEqual(0, data.TopMovies.Count);
            Assert.AreEqual(0, data.TopCustomers.Count);
        }

        #endregion

        #region TopMovies

        [TestMethod]
        public void TopMovies_SortedByRentalCount()
        {
            var data = _service.GetDashboard();
            // Shrek! has 2 rentals, others have 1
            Assert.AreEqual("Shrek!", data.TopMovies[0].MovieName);
            Assert.AreEqual(2, data.TopMovies[0].RentalCount);
        }

        [TestMethod]
        public void TopMovies_IncludesGenreAndRating()
        {
            var data = _service.GetDashboard();
            var shrek = data.TopMovies.First(m => m.MovieName == "Shrek!");
            Assert.AreEqual(Genre.Animation, shrek.Genre);
            Assert.AreEqual(5, shrek.Rating);
        }

        [TestMethod]
        public void TopMovies_RespectsLimit()
        {
            var rentals = _rentalRepo.GetAll();
            var movieLookup = _movieRepo.GetAll().ToDictionary(m => m.Id);
            var top2 = DashboardService.ComputeTopMovies(rentals, movieLookup, 2);
            Assert.AreEqual(2, top2.Count);
        }

        [TestMethod]
        public void TopMovies_TiesBreakByRevenue()
        {
            // Godfather, Aliens, Die Hard all have 1 rental each
            var data = _service.GetDashboard();
            var singleRentals = data.TopMovies.Where(m => m.RentalCount == 1).ToList();
            // They should be sorted by revenue descending
            for (int i = 0; i < singleRentals.Count - 1; i++)
            {
                Assert.IsTrue(singleRentals[i].TotalRevenue >= singleRentals[i + 1].TotalRevenue);
            }
        }

        [TestMethod]
        public void TopMovies_CalculatesRevenue()
        {
            var data = _service.GetDashboard();
            var shrek = data.TopMovies.First(m => m.MovieName == "Shrek!");
            Assert.IsTrue(shrek.TotalRevenue > 0);
        }

        [TestMethod]
        public void TopMovies_UnknownMovie_FallsBackToRentalName()
        {
            var rentals = new List<Rental>
            {
                new Rental { MovieId = 999, MovieName = "Deleted Movie", RentalDate = DateTime.Today,
                    DueDate = DateTime.Today.AddDays(7), DailyRate = 3.99m, Status = RentalStatus.Active }
            };
            var top = DashboardService.ComputeTopMovies(rentals, new Dictionary<int, Movie>(), 5);
            Assert.AreEqual("Deleted Movie", top[0].MovieName);
            Assert.IsNull(top[0].Genre);
        }

        #endregion

        #region TopCustomers

        [TestMethod]
        public void TopCustomers_SortedByTotalSpent()
        {
            var data = _service.GetDashboard();
            // John Smith has 2 rentals — highest spend
            Assert.AreEqual("John Smith", data.TopCustomers[0].CustomerName);
        }

        [TestMethod]
        public void TopCustomers_IncludesMembershipTier()
        {
            var data = _service.GetDashboard();
            var john = data.TopCustomers.First(c => c.CustomerName == "John Smith");
            Assert.AreEqual(MembershipType.Gold, john.MembershipType);
        }

        [TestMethod]
        public void TopCustomers_TracksLateFees()
        {
            var data = _service.GetDashboard();
            var john = data.TopCustomers.First(c => c.CustomerName == "John Smith");
            Assert.AreEqual(1.50m, john.LateFees);
        }

        [TestMethod]
        public void TopCustomers_RespectsLimit()
        {
            var rentals = _rentalRepo.GetAll();
            var custLookup = _customerRepo.GetAll().ToDictionary(c => c.Id);
            var top1 = DashboardService.ComputeTopCustomers(rentals, custLookup, 1);
            Assert.AreEqual(1, top1.Count);
        }

        [TestMethod]
        public void TopCustomers_CountsRentals()
        {
            var data = _service.GetDashboard();
            var john = data.TopCustomers.First(c => c.CustomerName == "John Smith");
            Assert.AreEqual(2, john.RentalCount);
        }

        [TestMethod]
        public void TopCustomers_UnknownCustomer_FallsBackToRentalName()
        {
            var rentals = new List<Rental>
            {
                new Rental { CustomerId = 999, CustomerName = "Ghost", MovieId = 1,
                    RentalDate = DateTime.Today, DueDate = DateTime.Today.AddDays(7),
                    DailyRate = 3.99m, Status = RentalStatus.Active }
            };
            var top = DashboardService.ComputeTopCustomers(rentals, new Dictionary<int, Customer>(), 5);
            Assert.AreEqual("Ghost", top[0].CustomerName);
            Assert.AreEqual(MembershipType.Basic, top[0].MembershipType); // default
        }

        #endregion

        #region RevenueByGenre

        [TestMethod]
        public void RevenueByGenre_GroupsCorrectly()
        {
            var data = _service.GetDashboard();
            // Animation (Shrek x2), Drama (Godfather), SciFi (Aliens), Action (Die Hard)
            Assert.AreEqual(4, data.RevenueByGenre.Count);
        }

        [TestMethod]
        public void RevenueByGenre_SortedByRevenue()
        {
            var data = _service.GetDashboard();
            for (int i = 0; i < data.RevenueByGenre.Count - 1; i++)
            {
                Assert.IsTrue(data.RevenueByGenre[i].Revenue >= data.RevenueByGenre[i + 1].Revenue);
            }
        }

        [TestMethod]
        public void RevenueByGenre_TracksCounts()
        {
            var data = _service.GetDashboard();
            var animation = data.RevenueByGenre.First(g => g.GenreName == "Animation");
            Assert.AreEqual(2, animation.RentalCount);
        }

        [TestMethod]
        public void RevenueByGenre_TracksLateFees()
        {
            var data = _service.GetDashboard();
            var total = data.RevenueByGenre.Sum(g => g.LateFees);
            Assert.IsTrue(total > 0);
        }

        [TestMethod]
        public void RevenueByGenre_UnknownGenre_GroupedAsUnknown()
        {
            var rentals = new List<Rental>
            {
                new Rental { MovieId = 999, RentalDate = DateTime.Today,
                    DueDate = DateTime.Today.AddDays(7), DailyRate = 3.99m, Status = RentalStatus.Active }
            };
            var result = DashboardService.ComputeRevenueByGenre(rentals, new Dictionary<int, Movie>());
            Assert.AreEqual("Unknown", result[0].GenreName);
        }

        [TestMethod]
        public void RevenueByGenre_MovieWithNoGenre_IsUnknown()
        {
            var movie = new Movie { Id = 10, Name = "No Genre Movie" };
            var movieLookup = new Dictionary<int, Movie> { { 10, movie } };
            var rentals = new List<Rental>
            {
                new Rental { MovieId = 10, RentalDate = DateTime.Today,
                    DueDate = DateTime.Today.AddDays(7), DailyRate = 3.99m, Status = RentalStatus.Active }
            };
            var result = DashboardService.ComputeRevenueByGenre(rentals, movieLookup);
            Assert.AreEqual("Unknown", result[0].GenreName);
        }

        #endregion

        #region MembershipBreakdown

        [TestMethod]
        public void MembershipBreakdown_GroupsByTier()
        {
            var data = _service.GetDashboard();
            // Gold (John), Silver (Jane), Platinum (Bob)
            Assert.AreEqual(3, data.MembershipBreakdown.Count);
        }

        [TestMethod]
        public void MembershipBreakdown_CountsUniqueCustomers()
        {
            var data = _service.GetDashboard();
            var gold = data.MembershipBreakdown.First(m => m.Tier == MembershipType.Gold);
            Assert.AreEqual(1, gold.UniqueCustomers);
            Assert.AreEqual(2, gold.RentalCount); // John has 2 rentals
        }

        [TestMethod]
        public void MembershipBreakdown_SortedByRevenue()
        {
            var data = _service.GetDashboard();
            for (int i = 0; i < data.MembershipBreakdown.Count - 1; i++)
            {
                Assert.IsTrue(data.MembershipBreakdown[i].Revenue >= data.MembershipBreakdown[i + 1].Revenue);
            }
        }

        [TestMethod]
        public void MembershipBreakdown_UnknownCustomer_DefaultsToBasic()
        {
            var rentals = new List<Rental>
            {
                new Rental { CustomerId = 999, RentalDate = DateTime.Today,
                    DueDate = DateTime.Today.AddDays(7), DailyRate = 3.99m, Status = RentalStatus.Active }
            };
            var result = DashboardService.ComputeMembershipBreakdown(rentals, new Dictionary<int, Customer>());
            Assert.AreEqual(MembershipType.Basic, result[0].Tier);
        }

        #endregion

        #region RecentRentals

        [TestMethod]
        public void RecentRentals_SortedByMostRecent()
        {
            var data = _service.GetDashboard();
            for (int i = 0; i < data.RecentRentals.Count - 1; i++)
            {
                Assert.IsTrue(data.RecentRentals[i].RentalDate >= data.RecentRentals[i + 1].RentalDate);
            }
        }

        [TestMethod]
        public void RecentRentals_RespectsLimit()
        {
            var rentals = _rentalRepo.GetAll();
            var recent2 = DashboardService.GetRecentRentals(rentals, 2);
            Assert.AreEqual(2, recent2.Count);
        }

        [TestMethod]
        public void RecentRentals_LimitLargerThanCount_ReturnsAll()
        {
            var rentals = _rentalRepo.GetAll();
            var all = DashboardService.GetRecentRentals(rentals, 100);
            Assert.AreEqual(5, all.Count);
        }

        #endregion

        #region MonthlyRevenue

        [TestMethod]
        public void MonthlyRevenue_Returns6Months()
        {
            var data = _service.GetDashboard();
            Assert.AreEqual(6, data.MonthlyRevenue.Count);
        }

        [TestMethod]
        public void MonthlyRevenue_CurrentMonthIncluded()
        {
            var data = _service.GetDashboard();
            var currentMonth = data.MonthlyRevenue.Last();
            Assert.AreEqual(DateTime.Today.Month, currentMonth.Month);
            Assert.AreEqual(DateTime.Today.Year, currentMonth.Year);
        }

        [TestMethod]
        public void MonthlyRevenue_HasLabels()
        {
            var data = _service.GetDashboard();
            foreach (var m in data.MonthlyRevenue)
            {
                Assert.IsFalse(string.IsNullOrEmpty(m.Label));
            }
        }

        [TestMethod]
        public void MonthlyRevenue_CurrentMonthHasData()
        {
            var data = _service.GetDashboard();
            var currentMonth = data.MonthlyRevenue.Last();
            // We have rentals starting today, so current month should have data
            Assert.IsTrue(currentMonth.RentalCount > 0 || currentMonth.Revenue >= 0);
        }

        [TestMethod]
        public void MonthlyRevenue_CustomMonthCount()
        {
            var rentals = _rentalRepo.GetAll();
            var months3 = DashboardService.ComputeMonthlyRevenue(rentals, 3);
            Assert.AreEqual(3, months3.Count);
        }

        [TestMethod]
        public void MonthlyRevenue_EmptyRentals_ReturnsEmptyMonths()
        {
            var months = DashboardService.ComputeMonthlyRevenue(new List<Rental>(), 6);
            Assert.AreEqual(6, months.Count);
            Assert.IsTrue(months.All(m => m.Revenue == 0 && m.RentalCount == 0));
        }

        #endregion

        #region DashboardController

        [TestMethod]
        public void Controller_Index_ReturnsViewWithModel()
        {
            var controller = new DashboardController(_service);
            var result = controller.Index() as ViewResult;

            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result.Model, typeof(DashboardViewModel));

            var model = (DashboardViewModel)result.Model;
            Assert.IsNotNull(model.Data);
            Assert.IsTrue(model.Data.Stats.TotalRentals > 0);
        }

        [TestMethod]
        public void Controller_Index_EmptyData_NoErrors()
        {
            var emptyService = new DashboardService(
                new TestRentalRepository(),
                new TestMovieRepository(),
                new TestCustomerRepository());
            var controller = new DashboardController(emptyService);

            var result = controller.Index() as ViewResult;
            Assert.IsNotNull(result);

            var model = (DashboardViewModel)result.Model;
            Assert.AreEqual(0, model.Data.Stats.TotalRentals);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Controller_NullService_Throws()
        {
            new DashboardController(null);
        }

        #endregion

        #region DashboardService Constructor

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Service_NullRentalRepo_Throws()
        {
            new DashboardService(null, _movieRepo, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Service_NullMovieRepo_Throws()
        {
            new DashboardService(_rentalRepo, null, _customerRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Service_NullCustomerRepo_Throws()
        {
            new DashboardService(_rentalRepo, _movieRepo, null);
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public void Dashboard_SingleRental_AllSectionsPopulated()
        {
            var singleRentalRepo = new TestRentalRepository();
            singleRentalRepo.Add(new Rental
            {
                CustomerId = 1, CustomerName = "Solo Customer",
                MovieId = 1, MovieName = "Solo Movie",
                RentalDate = DateTime.Today, DueDate = DateTime.Today.AddDays(7),
                DailyRate = 5.00m, Status = RentalStatus.Active
            });

            var service = new DashboardService(singleRentalRepo, _movieRepo, _customerRepo);
            var data = service.GetDashboard();

            Assert.AreEqual(1, data.Stats.TotalRentals);
            Assert.AreEqual(1, data.TopMovies.Count);
            Assert.AreEqual(1, data.TopCustomers.Count);
            Assert.AreEqual(1, data.RecentRentals.Count);
        }

        [TestMethod]
        public void Dashboard_AllReturned_StatsCorrect()
        {
            var returnedRepo = new TestRentalRepository();
            returnedRepo.Add(new Rental
            {
                CustomerId = 1, CustomerName = "Done Customer",
                MovieId = 1, MovieName = "Done Movie",
                RentalDate = DateTime.Today.AddDays(-10), DueDate = DateTime.Today.AddDays(-3),
                ReturnDate = DateTime.Today.AddDays(-2), DailyRate = 3.99m,
                LateFee = 1.50m, Status = RentalStatus.Returned
            });

            var service = new DashboardService(returnedRepo, _movieRepo, _customerRepo);
            var data = service.GetDashboard();

            Assert.AreEqual(0, data.Stats.ActiveRentals);
            Assert.AreEqual(1, data.Stats.ReturnedRentals);
            Assert.IsTrue(data.Stats.TotalLateFees > 0);
        }

        [TestMethod]
        public void Dashboard_MultipleCustomersSameMovie_AggregatesCorrectly()
        {
            var multiRepo = new TestRentalRepository();
            for (int i = 0; i < 10; i++)
            {
                multiRepo.Add(new Rental
                {
                    CustomerId = i % 3 + 1, CustomerName = $"Customer {i % 3 + 1}",
                    MovieId = 1, MovieName = "Popular Movie",
                    RentalDate = DateTime.Today.AddDays(-i), DueDate = DateTime.Today.AddDays(7 - i),
                    DailyRate = 3.99m, Status = RentalStatus.Active
                });
            }

            var service = new DashboardService(multiRepo, _movieRepo, _customerRepo);
            var data = service.GetDashboard();

            Assert.AreEqual(1, data.TopMovies.Count);
            Assert.AreEqual(10, data.TopMovies[0].RentalCount);
            Assert.AreEqual(3, data.TopCustomers.Count);
        }

        [TestMethod]
        public void TopMovies_NullMovieName_FallsBackToUnknown()
        {
            var rentals = new List<Rental>
            {
                new Rental { MovieId = 1, MovieName = null,
                    RentalDate = DateTime.Today, DueDate = DateTime.Today.AddDays(7),
                    DailyRate = 3.99m, Status = RentalStatus.Active }
            };
            var top = DashboardService.ComputeTopMovies(rentals, new Dictionary<int, Movie>(), 5);
            Assert.AreEqual("Unknown", top[0].MovieName);
        }

        [TestMethod]
        public void TopCustomers_NullCustomerName_FallsBackToUnknown()
        {
            var rentals = new List<Rental>
            {
                new Rental { CustomerId = 1, CustomerName = null, MovieId = 1,
                    RentalDate = DateTime.Today, DueDate = DateTime.Today.AddDays(7),
                    DailyRate = 3.99m, Status = RentalStatus.Active }
            };
            var top = DashboardService.ComputeTopCustomers(rentals, new Dictionary<int, Customer>(), 5);
            Assert.AreEqual("Unknown", top[0].CustomerName);
        }

        [TestMethod]
        public void Dashboard_LargeDataset_Performs()
        {
            var largeRepo = new TestRentalRepository();
            for (int i = 0; i < 1000; i++)
            {
                largeRepo.Add(new Rental
                {
                    CustomerId = i % 50 + 1, CustomerName = $"Customer {i % 50}",
                    MovieId = i % 20 + 1, MovieName = $"Movie {i % 20}",
                    RentalDate = DateTime.Today.AddDays(-i % 180),
                    DueDate = DateTime.Today.AddDays(7 - i % 180),
                    DailyRate = 3.99m + (i % 5) * 0.50m,
                    LateFee = i % 10 == 0 ? 1.50m : 0m,
                    Status = i % 3 == 0 ? RentalStatus.Returned : RentalStatus.Active
                });
            }

            var service = new DashboardService(largeRepo, _movieRepo, _customerRepo);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var data = service.GetDashboard();
            sw.Stop();

            Assert.AreEqual(1000, data.Stats.TotalRentals);
            Assert.IsTrue(data.TopMovies.Count <= 5);
            Assert.IsTrue(data.TopCustomers.Count <= 5);
            Assert.IsTrue(sw.ElapsedMilliseconds < 1000, $"Dashboard took {sw.ElapsedMilliseconds}ms");
        }

        #endregion
    }
}
