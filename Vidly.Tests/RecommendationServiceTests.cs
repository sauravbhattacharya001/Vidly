using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;
using Vidly.ViewModels;

namespace Vidly.Tests
{
    [TestClass]
    public class RecommendationServiceTests
    {
        #region Test Helpers

        /// <summary>
        /// Simple in-memory movie repository for testing (isolated from static state).
        /// </summary>
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

        /// <summary>
        /// Simple in-memory rental repository for testing (isolated from static state).
        /// </summary>
        private class TestRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            private int _nextId = 1;

            public void Add(Rental rental)
            {
                if (rental.Id == 0) rental.Id = _nextId++;
                _rentals.Add(rental);
            }

            public Rental GetById(int id) =>
                _rentals.FirstOrDefault(r => r.Id == id);

            public IReadOnlyList<Rental> GetAll() =>
                _rentals.AsReadOnly();

            public void Update(Rental rental)
            {
                var idx = _rentals.FindIndex(r => r.Id == rental.Id);
                if (idx >= 0) _rentals[idx] = rental;
            }

            public void Remove(int id)
            {
                var rental = _rentals.FirstOrDefault(r => r.Id == id);
                if (rental == null) throw new KeyNotFoundException();
                _rentals.Remove(rental);
            }

            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _rentals.Where(r => r.CustomerId == customerId && r.Status != RentalStatus.Returned)
                    .ToList().AsReadOnly();

            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _rentals.Where(r => r.MovieId == movieId).ToList().AsReadOnly();

            public IReadOnlyList<Rental> GetOverdue() =>
                _rentals.Where(r => r.Status == RentalStatus.Overdue).ToList().AsReadOnly();

            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                _rentals.ToList().AsReadOnly();

            public Rental ReturnRental(int rentalId)
            {
                var rental = _rentals.FirstOrDefault(r => r.Id == rentalId);
                if (rental != null) rental.Status = RentalStatus.Returned;
                return rental;
            }

            public bool IsMovieRentedOut(int movieId) =>
                _rentals.Any(r => r.MovieId == movieId && r.Status != RentalStatus.Returned);

            public Rental Checkout(Rental rental) { Add(rental); return rental; }

            public RentalStats GetStats() => new RentalStats
            {
                TotalRentals = _rentals.Count,
                ActiveRentals = _rentals.Count(r => r.Status == RentalStatus.Active),
                OverdueRentals = _rentals.Count(r => r.Status == RentalStatus.Overdue),
                ReturnedRentals = _rentals.Count(r => r.Status == RentalStatus.Returned)
            };
        }

        private static Movie CreateMovie(int id, string name, Genre? genre = null, int? rating = null)
        {
            return new Movie
            {
                Id = id,
                Name = name,
                Genre = genre,
                Rating = rating,
                ReleaseDate = new DateTime(2020, 1, 1)
            };
        }

        private static Rental CreateRental(int customerId, int movieId, int daysAgo = 5)
        {
            return new Rental
            {
                CustomerId = customerId,
                MovieId = movieId,
                RentalDate = DateTime.Today.AddDays(-daysAgo),
                DueDate = DateTime.Today.AddDays(-daysAgo + 7),
                DailyRate = 3.99m,
                Status = RentalStatus.Active
            };
        }

        #endregion

