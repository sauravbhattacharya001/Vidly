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
    public class MovieSimilarityServiceTests
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
            public RentalStats GetStats() => new RentalStats();
        }

        private static Movie CreateMovie(int id, string name, Genre? genre = null, int? rating = null)
            => new Movie { Id = id, Name = name, Genre = genre, Rating = rating };

        private static Rental CreateRental(int id, int customerId, int movieId)
            => new Rental
            {
                Id = id,
                CustomerId = customerId,
                MovieId = movieId,
                Status = RentalStatus.Returned,
                RentalDate = DateTime.Today.AddDays(-10),
                DueDate = DateTime.Today.AddDays(-3),
                DailyRate = 1.99m
            };

        private MovieSimilarityService CreateService(
            TestMovieRepository movieRepo, TestRentalRepository rentalRepo)
            => new MovieSimilarityService(movieRepo, rentalRepo);

        #endregion

        // ========== Constructor ==========

        [TestMethod]
        public void Constructor_ThrowsOnNullMovieRepository()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new MovieSimilarityService(null, new TestRentalRepository()));
        }

        [TestMethod]
        public void Constructor_ThrowsOnNullRentalRepository()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new MovieSimilarityService(new TestMovieRepository(), null));
        }

        // ========== FindSimilar ==========

        [TestMethod]
        public void FindSimilar_ThrowsOnMaxResultsLessThan1()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Movie1", Genre.Action, 5));
            var service = CreateService(movieRepo, new TestRentalRepository());

            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => service.FindSimilar(1, maxResults: 0));
        }

        [TestMethod]
        public void FindSimilar_ThrowsOnInvalidMovieId()
        {
            var service = CreateService(new TestMovieRepository(), new TestRentalRepository());
            Assert.ThrowsException<ArgumentException>(
                () => service.FindSimilar(999));
        }

        [TestMethod]
        public void FindSimilar_ReturnsEmptyForMovieWithNoSimilarities()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Movie1", Genre.Action, 5));
            movieRepo.Add(CreateMovie(2, "Movie2", Genre.Comedy, 1));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.FindSimilar(1);

            // Different genre + max rating diff (5 vs 1, diff=4 => ratingScore=0) + no co-rentals
            // Genre: 0, Rating: 1 - 4/4 = 0, CoRental: 0 => total = 0
            Assert.AreEqual(0, result.SimilarMovies.Count);
        }

        [TestMethod]
        public void FindSimilar_ReturnsSimilarMoviesSortedByScore()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", Genre.Action, 4));
            movieRepo.Add(CreateMovie(2, "Same Genre Same Rating", Genre.Action, 4));
            movieRepo.Add(CreateMovie(3, "Same Genre Diff Rating", Genre.Action, 1));
            movieRepo.Add(CreateMovie(4, "Diff Genre Same Rating", Genre.Comedy, 4));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.FindSimilar(1);

            Assert.IsTrue(result.SimilarMovies.Count > 0);
            // First should be same genre + same rating (highest score)
            Assert.AreEqual(2, result.SimilarMovies[0].Movie.Id);
        }

        [TestMethod]
        public void FindSimilar_RespectsMaxResultsLimit()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", Genre.Action, 3));
            for (int i = 2; i <= 20; i++)
                movieRepo.Add(CreateMovie(i, $"Movie{i}", Genre.Action, 3));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.FindSimilar(1, maxResults: 5);

            Assert.AreEqual(5, result.SimilarMovies.Count);
        }

        [TestMethod]
        public void FindSimilar_SameGenreIncreasesScore()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", Genre.Action));
            movieRepo.Add(CreateMovie(2, "SameGenre", Genre.Action));
            movieRepo.Add(CreateMovie(3, "DiffGenre", Genre.Comedy));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.FindSimilar(1);

            var sameGenreResult = result.SimilarMovies.FirstOrDefault(s => s.Movie.Id == 2);
            Assert.IsNotNull(sameGenreResult);
            Assert.AreEqual(1.0, sameGenreResult.GenreScore);
        }

        [TestMethod]
        public void FindSimilar_SimilarRatingIncreasesScore()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", rating: 4));
            movieRepo.Add(CreateMovie(2, "SimilarRating", rating: 4));
            movieRepo.Add(CreateMovie(3, "DiffRating", rating: 1));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.FindSimilar(1);

            var simRating = result.SimilarMovies.FirstOrDefault(s => s.Movie.Id == 2);
            Assert.IsNotNull(simRating);
            Assert.AreEqual(1.0, simRating.RatingScore);
        }

        [TestMethod]
        public void FindSimilar_CoRentalPatternIncreasesScore()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source"));
            movieRepo.Add(CreateMovie(2, "CoRented"));
            movieRepo.Add(CreateMovie(3, "NotCoRented"));

            var rentalRepo = new TestRentalRepository();
            // Customer 100 rented both movie 1 and movie 2
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var result = service.FindSimilar(1);

            var coRented = result.SimilarMovies.FirstOrDefault(s => s.Movie.Id == 2);
            Assert.IsNotNull(coRented);
            Assert.IsTrue(coRented.CoRentalScore > 0);
        }

        [TestMethod]
        public void FindSimilar_CombinesAllThreeSignals()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", Genre.Action, 4));
            movieRepo.Add(CreateMovie(2, "Perfect Match", Genre.Action, 4));

            var rentalRepo = new TestRentalRepository();
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var result = service.FindSimilar(1);

            var match = result.SimilarMovies.First();
            Assert.AreEqual(1.0, match.GenreScore);
            Assert.AreEqual(1.0, match.RatingScore);
            Assert.AreEqual(1.0, match.CoRentalScore);
            Assert.AreEqual(1.0, match.TotalScore);
        }

        [TestMethod]
        public void FindSimilar_IncludesReasons()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", Genre.Action, 4));
            movieRepo.Add(CreateMovie(2, "Match", Genre.Action, 4));

            var rentalRepo = new TestRentalRepository();
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var result = service.FindSimilar(1);

            var match = result.SimilarMovies.First();
            Assert.IsTrue(match.Reasons.Count > 0);
            Assert.IsTrue(match.Reasons.Any(r => r.Contains("genre")));
        }

        [TestMethod]
        public void FindSimilar_IncludesSignalsMetadata()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", Genre.Action, 3));
            movieRepo.Add(CreateMovie(2, "Other", Genre.Action, 3));

            var rentalRepo = new TestRentalRepository();
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var result = service.FindSimilar(1);

            Assert.IsNotNull(result.Signals);
            Assert.AreEqual(1, result.Signals.UniqueRenters);
            Assert.AreEqual(1, result.Signals.CoRentedMovieCount);
            Assert.IsTrue(result.Signals.HasGenre);
            Assert.IsTrue(result.Signals.HasRating);
        }

        [TestMethod]
        public void FindSimilar_MoviesWithoutGenreStillGetRatingAndCoRentalScores()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", genre: null, rating: 3));
            movieRepo.Add(CreateMovie(2, "Other", genre: null, rating: 3));

            var rentalRepo = new TestRentalRepository();
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var result = service.FindSimilar(1);

            Assert.IsTrue(result.SimilarMovies.Count > 0);
            var match = result.SimilarMovies.First();
            Assert.AreEqual(0.0, match.GenreScore);
            Assert.IsTrue(match.RatingScore > 0 || match.CoRentalScore > 0);
        }

        [TestMethod]
        public void FindSimilar_MoviesWithoutRatingStillGetGenreAndCoRentalScores()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", Genre.Action, rating: null));
            movieRepo.Add(CreateMovie(2, "Other", Genre.Action, rating: null));

            var rentalRepo = new TestRentalRepository();
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var result = service.FindSimilar(1);

            Assert.IsTrue(result.SimilarMovies.Count > 0);
            var match = result.SimilarMovies.First();
            Assert.AreEqual(0.0, match.RatingScore);
            Assert.IsTrue(match.GenreScore > 0 || match.CoRentalScore > 0);
        }

        [TestMethod]
        public void FindSimilar_ExcludesSourceMovieFromResults()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Source", Genre.Action, 5));
            movieRepo.Add(CreateMovie(2, "Other", Genre.Action, 5));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.FindSimilar(1);

            Assert.IsTrue(result.SimilarMovies.All(s => s.Movie.Id != 1));
        }

        // ========== Compare ==========

        [TestMethod]
        public void Compare_ThrowsOnInvalidMovie1()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(2, "Movie2"));
            var service = CreateService(movieRepo, new TestRentalRepository());

            Assert.ThrowsException<ArgumentException>(() => service.Compare(999, 2));
        }

        [TestMethod]
        public void Compare_ThrowsOnInvalidMovie2()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "Movie1"));
            var service = CreateService(movieRepo, new TestRentalRepository());

            Assert.ThrowsException<ArgumentException>(() => service.Compare(1, 999));
        }

        [TestMethod]
        public void Compare_SameGenreGivesGenreScore1()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", Genre.Action, 3));
            movieRepo.Add(CreateMovie(2, "M2", Genre.Action, 3));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.Compare(1, 2);

            Assert.AreEqual(1.0, result.GenreScore);
            Assert.IsTrue(result.SameGenre);
        }

        [TestMethod]
        public void Compare_DifferentGenreGivesGenreScore0()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", Genre.Action));
            movieRepo.Add(CreateMovie(2, "M2", Genre.Comedy));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.Compare(1, 2);

            Assert.AreEqual(0.0, result.GenreScore);
            Assert.IsFalse(result.SameGenre);
        }

        [TestMethod]
        public void Compare_SameRatingGivesRatingScore1()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", rating: 4));
            movieRepo.Add(CreateMovie(2, "M2", rating: 4));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.Compare(1, 2);

            Assert.AreEqual(1.0, result.RatingScore);
        }

        [TestMethod]
        public void Compare_AdjacentRatingGivesRatingScore075()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", rating: 4));
            movieRepo.Add(CreateMovie(2, "M2", rating: 3));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.Compare(1, 2);

            Assert.AreEqual(0.75, result.RatingScore);
        }

        [TestMethod]
        public void Compare_MaxRatingDiffGivesRatingScore0()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", rating: 1));
            movieRepo.Add(CreateMovie(2, "M2", rating: 5));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.Compare(1, 2);

            Assert.AreEqual(0.0, result.RatingScore);
        }

        [TestMethod]
        public void Compare_SharedRentersCountIsCorrect()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1"));
            movieRepo.Add(CreateMovie(2, "M2"));

            var rentalRepo = new TestRentalRepository();
            // Customers 100 and 101 rented both; customer 102 rented only movie 1
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));
            rentalRepo.Add(CreateRental(3, 101, 1));
            rentalRepo.Add(CreateRental(4, 101, 2));
            rentalRepo.Add(CreateRental(5, 102, 1));

            var service = CreateService(movieRepo, rentalRepo);
            var result = service.Compare(1, 2);

            Assert.AreEqual(2, result.SharedRenters);
            Assert.AreEqual(3, result.Movie1TotalRenters);
            Assert.AreEqual(2, result.Movie2TotalRenters);
        }

        [TestMethod]
        public void Compare_NoSharedRentersGivesZeroCoRentalScore()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1"));
            movieRepo.Add(CreateMovie(2, "M2"));

            var rentalRepo = new TestRentalRepository();
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 200, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var result = service.Compare(1, 2);

            Assert.AreEqual(0.0, result.CoRentalScore);
            Assert.AreEqual(0, result.SharedRenters);
        }

        [TestMethod]
        public void Compare_VerdictReflectsScoreLevel()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", Genre.Action, 4));
            movieRepo.Add(CreateMovie(2, "M2", Genre.Action, 4));

            var rentalRepo = new TestRentalRepository();
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var result = service.Compare(1, 2);

            // Score = 0.35*1 + 0.25*1 + 0.40*1 = 1.0 => Highly Similar
            Assert.AreEqual("Highly Similar", result.Verdict);
        }

        [TestMethod]
        public void Compare_ComparisonIsSymmetricInScore()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", Genre.Action, 3));
            movieRepo.Add(CreateMovie(2, "M2", Genre.Action, 4));

            var rentalRepo = new TestRentalRepository();
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var r12 = service.Compare(1, 2);
            var r21 = service.Compare(2, 1);

            Assert.AreEqual(r12.TotalScore, r21.TotalScore);
            Assert.AreEqual(r12.GenreScore, r21.GenreScore);
            Assert.AreEqual(r12.RatingScore, r21.RatingScore);
        }

        [TestMethod]
        public void Compare_NullGenresHandled()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", genre: null));
            movieRepo.Add(CreateMovie(2, "M2", genre: null));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.Compare(1, 2);

            Assert.AreEqual(0.0, result.GenreScore);
            Assert.IsFalse(result.SameGenre);
        }

        [TestMethod]
        public void Compare_NullRatingsHandled()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", rating: null));
            movieRepo.Add(CreateMovie(2, "M2", rating: null));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.Compare(1, 2);

            Assert.AreEqual(0.0, result.RatingScore);
        }

        [TestMethod]
        public void Compare_RatingDifferenceIsNullWhenRatingsMissing()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", rating: null));
            movieRepo.Add(CreateMovie(2, "M2", rating: 3));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var result = service.Compare(1, 2);

            Assert.IsNull(result.RatingDifference);
        }

        // ========== CalculateGenreScore ==========

        [TestMethod]
        public void CalculateGenreScore_SameGenreReturns1()
        {
            var score = MovieSimilarityService.CalculateGenreScore(
                CreateMovie(1, "A", Genre.Action), CreateMovie(2, "B", Genre.Action));
            Assert.AreEqual(1.0, score);
        }

        [TestMethod]
        public void CalculateGenreScore_DifferentGenreReturns0()
        {
            var score = MovieSimilarityService.CalculateGenreScore(
                CreateMovie(1, "A", Genre.Action), CreateMovie(2, "B", Genre.Comedy));
            Assert.AreEqual(0.0, score);
        }

        [TestMethod]
        public void CalculateGenreScore_NullSourceGenreReturns0()
        {
            var score = MovieSimilarityService.CalculateGenreScore(
                CreateMovie(1, "A", genre: null), CreateMovie(2, "B", Genre.Action));
            Assert.AreEqual(0.0, score);
        }

        [TestMethod]
        public void CalculateGenreScore_NullCandidateGenreReturns0()
        {
            var score = MovieSimilarityService.CalculateGenreScore(
                CreateMovie(1, "A", Genre.Action), CreateMovie(2, "B", genre: null));
            Assert.AreEqual(0.0, score);
        }

        [TestMethod]
        public void CalculateGenreScore_BothNullReturns0()
        {
            var score = MovieSimilarityService.CalculateGenreScore(
                CreateMovie(1, "A", genre: null), CreateMovie(2, "B", genre: null));
            Assert.AreEqual(0.0, score);
        }

        // ========== CalculateRatingScore ==========

        [TestMethod]
        public void CalculateRatingScore_SameRatingReturns1()
        {
            var score = MovieSimilarityService.CalculateRatingScore(
                CreateMovie(1, "A", rating: 3), CreateMovie(2, "B", rating: 3));
            Assert.AreEqual(1.0, score);
        }

        [TestMethod]
        public void CalculateRatingScore_Diff1Returns075()
        {
            var score = MovieSimilarityService.CalculateRatingScore(
                CreateMovie(1, "A", rating: 3), CreateMovie(2, "B", rating: 4));
            Assert.AreEqual(0.75, score);
        }

        [TestMethod]
        public void CalculateRatingScore_Diff2Returns05()
        {
            var score = MovieSimilarityService.CalculateRatingScore(
                CreateMovie(1, "A", rating: 1), CreateMovie(2, "B", rating: 3));
            Assert.AreEqual(0.5, score);
        }

        [TestMethod]
        public void CalculateRatingScore_Diff3Returns025()
        {
            var score = MovieSimilarityService.CalculateRatingScore(
                CreateMovie(1, "A", rating: 1), CreateMovie(2, "B", rating: 4));
            Assert.AreEqual(0.25, score);
        }

        [TestMethod]
        public void CalculateRatingScore_Diff4Returns0()
        {
            var score = MovieSimilarityService.CalculateRatingScore(
                CreateMovie(1, "A", rating: 1), CreateMovie(2, "B", rating: 5));
            Assert.AreEqual(0.0, score);
        }

        [TestMethod]
        public void CalculateRatingScore_NullSourceReturns0()
        {
            var score = MovieSimilarityService.CalculateRatingScore(
                CreateMovie(1, "A", rating: null), CreateMovie(2, "B", rating: 3));
            Assert.AreEqual(0.0, score);
        }

        [TestMethod]
        public void CalculateRatingScore_NullCandidateReturns0()
        {
            var score = MovieSimilarityService.CalculateRatingScore(
                CreateMovie(1, "A", rating: 3), CreateMovie(2, "B", rating: null));
            Assert.AreEqual(0.0, score);
        }

        // ========== BuildCoRentalFromIndex ==========

        [TestMethod]
        public void BuildCoRentalFromIndex_NoRentalsReturnsEmpty()
        {
            var rentals = new List<Rental>().AsReadOnly();
            var customerMovies = MovieSimilarityService.BuildCustomerMovieIndex(rentals);
            var movieRenters = MovieSimilarityService.BuildMovieRentersIndex(customerMovies);
            var result = MovieSimilarityService.BuildCoRentalFromIndex(
                1, customerMovies, movieRenters);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void BuildCoRentalFromIndex_NoRentersForMovieReturnsEmpty()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 100, 2) // Rental for a different movie
            }.AsReadOnly();

            var customerMovies = MovieSimilarityService.BuildCustomerMovieIndex(rentals);
            var movieRenters = MovieSimilarityService.BuildMovieRentersIndex(customerMovies);
            var result = MovieSimilarityService.BuildCoRentalFromIndex(1, customerMovies, movieRenters);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void BuildCoRentalFromIndex_SingleSharedCustomerScores1()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 100, 1), // Customer 100 rented movie 1
                CreateRental(2, 100, 2)  // Customer 100 also rented movie 2
            }.AsReadOnly();

            var customerMovies = MovieSimilarityService.BuildCustomerMovieIndex(rentals);
            var movieRenters = MovieSimilarityService.BuildMovieRentersIndex(customerMovies);
            var result = MovieSimilarityService.BuildCoRentalFromIndex(1, customerMovies, movieRenters);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(1.0, result[2]);
        }

        [TestMethod]
        public void BuildCoRentalFromIndex_NormalizesByMaxCount()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 100, 1),
                CreateRental(2, 101, 1),
                CreateRental(3, 100, 2), // movie 2: 2 shared customers
                CreateRental(4, 101, 2),
                CreateRental(5, 100, 3)  // movie 3: 1 shared customer
            }.AsReadOnly();

            var customerMovies = MovieSimilarityService.BuildCustomerMovieIndex(rentals);
            var movieRenters = MovieSimilarityService.BuildMovieRentersIndex(customerMovies);
            var result = MovieSimilarityService.BuildCoRentalFromIndex(1, customerMovies, movieRenters);

            Assert.AreEqual(1.0, result[2]); // max = 2/2
            Assert.AreEqual(0.5, result[3]); // 1/2
        }

        [TestMethod]
        public void BuildCoRentalFromIndex_ExcludesSourceMovieFromResults()
        {
            var rentals = new List<Rental>
            {
                CreateRental(1, 100, 1),
                CreateRental(2, 100, 2)
            }.AsReadOnly();

            var customerMovies = MovieSimilarityService.BuildCustomerMovieIndex(rentals);
            var movieRenters = MovieSimilarityService.BuildMovieRentersIndex(customerMovies);
            var result = MovieSimilarityService.BuildCoRentalFromIndex(1, customerMovies, movieRenters);

            Assert.IsFalse(result.ContainsKey(1));
        }

        // ========== GetSimilarityMatrix ==========

        [TestMethod]
        public void GetSimilarityMatrix_ReturnsNxNMatrix()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", Genre.Action, 3));
            movieRepo.Add(CreateMovie(2, "M2", Genre.Action, 3));
            movieRepo.Add(CreateMovie(3, "M3", Genre.Comedy, 1));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var matrix = service.GetSimilarityMatrix();

            Assert.AreEqual(3, matrix.Movies.Count);
            Assert.AreEqual(3, matrix.Scores.GetLength(0));
            Assert.AreEqual(3, matrix.Scores.GetLength(1));
        }

        [TestMethod]
        public void GetSimilarityMatrix_DiagonalIs1()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", Genre.Action, 3));
            movieRepo.Add(CreateMovie(2, "M2", Genre.Comedy, 1));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var matrix = service.GetSimilarityMatrix();

            Assert.AreEqual(1.0, matrix.Scores[0, 0]);
            Assert.AreEqual(1.0, matrix.Scores[1, 1]);
        }

        [TestMethod]
        public void GetSimilarityMatrix_MatrixIsSymmetric()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "M1", Genre.Action, 4));
            movieRepo.Add(CreateMovie(2, "M2", Genre.Action, 3));
            movieRepo.Add(CreateMovie(3, "M3", Genre.Comedy, 5));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var matrix = service.GetSimilarityMatrix();

            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    Assert.AreEqual(matrix.Scores[i, j], matrix.Scores[j, i],
                        $"Asymmetric at [{i},{j}]");
        }

        [TestMethod]
        public void GetSimilarityMatrix_ClustersMoviesWithHighSimilarity()
        {
            var movieRepo = new TestMovieRepository();
            // Two action movies with same rating — should cluster
            movieRepo.Add(CreateMovie(1, "ActionA", Genre.Action, 4));
            movieRepo.Add(CreateMovie(2, "ActionB", Genre.Action, 4));
            // One outlier
            movieRepo.Add(CreateMovie(3, "DocLow", Genre.Documentary, 1));

            var rentalRepo = new TestRentalRepository();
            // Make the action movies co-rented to boost their similarity above threshold
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var matrix = service.GetSimilarityMatrix();

            Assert.IsTrue(matrix.Clusters.Count >= 1);
            var actionCluster = matrix.Clusters.FirstOrDefault(
                c => c.Movies.Any(m => m.Id == 1) && c.Movies.Any(m => m.Id == 2));
            Assert.IsNotNull(actionCluster);
        }

        [TestMethod]
        public void GetSimilarityMatrix_SingleMovieMatrixHasNoClusters()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "OnlyOne", Genre.Action, 5));
            var service = CreateService(movieRepo, new TestRentalRepository());

            var matrix = service.GetSimilarityMatrix();

            Assert.AreEqual(0, matrix.Clusters.Count);
        }

        [TestMethod]
        public void GetSimilarityMatrix_ClusterHasDominantGenre()
        {
            var movieRepo = new TestMovieRepository();
            movieRepo.Add(CreateMovie(1, "A1", Genre.Action, 5));
            movieRepo.Add(CreateMovie(2, "A2", Genre.Action, 5));

            var rentalRepo = new TestRentalRepository();
            rentalRepo.Add(CreateRental(1, 100, 1));
            rentalRepo.Add(CreateRental(2, 100, 2));

            var service = CreateService(movieRepo, rentalRepo);
            var matrix = service.GetSimilarityMatrix();

            Assert.IsTrue(matrix.Clusters.Count >= 1);
            Assert.AreEqual("Action", matrix.Clusters[0].DominantGenre);
        }

        // ========== BuildReasons ==========

        [TestMethod]
        public void BuildReasons_IncludesGenreReasonForSameGenre()
        {
            var source = CreateMovie(1, "S", Genre.Action);
            var candidate = CreateMovie(2, "C", Genre.Action);
            var reasons = MovieSimilarityService.BuildReasons(source, candidate, 1.0, 0, 0);
            Assert.IsTrue(reasons.Any(r => r.Contains("genre")));
        }

        [TestMethod]
        public void BuildReasons_IncludesRatingReasonForHighSimilarity()
        {
            var source = CreateMovie(1, "S", rating: 4);
            var candidate = CreateMovie(2, "C", rating: 4);
            var reasons = MovieSimilarityService.BuildReasons(source, candidate, 0, 1.0, 0);
            Assert.IsTrue(reasons.Any(r => r.Contains("rating")));
        }

        [TestMethod]
        public void BuildReasons_IncludesCoRentalReasonForOverlap()
        {
            var source = CreateMovie(1, "S");
            var candidate = CreateMovie(2, "C");
            var reasons = MovieSimilarityService.BuildReasons(source, candidate, 0, 0, 0.5);
            Assert.IsTrue(reasons.Any(r => r.Contains("rented")));
        }

        [TestMethod]
        public void BuildReasons_EmptyReasonsWhenNoSignalsMatch()
        {
            var source = CreateMovie(1, "S");
            var candidate = CreateMovie(2, "C");
            var reasons = MovieSimilarityService.BuildReasons(source, candidate, 0, 0, 0);
            Assert.AreEqual(0, reasons.Count);
        }

        // ========== GetVerdict ==========

        [TestMethod]
        public void GetVerdict_HighlySimilarForScoreGte08()
        {
            Assert.AreEqual("Highly Similar", MovieSimilarityService.GetVerdict(0.8));
            Assert.AreEqual("Highly Similar", MovieSimilarityService.GetVerdict(1.0));
        }

        [TestMethod]
        public void GetVerdict_VerySimilarForScoreGte06()
        {
            Assert.AreEqual("Very Similar", MovieSimilarityService.GetVerdict(0.6));
            Assert.AreEqual("Very Similar", MovieSimilarityService.GetVerdict(0.79));
        }

        [TestMethod]
        public void GetVerdict_ModeratelySimilarForScoreGte04()
        {
            Assert.AreEqual("Moderately Similar", MovieSimilarityService.GetVerdict(0.4));
            Assert.AreEqual("Moderately Similar", MovieSimilarityService.GetVerdict(0.59));
        }

        [TestMethod]
        public void GetVerdict_SomewhatSimilarForScoreGte02()
        {
            Assert.AreEqual("Somewhat Similar", MovieSimilarityService.GetVerdict(0.2));
            Assert.AreEqual("Somewhat Similar", MovieSimilarityService.GetVerdict(0.39));
        }

        [TestMethod]
        public void GetVerdict_SlightlySimilarForScoreGt0()
        {
            Assert.AreEqual("Slightly Similar", MovieSimilarityService.GetVerdict(0.01));
            Assert.AreEqual("Slightly Similar", MovieSimilarityService.GetVerdict(0.19));
        }

        [TestMethod]
        public void GetVerdict_NotSimilarForScore0()
        {
            Assert.AreEqual("Not Similar", MovieSimilarityService.GetVerdict(0.0));
        }
    }
}
