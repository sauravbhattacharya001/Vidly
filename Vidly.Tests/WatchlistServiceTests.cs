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
    public class WatchlistServiceTests
    {
        #region Stub Repositories

        private class StubWatchlistRepository : IWatchlistRepository
        {
            private readonly Dictionary<int, WatchlistItem> _items = new Dictionary<int, WatchlistItem>();
            private int _nextId = 1;

            public WatchlistItem GetById(int id) =>
                _items.TryGetValue(id, out var w) ? w : null;

            public IReadOnlyList<WatchlistItem> GetAll() =>
                _items.Values.ToList().AsReadOnly();

            public IReadOnlyList<WatchlistItem> GetByCustomer(int customerId) =>
                _items.Values
                    .Where(w => w.CustomerId == customerId)
                    .OrderByDescending(w => (int)w.Priority)
                    .ThenByDescending(w => w.AddedDate)
                    .ToList().AsReadOnly();

            public bool IsOnWatchlist(int customerId, int movieId) =>
                _items.Values.Any(w => w.CustomerId == customerId && w.MovieId == movieId);

            public WatchlistItem Add(WatchlistItem item)
            {
                if (IsOnWatchlist(item.CustomerId, item.MovieId))
                    throw new InvalidOperationException("Already on watchlist.");
                item.Id = _nextId++;
                _items[item.Id] = item;
                return item;
            }

            public void Remove(int id) => _items.Remove(id);

            public bool RemoveByCustomerAndMovie(int customerId, int movieId)
            {
                var item = _items.Values.FirstOrDefault(
                    w => w.CustomerId == customerId && w.MovieId == movieId);
                if (item == null) return false;
                _items.Remove(item.Id);
                return true;
            }

            public void Update(WatchlistItem item) { _items[item.Id] = item; }

            public int ClearCustomerWatchlist(int customerId)
            {
                var ids = _items.Values
                    .Where(w => w.CustomerId == customerId)
                    .Select(w => w.Id).ToList();
                foreach (var id in ids) _items.Remove(id);
                return ids.Count;
            }

            public WatchlistStats GetStats(int customerId)
            {
                var items = _items.Values.Where(w => w.CustomerId == customerId).ToList();
                return new WatchlistStats
                {
                    TotalItems = items.Count,
                    NormalCount = items.Count(w => w.Priority == WatchlistPriority.Normal),
                    HighCount = items.Count(w => w.Priority == WatchlistPriority.High),
                    MustWatchCount = items.Count(w => w.Priority == WatchlistPriority.MustWatch)
                };
            }

            public IReadOnlyList<PopularWatchlistMovie> GetMostWatchlisted(int limit = 10) =>
                _items.Values
                    .GroupBy(w => w.MovieId)
                    .Select(g => new PopularWatchlistMovie
                    {
                        MovieId = g.Key,
                        MovieName = g.First().MovieName,
                        WatchlistCount = g.Count()
                    })
                    .OrderByDescending(p => p.WatchlistCount)
                    .Take(limit)
                    .ToList().AsReadOnly();
        }

        private class StubMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();

            public void Seed(params Movie[] movies)
            {
                foreach (var m in movies) _movies[m.Id] = m;
            }

            public Movie GetById(int id) =>
                _movies.TryGetValue(id, out var m) ? m : null;

            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList().AsReadOnly();
            public void Add(Movie entity) { _movies[entity.Id] = entity; }
            public void Update(Movie entity) { _movies[entity.Id] = entity; }
            public void Remove(int id) { _movies.Remove(id); }

            public IReadOnlyList<Movie> GetByGenre(Genre genre) =>
                _movies.Values.Where(m => m.Genre == genre).ToList().AsReadOnly();
            public IReadOnlyList<Movie> Search(string query) =>
                _movies.Values.Where(m => m.Name.Contains(query)).ToList().AsReadOnly();
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                _movies.Values.Where(m => m.ReleaseDate.HasValue && m.ReleaseDate.Value.Year == year && m.ReleaseDate.Value.Month == month).ToList().AsReadOnly();
            public Movie GetRandom() => _movies.Values.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                _movies.Values.Where(m => (query == null || m.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) && (!genre.HasValue || m.Genre == genre) && (!minRating.HasValue || (m.Rating ?? 0) >= minRating.Value)).ToList().AsReadOnly();
        }

        private class StubCustomerRepository : ICustomerRepository
        {
            private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();

            public void Seed(params Customer[] customers)
            {
                foreach (var c in customers) _customers[c.Id] = c;
            }

            public Customer GetById(int id) =>
                _customers.TryGetValue(id, out var c) ? c : null;

            public IReadOnlyList<Customer> GetAll() => _customers.Values.ToList().AsReadOnly();
            public void Add(Customer entity) { _customers[entity.Id] = entity; }
            public void Update(Customer entity) { _customers[entity.Id] = entity; }
            public void Remove(int id) { _customers.Remove(id); }

            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType = null) =>
                _customers.Values.Where(c => c.Name.Contains(query)).ToList().AsReadOnly();
            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) =>
                _customers.Values.Where(c => c.MemberSince.HasValue && c.MemberSince.Value.Year == year && c.MemberSince.Value.Month == month).ToList().AsReadOnly();
            public CustomerStats GetStats() => new CustomerStats { TotalCustomers = _customers.Count };
        }

        private class StubRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            private int _nextId = 1;

            public void Seed(params Rental[] rentals)
            {
                foreach (var r in rentals)
                {
                    if (r.Id == 0) r.Id = _nextId++;
                    _rentals.Add(r);
                }
            }

            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals.AsReadOnly();
            public void Add(Rental entity) { entity.Id = _nextId++; _rentals.Add(entity); }
            public void Update(Rental entity) { }
            public void Remove(int id) { _rentals.RemoveAll(r => r.Id == id); }

            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _rentals.Where(r => r.CustomerId == customerId && r.ReturnDate == null)
                    .ToList().AsReadOnly();

            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _rentals.Where(r => r.MovieId == movieId).ToList().AsReadOnly();

            public IReadOnlyList<Rental> GetOverdue() =>
                _rentals.Where(r => r.IsOverdue).ToList().AsReadOnly();

            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                _rentals.ToList().AsReadOnly();

            public Rental ReturnRental(int rentalId) => GetById(rentalId);
            public bool IsMovieRentedOut(int movieId) =>
                _rentals.Any(r => r.MovieId == movieId && r.ReturnDate == null);

            public Rental Checkout(Rental rental) { Add(rental); return rental; }
            public RentalStats GetStats() => new RentalStats();
        }

        #endregion

        #region Setup

        private StubWatchlistRepository _watchlistRepo;
        private StubMovieRepository _movieRepo;
        private StubCustomerRepository _customerRepo;
        private StubRentalRepository _rentalRepo;
        private WatchlistService _service;

        private readonly Customer _alice = new Customer { Id = 1, Name = "Alice" };
        private readonly Customer _bob = new Customer { Id = 2, Name = "Bob" };
        private Movie _inception;
        private Movie _matrix;
        private Movie _newRelease;
        private Movie _classic;

        [TestInitialize]
        public void Setup()
        {
            _watchlistRepo = new StubWatchlistRepository();
            _movieRepo = new StubMovieRepository();
            _customerRepo = new StubCustomerRepository();
            _rentalRepo = new StubRentalRepository();

            _customerRepo.Seed(_alice, _bob);

            _inception = new Movie { Id = 1, Name = "Inception", Genre = Genre.Action, Rating = 5 };
            _matrix = new Movie { Id = 2, Name = "The Matrix", Genre = Genre.Action, Rating = 4 };
            _newRelease = new Movie { Id = 3, Name = "New Film", Genre = Genre.Drama, Rating = 3,
                ReleaseDate = DateTime.Today.AddDays(-10) };
            _classic = new Movie { Id = 4, Name = "Casablanca", Genre = Genre.Romance, Rating = 5,
                ReleaseDate = DateTime.Today.AddDays(-365) };
            _movieRepo.Seed(_inception, _matrix, _newRelease, _classic);

            _service = new WatchlistService(_watchlistRepo, _movieRepo, _customerRepo, _rentalRepo);
        }

        #endregion

        #region AddToWatchlist

        [TestMethod]
        public void AddToWatchlist_ValidInput_ReturnsItem()
        {
            var item = _service.AddToWatchlist(1, 1, "Must see!");
            Assert.IsNotNull(item);
            Assert.AreEqual(1, item.CustomerId);
            Assert.AreEqual(1, item.MovieId);
            Assert.AreEqual("Must see!", item.Note);
        }

        [TestMethod]
        public void AddToWatchlist_NewRelease_AutoPromotesToHigh()
        {
            var item = _service.AddToWatchlist(1, 3); // newRelease, no priority
            Assert.AreEqual(WatchlistPriority.High, item.Priority);
        }

        [TestMethod]
        public void AddToWatchlist_OldMovie_StaysNormal()
        {
            var item = _service.AddToWatchlist(1, 4); // classic, no priority
            Assert.AreEqual(WatchlistPriority.Normal, item.Priority);
        }

        [TestMethod]
        public void AddToWatchlist_ExplicitPriority_Honored()
        {
            var item = _service.AddToWatchlist(1, 3, priority: WatchlistPriority.MustWatch);
            Assert.AreEqual(WatchlistPriority.MustWatch, item.Priority);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddToWatchlist_InvalidCustomer_Throws()
        {
            _service.AddToWatchlist(999, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddToWatchlist_InvalidMovie_Throws()
        {
            _service.AddToWatchlist(1, 999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddToWatchlist_Duplicate_Throws()
        {
            _service.AddToWatchlist(1, 1);
            _service.AddToWatchlist(1, 1);
        }

        #endregion

        #region GetSmartWatchlist

        [TestMethod]
        public void GetSmartWatchlist_Empty_ReturnsEmpty()
        {
            var result = _service.GetSmartWatchlist(1);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void GetSmartWatchlist_OrdersByPriorityThenAvailability()
        {
            _service.AddToWatchlist(1, 1, priority: WatchlistPriority.Normal);
            _service.AddToWatchlist(1, 2, priority: WatchlistPriority.MustWatch);

            var result = _service.GetSmartWatchlist(1);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(WatchlistPriority.MustWatch, result[0].Item.Priority);
            Assert.AreEqual(WatchlistPriority.Normal, result[1].Item.Priority);
        }

        [TestMethod]
        public void GetSmartWatchlist_MarksRentedMovies()
        {
            _service.AddToWatchlist(1, 1);
            _rentalRepo.Seed(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = DateTime.Today.AddDays(-3),
                DueDate = DateTime.Today.AddDays(4),
                ReturnDate = DateTime.Today.AddDays(-1),
                DailyRate = 3m
            });

            var result = _service.GetSmartWatchlist(1);
            Assert.IsTrue(result[0].HasBeenRented);
        }

        [TestMethod]
        public void GetSmartWatchlist_DetectsAvailability()
        {
            _service.AddToWatchlist(1, 1);
            // Movie 1 is actively rented by someone else
            _rentalRepo.Seed(new Rental
            {
                CustomerId = 2, MovieId = 1,
                RentalDate = DateTime.Today.AddDays(-1),
                DueDate = DateTime.Today.AddDays(6),
                ReturnDate = null,
                DailyRate = 3m
            });

            var result = _service.GetSmartWatchlist(1);
            Assert.IsFalse(result[0].IsAvailable);
        }

        #endregion

        #region GetTrendingMovies

        [TestMethod]
        public void GetTrendingMovies_RankedByWatchlistCount()
        {
            _service.AddToWatchlist(1, 1);
            _service.AddToWatchlist(2, 1); // Inception on both
            _service.AddToWatchlist(1, 2); // Matrix on one

            var trending = _service.GetTrendingMovies(10);
            Assert.AreEqual(2, trending.Count);
            Assert.AreEqual(1, trending[0].MovieId); // Inception first (count=2)
            Assert.AreEqual(2, trending[0].WatchlistCount);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetTrendingMovies_ZeroLimit_Throws()
        {
            _service.GetTrendingMovies(0);
        }

        [TestMethod]
        public void GetTrendingMovies_IncludesGenreAndRating()
        {
            _service.AddToWatchlist(1, 1);
            var trending = _service.GetTrendingMovies(5);
            Assert.AreEqual(Genre.Action, trending[0].Genre);
            Assert.AreEqual(5, trending[0].Rating);
        }

        #endregion

        #region CompareWatchlists

        [TestMethod]
        public void CompareWatchlists_FindsSharedAndUnique()
        {
            _service.AddToWatchlist(1, 1); // Both have Inception
            _service.AddToWatchlist(1, 2); // Only Alice has Matrix
            _service.AddToWatchlist(2, 1); // Both have Inception
            _service.AddToWatchlist(2, 4); // Only Bob has Casablanca

            var comparison = _service.CompareWatchlists(1, 2);
            Assert.AreEqual(1, comparison.SharedMovies.Count);
            Assert.AreEqual("Inception", comparison.SharedMovies[0].MovieName);
            Assert.AreEqual(1, comparison.OnlyInA.Count);
            Assert.AreEqual(1, comparison.OnlyInB.Count);
        }

        [TestMethod]
        public void CompareWatchlists_CalculatesSimilarity()
        {
            _service.AddToWatchlist(1, 1);
            _service.AddToWatchlist(2, 1); // same movie
            _service.AddToWatchlist(1, 2); // unique to Alice

            var comparison = _service.CompareWatchlists(1, 2);
            // Dice: 2*1 / (2+1) = 0.6667
            Assert.IsTrue(comparison.SimilarityScore > 0.66);
            Assert.IsTrue(comparison.SimilarityScore < 0.67);
        }

        [TestMethod]
        public void CompareWatchlists_BothEmpty_ZeroSimilarity()
        {
            var comparison = _service.CompareWatchlists(1, 2);
            Assert.AreEqual(0.0, comparison.SimilarityScore);
            Assert.AreEqual(0, comparison.SharedMovies.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CompareWatchlists_SameCustomer_Throws()
        {
            _service.CompareWatchlists(1, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CompareWatchlists_InvalidCustomer_Throws()
        {
            _service.CompareWatchlists(1, 999);
        }

        #endregion

        #region GetInsights

        [TestMethod]
        public void GetInsights_ReturnsGenreBreakdown()
        {
            _service.AddToWatchlist(1, 1, priority: WatchlistPriority.MustWatch); // Action
            _service.AddToWatchlist(1, 2); // Action
            _service.AddToWatchlist(1, 3); // Drama

            var insights = _service.GetInsights(1);
            Assert.AreEqual(3, insights.TotalItems);
            Assert.AreEqual(Genre.Action, insights.TopGenre);
            Assert.AreEqual(2, insights.GenreBreakdown.Count);
            Assert.AreEqual(2, insights.GenreBreakdown[0].Count); // Action=2
        }

        [TestMethod]
        public void GetInsights_IdentifiesStaleItems()
        {
            // Add item that's been on watchlist for >30 days
            _watchlistRepo.Add(new WatchlistItem
            {
                CustomerId = 1, MovieId = 1, MovieName = "Inception",
                AddedDate = DateTime.UtcNow.AddDays(-45),
                Priority = WatchlistPriority.Normal
            });

            var insights = _service.GetInsights(1);
            Assert.AreEqual(1, insights.StaleCount);
        }

        [TestMethod]
        public void GetInsights_IdentifiesTopPick()
        {
            _service.AddToWatchlist(1, 1, priority: WatchlistPriority.Normal);    // Rating 5
            _service.AddToWatchlist(1, 2, priority: WatchlistPriority.MustWatch); // Rating 4

            var insights = _service.GetInsights(1);
            Assert.IsNotNull(insights.TopPick);
            Assert.AreEqual(2, insights.TopPick.MovieId); // MustWatch beats higher rating
        }

        [TestMethod]
        public void GetInsights_CalculatesAverageRating()
        {
            _service.AddToWatchlist(1, 1); // Rating 5
            _service.AddToWatchlist(1, 2); // Rating 4

            var insights = _service.GetInsights(1);
            Assert.AreEqual(4.5, insights.AverageRating);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetInsights_InvalidCustomer_Throws()
        {
            _service.GetInsights(999);
        }

        [TestMethod]
        public void GetInsights_CountsAlreadyRented()
        {
            _service.AddToWatchlist(1, 1);
            _rentalRepo.Seed(new Rental
            {
                CustomerId = 1, MovieId = 1,
                RentalDate = DateTime.Today.AddDays(-5),
                DueDate = DateTime.Today.AddDays(2),
                ReturnDate = DateTime.Today.AddDays(-2),
                DailyRate = 3m
            });

            var insights = _service.GetInsights(1);
            Assert.AreEqual(1, insights.AlreadyRentedCount);
        }

        #endregion

        #region SetPriority

        [TestMethod]
        public void SetPriority_UpdatesItem()
        {
            var item = _service.AddToWatchlist(1, 1, priority: WatchlistPriority.Normal);
            _service.SetPriority(item.Id, WatchlistPriority.MustWatch);

            var updated = _watchlistRepo.GetById(item.Id);
            Assert.AreEqual(WatchlistPriority.MustWatch, updated.Priority);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SetPriority_InvalidId_Throws()
        {
            _service.SetPriority(999, WatchlistPriority.High);
        }

        #endregion

        #region BulkAdd

        [TestMethod]
        public void BulkAdd_AddsMultipleMovies()
        {
            var result = _service.BulkAdd(1, new[] { 1, 2, 4 });
            Assert.AreEqual(3, result.Added.Count);
            Assert.AreEqual(0, result.AlreadyExists.Count);
            Assert.AreEqual(0, result.NotFound.Count);
            Assert.AreEqual(3, result.TotalProcessed);
        }

        [TestMethod]
        public void BulkAdd_SkipsDuplicates()
        {
            _service.AddToWatchlist(1, 1);
            var result = _service.BulkAdd(1, new[] { 1, 2 });
            Assert.AreEqual(1, result.Added.Count);
            Assert.AreEqual(1, result.AlreadyExists.Count);
        }

        [TestMethod]
        public void BulkAdd_TracksNotFound()
        {
            var result = _service.BulkAdd(1, new[] { 1, 999 });
            Assert.AreEqual(1, result.Added.Count);
            Assert.AreEqual(1, result.NotFound.Count);
            Assert.IsTrue(result.NotFound.Contains(999));
        }

        [TestMethod]
        public void BulkAdd_AutoPromotesNewReleases()
        {
            _service.BulkAdd(1, new[] { 3 }); // new release
            var items = _watchlistRepo.GetByCustomer(1);
            Assert.AreEqual(WatchlistPriority.High, items[0].Priority);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void BulkAdd_InvalidCustomer_Throws()
        {
            _service.BulkAdd(999, new[] { 1 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void BulkAdd_NullMovieIds_Throws()
        {
            _service.BulkAdd(1, null);
        }

        #endregion

        #region Constructor Validation

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullWatchlistRepo_Throws()
        {
            new WatchlistService(null, _movieRepo, _customerRepo, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new WatchlistService(_watchlistRepo, null, _customerRepo, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new WatchlistService(_watchlistRepo, _movieRepo, null, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepo_Throws()
        {
            new WatchlistService(_watchlistRepo, _movieRepo, _customerRepo, null);
        }

        #endregion
    }
}
