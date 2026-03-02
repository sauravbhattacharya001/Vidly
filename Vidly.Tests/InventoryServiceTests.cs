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
    public class InventoryServiceTests
    {
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

            public void Update(Movie movie)
            {
                if (_movies.ContainsKey(movie.Id))
                    _movies[movie.Id] = movie;
            }

            public void Remove(int id) => _movies.Remove(id);

            public Movie GetRandom() =>
                _movies.Values.FirstOrDefault();

            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                _movies.Values.Where(m =>
                    m.Name.IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList().AsReadOnly();

            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                _movies.Values.Where(m =>
                    m.ReleaseDate.HasValue &&
                    m.ReleaseDate.Value.Year == year &&
                    m.ReleaseDate.Value.Month == month)
                    .ToList().AsReadOnly();
        }

        private class TestRentalRepository : IRentalRepository
        {
            private readonly Dictionary<int, Rental> _rentals = new Dictionary<int, Rental>();
            private int _nextId = 1;

            public void AddRental(Rental rental)
            {
                if (rental.Id == 0) rental.Id = _nextId++;
                _rentals[rental.Id] = rental;
            }

            public void Add(Rental rental) => AddRental(rental);

            public Rental GetById(int id) =>
                _rentals.TryGetValue(id, out var r) ? r : null;

            public IReadOnlyList<Rental> GetAll() =>
                _rentals.Values.ToList().AsReadOnly();

            public void Update(Rental rental)
            {
                if (_rentals.ContainsKey(rental.Id))
                    _rentals[rental.Id] = rental;
            }

            public void Remove(int id) => _rentals.Remove(id);

            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _rentals.Values.Where(r => r.CustomerId == customerId
                    && r.Status != RentalStatus.Returned).ToList().AsReadOnly();

            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _rentals.Values.Where(r => r.MovieId == movieId).ToList().AsReadOnly();

            public IReadOnlyList<Rental> GetOverdue() =>
                _rentals.Values.Where(r => r.Status != RentalStatus.Returned
                    && r.DueDate < DateTime.Today).ToList().AsReadOnly();

            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                _rentals.Values.ToList().AsReadOnly();

            public Rental ReturnRental(int rentalId)
            {
                var r = GetById(rentalId);
                if (r != null) { r.Status = RentalStatus.Returned; r.ReturnDate = DateTime.Today; }
                return r;
            }

            public bool IsMovieRentedOut(int movieId) =>
                _rentals.Values.Any(r => r.MovieId == movieId
                    && r.Status != RentalStatus.Returned);

            public Rental Checkout(Rental rental) { Add(rental); return rental; }

            public RentalStats GetStats() => new RentalStats
            {
                TotalRentals = _rentals.Count,
                ActiveRentals = _rentals.Values.Count(r => r.Status == RentalStatus.Active),
                OverdueRentals = _rentals.Values.Count(r => r.Status == RentalStatus.Overdue)
            };
        }

        private TestMovieRepository _movieRepo;
        private TestRentalRepository _rentalRepo;
        private InventoryService _sut;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new TestMovieRepository();
            _rentalRepo = new TestRentalRepository();

            _movieRepo.Add(new Movie { Id = 1, Name = "Shrek!", Genre = Genre.Animation, Rating = 4 });
            _movieRepo.Add(new Movie { Id = 2, Name = "The Godfather", Genre = Genre.Drama, Rating = 5 });
            _movieRepo.Add(new Movie { Id = 3, Name = "Toy Story", Genre = Genre.Animation, Rating = 5 });
            _movieRepo.Add(new Movie { Id = 4, Name = "Inception", Genre = Genre.SciFi, Rating = 5 });

            _rentalRepo.AddRental(new Rental
            {
                Id = 1, CustomerId = 1, MovieId = 1, MovieName = "Shrek!",
                RentalDate = DateTime.Today.AddDays(-3),
                DueDate = DateTime.Today.AddDays(4),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });
            _rentalRepo.AddRental(new Rental
            {
                Id = 2, CustomerId = 2, MovieId = 2, MovieName = "The Godfather",
                RentalDate = DateTime.Today.AddDays(-10),
                DueDate = DateTime.Today.AddDays(-3),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });
            _rentalRepo.AddRental(new Rental
            {
                Id = 3, CustomerId = 3, MovieId = 3, MovieName = "Toy Story",
                RentalDate = DateTime.Today.AddDays(-14),
                DueDate = DateTime.Today.AddDays(-7),
                ReturnDate = DateTime.Today.AddDays(-6),
                DailyRate = 3.99m, Status = RentalStatus.Returned
            });

            _sut = new InventoryService(_rentalRepo, _movieRepo);
        }

        // ---- Constructor ----

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new InventoryService(null, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new InventoryService(_rentalRepo, null);
        }

        // ---- SetStock / GetStockCount ----

        [TestMethod]
        public void GetStockCount_Default_ReturnsDefaultCopies()
        {
            Assert.AreEqual(InventoryService.DefaultCopiesPerTitle,
                _sut.GetStockCount(1));
        }

        [TestMethod]
        public void SetStock_CustomValue_IsReturned()
        {
            _sut.SetStock(1, 5);
            Assert.AreEqual(5, _sut.GetStockCount(1));
        }

        [TestMethod]
        public void SetStock_Zero_IsAllowed()
        {
            _sut.SetStock(1, 0);
            Assert.AreEqual(0, _sut.GetStockCount(1));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void SetStock_Negative_Throws()
        {
            _sut.SetStock(1, -1);
        }

        [TestMethod]
        public void SetStock_Override_DoesNotAffectOtherMovies()
        {
            _sut.SetStock(1, 10);
            Assert.AreEqual(InventoryService.DefaultCopiesPerTitle,
                _sut.GetStockCount(2));
        }

        // ---- GetMovieStock ----

        [TestMethod]
        public void GetMovieStock_ExistingMovie_ReturnsStock()
        {
            var stock = _sut.GetMovieStock(1);
            Assert.IsNotNull(stock);
            Assert.AreEqual(1, stock.MovieId);
            Assert.AreEqual("Shrek!", stock.MovieName);
            Assert.AreEqual(Genre.Animation, stock.Genre);
        }

        [TestMethod]
        public void GetMovieStock_NonExistent_ReturnsNull()
        {
            Assert.IsNull(_sut.GetMovieStock(9999));
        }

        [TestMethod]
        public void GetMovieStock_ActiveRental_ReducesAvailability()
        {
            var stock = _sut.GetMovieStock(1);
            Assert.AreEqual(1, stock.RentedCopies);
            Assert.AreEqual(InventoryService.DefaultCopiesPerTitle - 1,
                stock.AvailableCopies);
        }

        [TestMethod]
        public void GetMovieStock_ReturnedRental_DoesNotReduceAvailability()
        {
            var stock = _sut.GetMovieStock(3);
            Assert.AreEqual(0, stock.RentedCopies);
            Assert.AreEqual(stock.TotalCopies, stock.AvailableCopies);
        }

        [TestMethod]
        public void GetMovieStock_OverdueRental_CountedSeparately()
        {
            var stock = _sut.GetMovieStock(2);
            Assert.AreEqual(1, stock.OverdueCopies);
            Assert.AreEqual(1, stock.RentedCopies);
        }

        [TestMethod]
        public void GetMovieStock_NoRentals_FullAvailability()
        {
            var stock = _sut.GetMovieStock(4);
            Assert.AreEqual(0, stock.RentedCopies);
            Assert.AreEqual(InventoryService.DefaultCopiesPerTitle,
                stock.AvailableCopies);
        }

        [TestMethod]
        public void GetMovieStock_EarliestReturn_Set()
        {
            var stock = _sut.GetMovieStock(1);
            Assert.IsNotNull(stock.EarliestReturn);
        }

        [TestMethod]
        public void GetMovieStock_NoActiveRentals_EarliestReturnNull()
        {
            var stock = _sut.GetMovieStock(4);
            Assert.IsNull(stock.EarliestReturn);
        }

        [TestMethod]
        public void GetMovieStock_AvailableCopies_NeverNegative()
        {
            _sut.SetStock(1, 0);
            var stock = _sut.GetMovieStock(1);
            Assert.IsTrue(stock.AvailableCopies >= 0);
        }

        // ---- MovieStock computed properties ----

        [TestMethod]
        public void MovieStock_IsAvailable_TrueWhenCopiesFree()
        {
            var stock = new MovieStock { TotalCopies = 3, RentedCopies = 1 };
            Assert.IsTrue(stock.IsAvailable);
        }

        [TestMethod]
        public void MovieStock_IsAvailable_FalseWhenAllRented()
        {
            var stock = new MovieStock { TotalCopies = 3, RentedCopies = 3 };
            Assert.IsFalse(stock.IsAvailable);
        }

        [TestMethod]
        public void MovieStock_Utilization_ZeroWhenNoneRented()
        {
            var stock = new MovieStock { TotalCopies = 3, RentedCopies = 0 };
            Assert.AreEqual(0.0, stock.Utilization);
        }

        [TestMethod]
        public void MovieStock_Utilization_OneWhenAllRented()
        {
            var stock = new MovieStock { TotalCopies = 3, RentedCopies = 3 };
            Assert.AreEqual(1.0, stock.Utilization);
        }

        [TestMethod]
        public void MovieStock_Utilization_ZeroWhenNoCopies()
        {
            var stock = new MovieStock { TotalCopies = 0, RentedCopies = 0 };
            Assert.AreEqual(0.0, stock.Utilization);
        }

        [TestMethod]
        [DataRow(3, 0, StockLevel.High)]
        [DataRow(3, 1, StockLevel.High)]
        [DataRow(4, 2, StockLevel.Medium)]
        [DataRow(5, 4, StockLevel.Low)]
        [DataRow(3, 3, StockLevel.OutOfStock)]
        [DataRow(0, 0, StockLevel.OutOfStock)]
        public void MovieStock_Level_CorrectClassification(
            int total, int rented, StockLevel expected)
        {
            var stock = new MovieStock { TotalCopies = total, RentedCopies = rented };
            Assert.AreEqual(expected, stock.Level);
        }

        // ---- GetAllStock ----

        [TestMethod]
        public void GetAllStock_ReturnsAllMovies()
        {
            var allStock = _sut.GetAllStock();
            Assert.AreEqual(4, allStock.Count);
        }

        [TestMethod]
        public void GetAllStock_SortedByUrgency()
        {
            _sut.SetStock(1, 1);
            var allStock = _sut.GetAllStock();
            Assert.AreEqual(StockLevel.OutOfStock, allStock[0].Level);
        }

        [TestMethod]
        public void GetAllStock_ContainsAllMovieNames()
        {
            var allStock = _sut.GetAllStock();
            var names = allStock.Select(s => s.MovieName).ToList();
            CollectionAssert.Contains(names, "Shrek!");
            CollectionAssert.Contains(names, "The Godfather");
            CollectionAssert.Contains(names, "Toy Story");
            CollectionAssert.Contains(names, "Inception");
        }

        // ---- GetByStockLevel ----

        [TestMethod]
        public void GetByStockLevel_FiltersCorrectly()
        {
            var highStock = _sut.GetByStockLevel(StockLevel.High);
            foreach (var s in highStock)
                Assert.AreEqual(StockLevel.High, s.Level);
        }

        [TestMethod]
        public void GetByStockLevel_EmptyWhenNoMatch()
        {
            var oos = _sut.GetByStockLevel(StockLevel.OutOfStock);
            Assert.AreEqual(0, oos.Count);
        }

        [TestMethod]
        public void GetByStockLevel_FindsLowStock()
        {
            _sut.SetStock(1, 1);
            var oos = _sut.GetByStockLevel(StockLevel.OutOfStock);
            Assert.AreEqual(1, oos.Count);
            Assert.AreEqual(1, oos[0].MovieId);
        }

        // ---- IsAvailable ----

        [TestMethod]
        public void IsAvailable_UnrentedMovie_True()
        {
            Assert.IsTrue(_sut.IsAvailable(4));
        }

        [TestMethod]
        public void IsAvailable_ReturnedMovie_True()
        {
            Assert.IsTrue(_sut.IsAvailable(3));
        }

        [TestMethod]
        public void IsAvailable_NonExistentMovie_False()
        {
            Assert.IsFalse(_sut.IsAvailable(9999));
        }

        [TestMethod]
        public void IsAvailable_AllCopiesRented_False()
        {
            _sut.SetStock(1, 1);
            Assert.IsFalse(_sut.IsAvailable(1));
        }

        [TestMethod]
        public void IsAvailable_MultipleCopies_TrueEvenWhenSomeRented()
        {
            Assert.IsTrue(_sut.IsAvailable(1));
        }

        // ---- GetSummary ----

        [TestMethod]
        public void GetSummary_TotalTitles_MatchesCatalog()
        {
            var summary = _sut.GetSummary();
            Assert.AreEqual(4, summary.TotalTitles);
        }

        [TestMethod]
        public void GetSummary_TotalCopies_Correct()
        {
            var summary = _sut.GetSummary();
            Assert.AreEqual(4 * InventoryService.DefaultCopiesPerTitle,
                summary.TotalCopies);
        }

        [TestMethod]
        public void GetSummary_TotalRented_Correct()
        {
            var summary = _sut.GetSummary();
            Assert.AreEqual(2, summary.TotalRented);
        }

        [TestMethod]
        public void GetSummary_TotalAvailable_Consistent()
        {
            var summary = _sut.GetSummary();
            Assert.AreEqual(summary.TotalCopies - summary.TotalRented,
                summary.TotalAvailable);
        }

        [TestMethod]
        public void GetSummary_OverallUtilization_InRange()
        {
            var summary = _sut.GetSummary();
            Assert.IsTrue(summary.OverallUtilization >= 0.0 &&
                summary.OverallUtilization <= 1.0);
        }

        [TestMethod]
        public void GetSummary_OverdueCount_Correct()
        {
            var summary = _sut.GetSummary();
            Assert.AreEqual(1, summary.TotalOverdue);
        }

        [TestMethod]
        public void GetSummary_OverdueRevenue_Positive()
        {
            var summary = _sut.GetSummary();
            Assert.IsTrue(summary.OverdueRevenue > 0);
        }

        [TestMethod]
        public void GetSummary_GenreBreakdown_NotEmpty()
        {
            var summary = _sut.GetSummary();
            Assert.IsTrue(summary.GenreBreakdown.Count > 0);
        }

        [TestMethod]
        public void GetSummary_GenreBreakdown_TitlesSum()
        {
            var summary = _sut.GetSummary();
            var totalFromGenres = summary.GenreBreakdown.Sum(g => g.TitleCount);
            Assert.AreEqual(summary.TotalTitles, totalFromGenres);
        }

        [TestMethod]
        public void GetSummary_GenreBreakdown_SortedByUtilization()
        {
            var summary = _sut.GetSummary();
            for (int i = 1; i < summary.GenreBreakdown.Count; i++)
            {
                Assert.IsTrue(
                    summary.GenreBreakdown[i - 1].Utilization >=
                    summary.GenreBreakdown[i].Utilization);
            }
        }

        [TestMethod]
        public void GetSummary_GenreBreakdown_ContainsAnimation()
        {
            var summary = _sut.GetSummary();
            var animation = summary.GenreBreakdown
                .FirstOrDefault(g => g.GenreName == "Animation");
            Assert.IsNotNull(animation);
            Assert.AreEqual(2, animation.TitleCount);
        }

        // ---- ForecastAvailability ----

        [TestMethod]
        public void ForecastAvailability_ValidMovie_ReturnsForecast()
        {
            var forecast = _sut.ForecastAvailability(1, 7);
            Assert.AreEqual(7, forecast.Count);
        }

        [TestMethod]
        public void ForecastAvailability_NonExistent_ReturnsEmpty()
        {
            var forecast = _sut.ForecastAvailability(9999, 7);
            Assert.AreEqual(0, forecast.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ForecastAvailability_DaysZero_Throws()
        {
            _sut.ForecastAvailability(1, 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ForecastAvailability_DaysNegative_Throws()
        {
            _sut.ForecastAvailability(1, -5);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void ForecastAvailability_Days91_Throws()
        {
            _sut.ForecastAvailability(1, 91);
        }

        [TestMethod]
        public void ForecastAvailability_AvailabilityNonDecreasing()
        {
            var forecast = _sut.ForecastAvailability(1, 14);
            for (int i = 1; i < forecast.Count; i++)
            {
                Assert.IsTrue(forecast[i].PredictedAvailable >= forecast[i - 1].PredictedAvailable,
                    $"Day {i}: availability decreased");
            }
        }

        [TestMethod]
        public void ForecastAvailability_DatesAreSequential()
        {
            var forecast = _sut.ForecastAvailability(1, 5);
            for (int i = 1; i < forecast.Count; i++)
            {
                Assert.AreEqual(forecast[i - 1].Date.AddDays(1), forecast[i].Date);
            }
        }

        [TestMethod]
        public void ForecastAvailability_ExpectedReturns_Monotonic()
        {
            var forecast = _sut.ForecastAvailability(1, 14);
            for (int i = 1; i < forecast.Count; i++)
            {
                Assert.IsTrue(forecast[i].ExpectedReturns >= forecast[i - 1].ExpectedReturns);
            }
        }

        [TestMethod]
        public void ForecastAvailability_ReturnedMovie_FullAvailability()
        {
            var forecast = _sut.ForecastAvailability(3, 3);
            foreach (var f in forecast)
                Assert.AreEqual(InventoryService.DefaultCopiesPerTitle,
                    f.PredictedAvailable);
        }

        [TestMethod]
        public void ForecastAvailability_NoRentals_FullAvailability()
        {
            var forecast = _sut.ForecastAvailability(4, 3);
            foreach (var f in forecast)
                Assert.AreEqual(InventoryService.DefaultCopiesPerTitle,
                    f.PredictedAvailable);
        }

        [TestMethod]
        public void ForecastAvailability_StartsFromToday()
        {
            var forecast = _sut.ForecastAvailability(1, 1);
            Assert.AreEqual(1, forecast.Count);
            Assert.AreEqual(DateTime.Today, forecast[0].Date);
        }

        // ---- GetRestockingNeeds ----

        [TestMethod]
        public void GetRestockingNeeds_DefaultSeed_Empty()
        {
            var needs = _sut.GetRestockingNeeds();
            Assert.AreEqual(0, needs.Count);
        }

        [TestMethod]
        public void GetRestockingNeeds_LowStock_Included()
        {
            _sut.SetStock(1, 1);
            var needs = _sut.GetRestockingNeeds();
            Assert.IsTrue(needs.Any(n => n.MovieId == 1));
        }

        [TestMethod]
        public void GetRestockingNeeds_RespectsLimit()
        {
            _sut.SetStock(1, 1);
            _sut.SetStock(2, 1);
            var needs = _sut.GetRestockingNeeds(1);
            Assert.IsTrue(needs.Count <= 1);
        }

        [TestMethod]
        public void GetRestockingNeeds_OnlyLowAndOutOfStock()
        {
            _sut.SetStock(1, 1);
            var needs = _sut.GetRestockingNeeds();
            foreach (var n in needs)
                Assert.IsTrue(n.Level == StockLevel.OutOfStock
                    || n.Level == StockLevel.Low);
        }

        // ---- Model computed properties ----

        [TestMethod]
        public void GenreStock_Utilization_ZeroWhenEmpty()
        {
            var gs = new GenreStock { TotalCopies = 0, RentedCopies = 0 };
            Assert.AreEqual(0.0, gs.Utilization);
        }

        [TestMethod]
        public void GenreStock_Utilization_Correct()
        {
            var gs = new GenreStock { TotalCopies = 10, RentedCopies = 3 };
            Assert.AreEqual(0.3, gs.Utilization, 0.01);
        }

        [TestMethod]
        public void InventorySummary_TotalAvailable_Computed()
        {
            var s = new InventorySummary { TotalCopies = 10, TotalRented = 4 };
            Assert.AreEqual(6, s.TotalAvailable);
        }

        [TestMethod]
        public void InventorySummary_OverallUtilization_Computed()
        {
            var s = new InventorySummary { TotalCopies = 10, TotalRented = 5 };
            Assert.AreEqual(0.5, s.OverallUtilization);
        }

        [TestMethod]
        public void InventorySummary_OverallUtilization_ZeroWhenNoCopies()
        {
            var s = new InventorySummary { TotalCopies = 0, TotalRented = 0 };
            Assert.AreEqual(0.0, s.OverallUtilization);
        }

        // ---- Integration ----

        [TestMethod]
        public void CustomStock_AffectsAvailability()
        {
            _sut.SetStock(1, 10);
            var stock = _sut.GetMovieStock(1);
            Assert.AreEqual(10, stock.TotalCopies);
            Assert.AreEqual(9, stock.AvailableCopies);
        }

        [TestMethod]
        public void CustomStock_AffectsSummary()
        {
            _sut.SetStock(1, 100);
            var summary = _sut.GetSummary();
            Assert.IsTrue(summary.TotalCopies >= 100);
        }

        [TestMethod]
        public void CustomStock_AffectsForecast()
        {
            _sut.SetStock(1, 10);
            var forecast = _sut.ForecastAvailability(1, 3);
            foreach (var f in forecast)
                Assert.IsTrue(f.PredictedAvailable >= 9);
        }

        [TestMethod]
        public void MultipleActiveRentals_CountedCorrectly()
        {
            _rentalRepo.AddRental(new Rental
            {
                Id = 10, CustomerId = 5, MovieId = 1, MovieName = "Shrek!",
                RentalDate = DateTime.Today.AddDays(-1),
                DueDate = DateTime.Today.AddDays(6),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var stock = _sut.GetMovieStock(1);
            Assert.AreEqual(2, stock.RentedCopies);
            Assert.AreEqual(InventoryService.DefaultCopiesPerTitle - 2,
                stock.AvailableCopies);
        }

        [TestMethod]
        public void MultipleActiveRentals_EarliestReturnIsCorrect()
        {
            _rentalRepo.AddRental(new Rental
            {
                Id = 11, CustomerId = 6, MovieId = 1, MovieName = "Shrek!",
                RentalDate = DateTime.Today.AddDays(-1),
                DueDate = DateTime.Today.AddDays(10),
                DailyRate = 3.99m, Status = RentalStatus.Active
            });

            var stock = _sut.GetMovieStock(1);
            Assert.AreEqual(DateTime.Today.AddDays(4), stock.EarliestReturn);
        }
    }
}