        #region Constructor Tests

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepository_Throws()
        {
            new RecommendationService(null, new TestRentalRepository());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullRentalRepository_Throws()
        {
            new RecommendationService(new TestMovieRepository(), null);
        }

        [TestMethod]
        public void Constructor_ValidParams_CreatesInstance()
        {
            var service = new RecommendationService(
                new TestMovieRepository(), new TestRentalRepository());
            Assert.IsNotNull(service);
        }

        #endregion

        #region GetRecommendations - Parameter Validation

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetRecommendations_ZeroMax_Throws()
        {
            var service = new RecommendationService(
                new TestMovieRepository(), new TestRentalRepository());
            service.GetRecommendations(1, maxRecommendations: 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetRecommendations_NegativeMax_Throws()
        {
            var service = new RecommendationService(
                new TestMovieRepository(), new TestRentalRepository());
            service.GetRecommendations(1, maxRecommendations: -1);
        }

        #endregion

        #region GetRecommendations - No Rental History

        [TestMethod]
        public void GetRecommendations_NoRentals_ReturnsEmptyPreferences()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Movie A", Genre.Action, 4));
            var rentals = new TestRentalRepository();

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            Assert.AreEqual(0, result.TotalRentals);
            Assert.AreEqual(0, result.GenrePreferences.Count);
        }

        [TestMethod]
        public void GetRecommendations_NoRentals_StillRecommendsByRating()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Great Movie", Genre.Action, 5));
            movies.Add(CreateMovie(2, "OK Movie", Genre.Drama, 3));
            var rentals = new TestRentalRepository();

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            Assert.IsTrue(result.Recommendations.Count > 0,
                "Should recommend movies even with no history (by rating).");
            Assert.AreEqual("Great Movie", result.Recommendations[0].Movie.Name,
                "Higher-rated movie should rank first.");
        }

        #endregion

        #region GetRecommendations - Basic Functionality

        [TestMethod]
        public void GetRecommendations_ExcludesRentedMovies()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Rented Movie", Genre.Action, 5));
            movies.Add(CreateMovie(2, "Available Movie", Genre.Action, 5));
            var rentals = new TestRentalRepository();
            rentals.Add(CreateRental(1, 1)); // Customer 1 rented movie 1

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            Assert.IsFalse(result.Recommendations.Any(r => r.Movie.Id == 1),
                "Should not recommend already-rented movies.");
            Assert.IsTrue(result.Recommendations.Any(r => r.Movie.Id == 2),
                "Should recommend available movies.");
        }

        [TestMethod]
        public void GetRecommendations_RespectsMaxLimit()
        {
            var movies = new TestMovieRepository();
            for (int i = 1; i <= 20; i++)
                movies.Add(CreateMovie(i, $"Movie {i}", Genre.Action, 4));
            var rentals = new TestRentalRepository();

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1, maxRecommendations: 5);

