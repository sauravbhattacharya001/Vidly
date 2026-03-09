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
    public class MovieComparisonServiceTests
    {
        #region Stub Repositories

        private class StubMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();
            public void Add(Movie movie) { _movies[movie.Id] = movie; }
            public Movie GetById(int id) => _movies.TryGetValue(id, out var m) ? m : null;
            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList();
            public void Update(Movie movie) { _movies[movie.Id] = movie; }
            public void Remove(int id) { _movies.Remove(id); }
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) => new List<Movie>();
            public Movie GetRandom() => _movies.Values.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) => new List<Movie>();
        }

        private class StubReviewRepository : IReviewRepository
        {
            private readonly List<Review> _reviews = new List<Review>();
            private int _nextId = 1;
            public void Add(Review r) { r.Id = _nextId++; _reviews.Add(r); }
            public Review GetById(int id) => _reviews.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Review> GetAll() => _reviews.ToList();
            public void Update(Review r) { }
            public void Remove(int id) { _reviews.RemoveAll(r => r.Id == id); }
            public IReadOnlyList<Review> GetByMovie(int movieId) =>
                _reviews.Where(r => r.MovieId == movieId).ToList();
            public IReadOnlyList<Review> GetByCustomer(int customerId) =>
                _reviews.Where(r => r.CustomerId == customerId).ToList();
            public Review GetByCustomerAndMovie(int customerId, int movieId) =>
                _reviews.FirstOrDefault(r => r.CustomerId == customerId && r.MovieId == movieId);
            public bool HasReviewed(int customerId, int movieId) =>
                _reviews.Any(r => r.CustomerId == customerId && r.MovieId == movieId);
            public ReviewStats GetMovieStats(int movieId) => new ReviewStats { MovieId = movieId };
            public IReadOnlyList<MovieRating> GetTopRatedMovies(int count, int minReviews = 1) => new List<MovieRating>();
            public IReadOnlyList<Review> Search(string query, int? minStars) => new List<Review>();
        }

        private class StubRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            private int _nextId = 1;
            public void Add(Rental r) { r.Id = _nextId++; _rentals.Add(r); }
            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals.ToList();
            public void Update(Rental r) { }
            public void Remove(int id) { _rentals.RemoveAll(r => r.Id == id); }
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) => new List<Rental>();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _rentals.Where(r => r.MovieId == movieId).ToList();
            public IReadOnlyList<Rental> GetOverdue() => new List<Rental>();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) => new List<Rental>();
            public Rental ReturnRental(int rentalId) => null;
            public bool IsMovieRentedOut(int movieId) =>
                _rentals.Any(r => r.MovieId == movieId && r.Status == RentalStatus.Active);
            public Rental Checkout(Rental rental) { Add(rental); return rental; }
            public Rental Checkout(Rental rental, int maxConcurrentRentals) { Add(rental); return rental; }
            public RentalStats GetStats() => new RentalStats();
        }

        #endregion

        private StubMovieRepository _movieRepo;
        private StubReviewRepository _reviewRepo;
        private StubRentalRepository _rentalRepo;
        private MovieComparisonService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new StubMovieRepository();
            _reviewRepo = new StubReviewRepository();
            _rentalRepo = new StubRentalRepository();
            _service = new MovieComparisonService(_movieRepo, _reviewRepo, _rentalRepo);
        }

        private Movie AddMovie(int id, string name, Genre? genre = Genre.Action,
            DateTime? releaseDate = null, decimal? dailyRate = null)
        {
            var movie = new Movie
            {
                Id = id,
                Name = name,
                Genre = genre,
                ReleaseDate = releaseDate ?? new DateTime(2020, 1, 1),
                DailyRate = dailyRate
            };
            _movieRepo.Add(movie);
            return movie;
        }

        private void AddReview(int movieId, int customerId, int stars)
        {
            _reviewRepo.Add(new Review
            {
                MovieId = movieId,
                CustomerId = customerId,
                Stars = stars
            });
        }

        private void AddRental(int movieId, int customerId,
            RentalStatus status = RentalStatus.Returned)
        {
            _rentalRepo.Add(new Rental
            {
                MovieId = movieId,
                CustomerId = customerId,
                RentalDate = DateTime.Today.AddDays(-7),
                DueDate = DateTime.Today.AddDays(-2),
                DailyRate = 3.0m,
                Status = status
            });
        }

        // ── Constructor Tests ──────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullMovieRepo_Throws()
        {
            new MovieComparisonService(null, _reviewRepo, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullReviewRepo_Throws()
        {
            new MovieComparisonService(_movieRepo, null, _rentalRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Ctor_NullRentalRepo_Throws()
        {
            new MovieComparisonService(_movieRepo, _reviewRepo, null);
        }

        // ── Compare Validation ─────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Compare_NullIds_Throws()
        {
            _service.Compare(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Compare_SingleId_Throws()
        {
            AddMovie(1, "Movie A");
            _service.Compare(new[] { 1 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Compare_FiveIds_Throws()
        {
            for (int i = 1; i <= 5; i++) AddMovie(i, $"Movie {i}");
            _service.Compare(new[] { 1, 2, 3, 4, 5 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Compare_InvalidIds_NotEnoughFound_Throws()
        {
            AddMovie(1, "Movie A");
            _service.Compare(new[] { 1, 99 }); // only 1 found
        }

        // ── Compare — Basic Functionality ──────────────────────────

        [TestMethod]
        public void Compare_TwoMovies_ReturnsCorrectEntries()
        {
            AddMovie(1, "The Matrix", Genre.Action);
            AddMovie(2, "Inception", Genre.SciFi);

            var result = _service.Compare(new[] { 1, 2 });

            Assert.AreEqual(2, result.Entries.Count);
            Assert.IsTrue(result.Entries.Any(e => e.Movie.Name == "The Matrix"));
            Assert.IsTrue(result.Entries.Any(e => e.Movie.Name == "Inception"));
            Assert.IsNotNull(result.ComparedAt);
        }

        [TestMethod]
        public void Compare_FourMovies_Succeeds()
        {
            for (int i = 1; i <= 4; i++) AddMovie(i, $"Movie {i}");

            var result = _service.Compare(new[] { 1, 2, 3, 4 });

            Assert.AreEqual(4, result.Entries.Count);
        }

        [TestMethod]
        public void Compare_DuplicateIds_Deduplicated()
        {
            AddMovie(1, "A");
            AddMovie(2, "B");

            var result = _service.Compare(new[] { 1, 2, 1, 2 });

            Assert.AreEqual(2, result.Entries.Count);
        }

        // ── Ratings ────────────────────────────────────────────────

        [TestMethod]
        public void Compare_BestRated_IdentifiedCorrectly()
        {
            AddMovie(1, "Average Movie");
            AddMovie(2, "Great Movie");

            AddReview(1, 10, 3);
            AddReview(2, 10, 5);
            AddReview(2, 11, 4);

            var result = _service.Compare(new[] { 1, 2 });

            Assert.AreEqual(2, result.BestRatedId);
        }

        [TestMethod]
        public void Compare_NoReviews_AverageRatingIsNull()
        {
            AddMovie(1, "A");
            AddMovie(2, "B");

            var result = _service.Compare(new[] { 1, 2 });

            Assert.IsNull(result.Entries[0].AverageRating);
            Assert.IsNull(result.Entries[1].AverageRating);
            Assert.IsNull(result.BestRatedId);
        }

        [TestMethod]
        public void Compare_ReviewCount_Tracked()
        {
            AddMovie(1, "A");
            AddMovie(2, "B");

            AddReview(1, 10, 4);
            AddReview(1, 11, 5);
            AddReview(1, 12, 3);
            AddReview(2, 10, 5);

            var result = _service.Compare(new[] { 1, 2 });

            var entryA = result.Entries.First(e => e.Movie.Id == 1);
            var entryB = result.Entries.First(e => e.Movie.Id == 2);
            Assert.AreEqual(3, entryA.ReviewCount);
            Assert.AreEqual(1, entryB.ReviewCount);
            Assert.AreEqual(1, result.MostReviewedId);
        }

        // ── Rentals & Popularity ───────────────────────────────────

        [TestMethod]
        public void Compare_MostPopular_BasedOnTotalRentals()
        {
            AddMovie(1, "A");
            AddMovie(2, "B");

            AddRental(1, 10);
            AddRental(1, 11);
            AddRental(1, 12);
            AddRental(2, 10);

            var result = _service.Compare(new[] { 1, 2 });

            Assert.AreEqual(1, result.MostPopularId);
            Assert.AreEqual(3, result.Entries.First(e => e.Movie.Id == 1).TotalRentals);
        }

        // ── Availability ───────────────────────────────────────────

        [TestMethod]
        public void Compare_ActiveRental_MarksUnavailable()
        {
            AddMovie(1, "A");
            AddMovie(2, "B");

            AddRental(1, 10, RentalStatus.Active);

            var result = _service.Compare(new[] { 1, 2 });

            var entryA = result.Entries.First(e => e.Movie.Id == 1);
            var entryB = result.Entries.First(e => e.Movie.Id == 2);
            Assert.IsFalse(entryA.IsAvailable);
            Assert.IsTrue(entryA.CurrentlyRented);
            Assert.IsTrue(entryB.IsAvailable);
            Assert.IsFalse(entryB.CurrentlyRented);
        }

        [TestMethod]
        public void Compare_ReturnedRental_StillAvailable()
        {
            AddMovie(1, "A");
            AddMovie(2, "B");

            AddRental(1, 10, RentalStatus.Returned);

            var result = _service.Compare(new[] { 1, 2 });

            Assert.IsTrue(result.Entries.First(e => e.Movie.Id == 1).IsAvailable);
        }

        // ── Pricing ────────────────────────────────────────────────

        [TestMethod]
        public void Compare_CheapestId_IdentifiedCorrectly()
        {
            AddMovie(1, "Cheap", dailyRate: 1.99m);
            AddMovie(2, "Expensive", dailyRate: 9.99m);

            var result = _service.Compare(new[] { 1, 2 });

            Assert.AreEqual(1, result.CheapestId);
        }

        [TestMethod]
        public void Compare_WeeklyEstimate_IsSevenTimesDailyRate()
        {
            AddMovie(1, "A", dailyRate: 2.00m);
            AddMovie(2, "B", dailyRate: 3.00m);

            var result = _service.Compare(new[] { 1, 2 });

            var entry = result.Entries.First(e => e.Movie.Id == 1);
            Assert.AreEqual(entry.DailyRate * 7, entry.WeeklyEstimate);
        }

        // ── Age & Display ──────────────────────────────────────────

        [TestMethod]
        public void Compare_AgeDays_CalculatedFromReleaseDate()
        {
            var releaseDate = DateTime.Today.AddDays(-100);
            AddMovie(1, "A", releaseDate: releaseDate);
            AddMovie(2, "B", releaseDate: DateTime.Today);

            var result = _service.Compare(new[] { 1, 2 });

            var entryA = result.Entries.First(e => e.Movie.Id == 1);
            var entryB = result.Entries.First(e => e.Movie.Id == 2);
            Assert.AreEqual(100, entryA.AgeDays);
            Assert.AreEqual(0, entryB.AgeDays);
        }

        [TestMethod]
        public void Compare_NoReleaseDate_AgeDaysIsNull()
        {
            var movie = new Movie { Id = 1, Name = "A", ReleaseDate = null };
            _movieRepo.Add(movie);
            AddMovie(2, "B");

            var result = _service.Compare(new[] { 1, 2 });

            var entry = result.Entries.First(e => e.Movie.Id == 1);
            Assert.IsNull(entry.AgeDays);
        }

        // ── AgeDisplay property ────────────────────────────────────

        [TestMethod]
        public void AgeDisplay_NullDays_ReturnsUnknown()
        {
            var entry = new MovieComparisonEntry { AgeDays = null };
            Assert.AreEqual("Unknown", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_NegativeDays_ReturnsUpcoming()
        {
            var entry = new MovieComparisonEntry { AgeDays = -5 };
            Assert.AreEqual("Upcoming", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_OneDaySingular()
        {
            var entry = new MovieComparisonEntry { AgeDays = 1 };
            Assert.AreEqual("1 day", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_MultipleDays()
        {
            var entry = new MovieComparisonEntry { AgeDays = 15 };
            Assert.AreEqual("15 days", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_OneMonth()
        {
            var entry = new MovieComparisonEntry { AgeDays = 30 };
            Assert.AreEqual("1 month", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_MultipleMonths()
        {
            var entry = new MovieComparisonEntry { AgeDays = 90 };
            Assert.AreEqual("3 months", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_OneYear()
        {
            var entry = new MovieComparisonEntry { AgeDays = 365 };
            Assert.AreEqual("1 year", entry.AgeDisplay);
        }

        [TestMethod]
        public void AgeDisplay_MultipleYears()
        {
            var entry = new MovieComparisonEntry { AgeDays = 800 };
            Assert.AreEqual("2 years", entry.AgeDisplay);
        }

        // ── StarDisplay property ───────────────────────────────────

        [TestMethod]
        public void StarDisplay_NoRating_ReturnsNoRatings()
        {
            var entry = new MovieComparisonEntry { AverageRating = null };
            Assert.AreEqual("No ratings", entry.StarDisplay);
        }

        [TestMethod]
        public void StarDisplay_FiveStars()
        {
            var entry = new MovieComparisonEntry { AverageRating = 5.0 };
            Assert.AreEqual("★★★★★", entry.StarDisplay);
        }

        [TestMethod]
        public void StarDisplay_ThreeStars()
        {
            var entry = new MovieComparisonEntry { AverageRating = 3.2 };
            Assert.AreEqual("★★★☆☆", entry.StarDisplay);
        }

        // ── GetAvailableMovies ─────────────────────────────────────

        [TestMethod]
        public void GetAvailableMovies_ReturnsAllMovies()
        {
            AddMovie(1, "A");
            AddMovie(2, "B");
            AddMovie(3, "C");

            var movies = _service.GetAvailableMovies();

            Assert.AreEqual(3, movies.Count);
        }

        [TestMethod]
        public void GetAvailableMovies_EmptyRepo_ReturnsEmpty()
        {
            var movies = _service.GetAvailableMovies();
            Assert.AreEqual(0, movies.Count);
        }
    }
}