            Assert.AreEqual(5, result.Recommendations.Count,
                "Should return at most maxRecommendations items.");
        }

        [TestMethod]
        public void GetRecommendations_PopulatesCustomerId()
        {
            var movies = new TestMovieRepository();
            var rentals = new TestRentalRepository();

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(42);

            Assert.AreEqual(42, result.CustomerId);
        }

        [TestMethod]
        public void GetRecommendations_CountsTotalAvailableMovies()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "A", Genre.Action, 3));
            movies.Add(CreateMovie(2, "B", Genre.Action, 3));
            movies.Add(CreateMovie(3, "C", Genre.Action, 3));
            var rentals = new TestRentalRepository();
            rentals.Add(CreateRental(1, 1));

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            Assert.AreEqual(2, result.TotalAvailableMovies,
                "Should exclude rented movies from available count.");
        }

        #endregion

        #region Genre Preference Analysis

        [TestMethod]
        public void AnalyzeGenrePreferences_EmptyRentals_ReturnsEmpty()
        {
            var movies = new TestMovieRepository();
            var prefs = RecommendationService.AnalyzeGenrePreferences(
                new List<Rental>(), movies);

            Assert.AreEqual(0, prefs.Count);
        }

        [TestMethod]
        public void AnalyzeGenrePreferences_NullRentals_ReturnsEmpty()
        {
            var movies = new TestMovieRepository();
            var prefs = RecommendationService.AnalyzeGenrePreferences(null, movies);

            Assert.AreEqual(0, prefs.Count);
        }

        [TestMethod]
        public void AnalyzeGenrePreferences_SingleGenre_HasPositiveScore()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Action Movie", Genre.Action, 4));
            var rentals = new List<Rental> { CreateRental(1, 1) };

            var prefs = RecommendationService.AnalyzeGenrePreferences(rentals, movies);

            Assert.IsTrue(prefs.ContainsKey(Genre.Action));
            Assert.IsTrue(prefs[Genre.Action] > 0, "Score should be positive.");
        }

        [TestMethod]
        public void AnalyzeGenrePreferences_MultipleGenres_AllTracked()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Action", Genre.Action, 4));
            movies.Add(CreateMovie(2, "Comedy", Genre.Comedy, 3));
            movies.Add(CreateMovie(3, "Drama", Genre.Drama, 5));
            var rentals = new List<Rental>
            {
                CreateRental(1, 1),
                CreateRental(1, 2),
                CreateRental(1, 3)
            };

            var prefs = RecommendationService.AnalyzeGenrePreferences(rentals, movies);

            Assert.AreEqual(3, prefs.Count, "Should track all 3 genres.");
            Assert.IsTrue(prefs.ContainsKey(Genre.Action));
            Assert.IsTrue(prefs.ContainsKey(Genre.Comedy));
            Assert.IsTrue(prefs.ContainsKey(Genre.Drama));
        }

        [TestMethod]
        public void AnalyzeGenrePreferences_RepeatGenre_HigherScore()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Action 1", Genre.Action, 4));
            movies.Add(CreateMovie(2, "Action 2", Genre.Action, 3));
            movies.Add(CreateMovie(3, "Comedy", Genre.Comedy, 4));

            var rentals = new List<Rental>
            {
                CreateRental(1, 1, 5),
                CreateRental(1, 2, 5),
                CreateRental(1, 3, 5)
            };

            var prefs = RecommendationService.AnalyzeGenrePreferences(rentals, movies);

            Assert.IsTrue(prefs[Genre.Action] > prefs[Genre.Comedy],
                "Genre rented twice should score higher than once.");
        }

        [TestMethod]
        public void AnalyzeGenrePreferences_RecentRentals_GetRecencyBonus()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Recent", Genre.Action, 4));
            movies.Add(CreateMovie(2, "Old", Genre.Comedy, 4));

            var rentals = new List<Rental>
            {
                CreateRental(1, 1, 1),   // 1 day ago — very recent
                CreateRental(1, 2, 60)   // 60 days ago — no recency bonus
            };

            var prefs = RecommendationService.AnalyzeGenrePreferences(rentals, movies);

            Assert.IsTrue(prefs[Genre.Action] > prefs[Genre.Comedy],
                "Recent rental should get recency bonus.");
        }

        [TestMethod]
        public void AnalyzeGenrePreferences_MovieWithNoGenre_Skipped()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "No Genre Movie", null, 4));
            var rentals = new List<Rental> { CreateRental(1, 1) };

            var prefs = RecommendationService.AnalyzeGenrePreferences(rentals, movies);

            Assert.AreEqual(0, prefs.Count,
                "Movies without genre should not generate preferences.");
        }

        [TestMethod]
        public void AnalyzeGenrePreferences_UnknownMovieId_Skipped()
        {
            var movies = new TestMovieRepository();
            // Movie ID 999 does not exist
            var rentals = new List<Rental> { CreateRental(1, 999) };

            var prefs = RecommendationService.AnalyzeGenrePreferences(rentals, movies);

            Assert.AreEqual(0, prefs.Count,
                "Rentals referencing unknown movies should be skipped.");
        }

        #endregion

        #region Movie Scoring

        [TestMethod]
        public void ScoreMovies_HigherRatedMovies_ScoreHigher()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "5-Star", Genre.Action, 5),
                CreateMovie(2, "3-Star", Genre.Action, 3)
            };

            var rented = new HashSet<int>();
            var prefs = new Dictionary<Genre, double>();

            var results = RecommendationService.ScoreMovies(movies, rented, prefs).ToList();

            Assert.IsTrue(results[0].Score > results[1].Score,
                "5-star movie should score higher than 3-star.");
        }

        [TestMethod]
        public void ScoreMovies_PreferredGenre_ScoresHigher()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "Action Movie", Genre.Action, 3),
                CreateMovie(2, "Comedy Movie", Genre.Comedy, 3)
            };

            var rented = new HashSet<int>();
            var prefs = new Dictionary<Genre, double> { { Genre.Action, 3.0 } };

            var results = RecommendationService.ScoreMovies(movies, rented, prefs).ToList();

            Assert.AreEqual("Action Movie", results[0].Movie.Name,
                "Movie in preferred genre should rank first.");
        }

        [TestMethod]
        public void ScoreMovies_ExcludesRentedMovies()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "Rented", Genre.Action, 5),
                CreateMovie(2, "Not Rented", Genre.Action, 5)
            };

            var rented = new HashSet<int> { 1 };
            var prefs = new Dictionary<Genre, double>();

            var results = RecommendationService.ScoreMovies(movies, rented, prefs).ToList();

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Not Rented", results[0].Movie.Name);
        }

        [TestMethod]
        public void ScoreMovies_FiveStarBonus()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "5-Star", Genre.Drama, 5),
                CreateMovie(2, "4-Star", Genre.Drama, 4)
            };

            var rented = new HashSet<int>();
            var prefs = new Dictionary<Genre, double>();

            var results = RecommendationService.ScoreMovies(movies, rented, prefs).ToList();

            // 5-star gets rating(5) + bonus(1) = 6; 4-star gets rating(4) = 4
            Assert.IsTrue(results[0].Score > results[1].Score + 1,
                "5-star movie should get extra bonus.");
        }

        [TestMethod]
        public void ScoreMovies_Reason_PreferredAndHighlyRated()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "Action Hit", Genre.Action, 5)
            };

            var rented = new HashSet<int>();
            var prefs = new Dictionary<Genre, double> { { Genre.Action, 2.0 } };

            var results = RecommendationService.ScoreMovies(movies, rented, prefs).ToList();

            Assert.IsTrue(results[0].Reason.Contains("Action"),
                "Reason should mention the genre.");
            Assert.IsTrue(results[0].Reason.Contains("rated"),
                "Reason should mention the rating.");
        }

        [TestMethod]
        public void ScoreMovies_Reason_OnlyPreferredGenre()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "Action B", Genre.Action, 2)
            };

            var rented = new HashSet<int>();
            var prefs = new Dictionary<Genre, double> { { Genre.Action, 2.0 } };

            var results = RecommendationService.ScoreMovies(movies, rented, prefs).ToList();

            Assert.IsTrue(results[0].Reason.Contains("interest"),
                "Reason should indicate genre-based suggestion for lower-rated films.");
        }

        [TestMethod]
        public void ScoreMovies_Reason_OnlyHighlyRated()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "Great Comedy", Genre.Comedy, 5)
            };

            var rented = new HashSet<int>();
            var prefs = new Dictionary<Genre, double>(); // No comedy preference

            var results = RecommendationService.ScoreMovies(movies, rented, prefs).ToList();

            Assert.IsTrue(results[0].Reason.Contains("rated") || results[0].Reason.Contains("Highly"),
                "Reason should mention high rating for non-preferred genre.");
        }

        [TestMethod]
        public void ScoreMovies_Reason_ExploreNewGenre()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "Low Rated Unknown", Genre.Horror, 2)
            };

            var rented = new HashSet<int>();
            var prefs = new Dictionary<Genre, double>(); // No horror preference

            var results = RecommendationService.ScoreMovies(movies, rented, prefs).ToList();

            Assert.IsTrue(results[0].Reason.Contains("different"),
                "Reason should suggest exploring for low-rated non-preferred movies.");
        }

        [TestMethod]
        public void ScoreMovies_NoMovies_ReturnsEmpty()
        {
            var results = RecommendationService.ScoreMovies(
                new List<Movie>(), new HashSet<int>(), new Dictionary<Genre, double>()).ToList();

            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void ScoreMovies_AllRented_ReturnsEmpty()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "A", Genre.Action, 5),
                CreateMovie(2, "B", Genre.Drama, 4)
            };

            var results = RecommendationService.ScoreMovies(
                movies, new HashSet<int> { 1, 2 }, new Dictionary<Genre, double>()).ToList();

            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void ScoreMovies_MovieWithNullRating_GetsZeroRatingScore()
        {
            var movies = new List<Movie>
            {
                CreateMovie(1, "No Rating", Genre.Action, null),
                CreateMovie(2, "Rated", Genre.Action, 3)
            };

            var rented = new HashSet<int>();
            var prefs = new Dictionary<Genre, double>();

            var results = RecommendationService.ScoreMovies(movies, rented, prefs).ToList();

            Assert.IsTrue(results[0].Score >= results[1].Score ||
                           results[0].Movie.Name == "Rated",
                "Movie with no rating should have lower score than rated movie.");
        }

        #endregion

        #region Full Integration Tests

        [TestMethod]
        public void GetRecommendations_FullScenario_PreferredGenreRanksHigher()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Action 1", Genre.Action, 4));  // Rented
            movies.Add(CreateMovie(2, "Action 2", Genre.Action, 4));  // Should rank high
            movies.Add(CreateMovie(3, "Comedy 1", Genre.Comedy, 4));  // Lower rank
            movies.Add(CreateMovie(4, "Drama 1", Genre.Drama, 5));    // Good rating but not preferred

            var rentals = new TestRentalRepository();
            rentals.Add(CreateRental(1, 1, 3)); // Customer 1 rented Action 1

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            Assert.AreEqual(1, result.TotalRentals);
            Assert.AreEqual(3, result.Recommendations.Count,
                "Should recommend 3 unwatched movies.");
            Assert.IsFalse(result.Recommendations.Any(r => r.Movie.Id == 1),
                "Should not include already-rented movie.");

            // Action 2 should rank high because of genre preference
            var actionRec = result.Recommendations.FirstOrDefault(r => r.Movie.Id == 2);
            Assert.IsNotNull(actionRec, "Action 2 should be in recommendations.");
        }

        [TestMethod]
        public void GetRecommendations_MultipleGenreRentals_MostRentedGenreWins()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Action A", Genre.Action, 3));
            movies.Add(CreateMovie(2, "Action B", Genre.Action, 3));
            movies.Add(CreateMovie(3, "Comedy A", Genre.Comedy, 3));
            movies.Add(CreateMovie(4, "Action C", Genre.Action, 3));  // Suggest this
            movies.Add(CreateMovie(5, "Comedy B", Genre.Comedy, 3));  // And this

            var rentals = new TestRentalRepository();
            rentals.Add(CreateRental(1, 1, 5));  // Action
            rentals.Add(CreateRental(1, 2, 5));  // Action
            rentals.Add(CreateRental(1, 3, 5));  // Comedy

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            var actionRec = result.Recommendations.FirstOrDefault(r => r.Movie.Id == 4);
            var comedyRec = result.Recommendations.FirstOrDefault(r => r.Movie.Id == 5);

            Assert.IsNotNull(actionRec);
            Assert.IsNotNull(comedyRec);
            Assert.IsTrue(actionRec.Score > comedyRec.Score,
                "Action should score higher (rented 2x vs 1x).");
        }

        [TestMethod]
        public void GetRecommendations_AllMoviesRented_EmptyRecommendations()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Only Movie", Genre.Action, 5));

            var rentals = new TestRentalRepository();
            rentals.Add(CreateRental(1, 1));

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            Assert.AreEqual(0, result.Recommendations.Count);
            Assert.AreEqual(0, result.TotalAvailableMovies);
        }

        [TestMethod]
        public void GetRecommendations_NoMovies_ReturnsEmptyResult()
        {
            var movies = new TestMovieRepository();
            var rentals = new TestRentalRepository();

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            Assert.AreEqual(0, result.Recommendations.Count);
            Assert.AreEqual(0, result.TotalRentals);
            Assert.AreEqual(0, result.GenrePreferences.Count);
        }

        [TestMethod]
        public void GetRecommendations_GenrePreferences_SortedByScore()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Action 1", Genre.Action, 4));
            movies.Add(CreateMovie(2, "Action 2", Genre.Action, 4));
            movies.Add(CreateMovie(3, "Comedy 1", Genre.Comedy, 4));

            var rentals = new TestRentalRepository();
            rentals.Add(CreateRental(1, 1, 5));
            rentals.Add(CreateRental(1, 2, 5));
            rentals.Add(CreateRental(1, 3, 5));

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            Assert.AreEqual(2, result.GenrePreferences.Count);
            Assert.AreEqual(Genre.Action, result.GenrePreferences[0].Genre,
                "Most-rented genre should appear first.");
            Assert.IsTrue(result.GenrePreferences[0].Score >= result.GenrePreferences[1].Score,
                "Preferences should be sorted by score descending.");
        }

        [TestMethod]
        public void GetRecommendations_GenrePreference_HasCorrectRentalCount()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Action 1", Genre.Action, 4));
            movies.Add(CreateMovie(2, "Action 2", Genre.Action, 4));
            movies.Add(CreateMovie(3, "Comedy", Genre.Comedy, 3));

            var rentals = new TestRentalRepository();
            rentals.Add(CreateRental(1, 1, 5));
            rentals.Add(CreateRental(1, 2, 5));
            rentals.Add(CreateRental(1, 3, 5));

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1);

            var actionPref = result.GenrePreferences.First(g => g.Genre == Genre.Action);
            Assert.AreEqual(2, actionPref.RentalCount,
                "Action rental count should be 2.");

            var comedyPref = result.GenrePreferences.First(g => g.Genre == Genre.Comedy);
            Assert.AreEqual(1, comedyPref.RentalCount,
                "Comedy rental count should be 1.");
        }

        [TestMethod]
        public void GetRecommendations_OtherCustomerRentals_Ignored()
        {
            var movies = new TestMovieRepository();
            movies.Add(CreateMovie(1, "Movie A", Genre.Action, 4));
            movies.Add(CreateMovie(2, "Movie B", Genre.Comedy, 4));

            var rentals = new TestRentalRepository();
            rentals.Add(CreateRental(2, 1, 5)); // Customer 2 rented movie 1

            var service = new RecommendationService(movies, rentals);
            var result = service.GetRecommendations(1); // Customer 1

            Assert.AreEqual(0, result.TotalRentals,
                "Customer 1 should have 0 rentals.");
            Assert.AreEqual(2, result.Recommendations.Count,
                "Both movies should be recommended to customer 1.");
        }

        #endregion

        #region ViewModel Tests

        [TestMethod]
        public void RecommendationViewModel_Defaults()
        {
            var vm = new RecommendationViewModel();

            Assert.IsNotNull(vm.Customers);
            Assert.AreEqual(0, vm.Customers.Count);
            Assert.IsNull(vm.SelectedCustomerId);
            Assert.IsNull(vm.SelectedCustomerName);
            Assert.IsNull(vm.Result);
        }

        [TestMethod]
        public void RecommendationViewModel_CanBePopulated()
        {
            var vm = new RecommendationViewModel
            {
                SelectedCustomerId = 1,
                SelectedCustomerName = "John",
                Result = new RecommendationResult
                {
                    CustomerId = 1,
                    TotalRentals = 5,
                    TotalAvailableMovies = 10
                }
            };

            Assert.AreEqual(1, vm.SelectedCustomerId);
            Assert.AreEqual("John", vm.SelectedCustomerName);
            Assert.AreEqual(5, vm.Result.TotalRentals);
        }

        #endregion

        #region RecommendationResult Tests

        [TestMethod]
        public void RecommendationResult_Defaults()
        {
            var result = new RecommendationResult();

            Assert.AreEqual(0, result.CustomerId);
            Assert.AreEqual(0, result.TotalRentals);
            Assert.IsNotNull(result.GenrePreferences);
            Assert.IsNotNull(result.Recommendations);
            Assert.AreEqual(0, result.TotalAvailableMovies);
        }

        [TestMethod]
        public void GenrePreference_Properties()
        {
            var pref = new GenrePreference
            {
                Genre = Genre.SciFi,
                RentalCount = 3,
                Score = 4.5
            };

            Assert.AreEqual(Genre.SciFi, pref.Genre);
            Assert.AreEqual(3, pref.RentalCount);
            Assert.AreEqual(4.5, pref.Score);
        }

        [TestMethod]
        public void MovieRecommendation_Properties()
        {
            var rec = new MovieRecommendation
            {
                Movie = CreateMovie(1, "Test", Genre.Action, 5),
                Score = 9.5,
                Reason = "Great match!"
            };

            Assert.AreEqual("Test", rec.Movie.Name);
            Assert.AreEqual(9.5, rec.Score);
            Assert.AreEqual("Great match!", rec.Reason);
        }

        #endregion
    }
}
