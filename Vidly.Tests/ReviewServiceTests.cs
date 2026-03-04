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
    public class ReviewServiceTests
    {
        #region Test Repositories

        private class StubReviewRepository : IReviewRepository
        {
            private readonly Dictionary<int, Review> _reviews = new Dictionary<int, Review>();
            private int _nextId = 1;

            public void Add(Review review)
            {
                review.Id = _nextId++;
                _reviews[review.Id] = review;
            }

            public Review GetById(int id) =>
                _reviews.TryGetValue(id, out var r) ? r : null;

            public IReadOnlyList<Review> GetAll() =>
                _reviews.Values.OrderByDescending(r => r.CreatedDate).ToList();

            public void Update(Review review) { _reviews[review.Id] = review; }
            public void Remove(int id) { _reviews.Remove(id); }
            public int Count => _reviews.Count;

            public IReadOnlyList<Review> GetByMovie(int movieId) =>
                _reviews.Values.Where(r => r.MovieId == movieId)
                    .OrderByDescending(r => r.CreatedDate).ToList();

            public IReadOnlyList<Review> GetByCustomer(int customerId) =>
                _reviews.Values.Where(r => r.CustomerId == customerId)
                    .OrderByDescending(r => r.CreatedDate).ToList();

            public Review GetByCustomerAndMovie(int customerId, int movieId) =>
                _reviews.Values.FirstOrDefault(r => r.CustomerId == customerId && r.MovieId == movieId);

            public bool HasReviewed(int customerId, int movieId) =>
                _reviews.Values.Any(r => r.CustomerId == customerId && r.MovieId == movieId);

            public ReviewStats GetMovieStats(int movieId)
            {
                var movieReviews = _reviews.Values.Where(r => r.MovieId == movieId).ToList();
                return new ReviewStats
                {
                    MovieId = movieId,
                    TotalReviews = movieReviews.Count,
                    AverageStars = movieReviews.Count > 0 ? movieReviews.Average(r => r.Stars) : 0,
                    FiveStarCount = movieReviews.Count(r => r.Stars == 5),
                    FourStarCount = movieReviews.Count(r => r.Stars == 4),
                    ThreeStarCount = movieReviews.Count(r => r.Stars == 3),
                    TwoStarCount = movieReviews.Count(r => r.Stars == 2),
                    OneStarCount = movieReviews.Count(r => r.Stars == 1),
                };
            }

            public IReadOnlyList<MovieRating> GetTopRatedMovies(int count, int minReviews = 1) =>
                _reviews.Values.GroupBy(r => r.MovieId)
                    .Where(g => g.Count() >= minReviews)
                    .Select(g => new MovieRating
                    {
                        MovieId = g.Key,
                        AverageStars = g.Average(r => r.Stars),
                        ReviewCount = g.Count(),
                    })
                    .OrderByDescending(mr => mr.AverageStars)
                    .Take(count).ToList();

            public IReadOnlyList<Review> Search(string query, int? minStars) =>
                _reviews.Values.ToList();
        }

        private class StubCustomerRepository : ICustomerRepository
        {
            private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
            private int _nextId = 1;

            public void Seed(int id, string name, MembershipType tier = MembershipType.Basic)
            {
                _customers[id] = new Customer { Id = id, Name = name, MembershipType = tier };
            }

            public Customer GetById(int id) =>
                _customers.TryGetValue(id, out var c) ? c : null;

            public IReadOnlyList<Customer> GetAll() => _customers.Values.ToList();
            public void Add(Customer entity) { entity.Id = _nextId++; _customers[entity.Id] = entity; }
            public void Update(Customer entity) { _customers[entity.Id] = entity; }
            public void Remove(int id) { _customers.Remove(id); }
            public int Count => _customers.Count;
        }

        private class StubMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();
            private int _nextId = 1;

            public void Seed(int id, string name, Genre? genre = null)
            {
                _movies[id] = new Movie { Id = id, Name = name, Genre = genre };
            }

            public Movie GetById(int id) =>
                _movies.TryGetValue(id, out var m) ? m : null;

            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList();
            public void Add(Movie entity) { entity.Id = _nextId++; _movies[entity.Id] = entity; }
            public void Update(Movie entity) { _movies[entity.Id] = entity; }
            public void Remove(int id) { _movies.Remove(id); }
            public int Count => _movies.Count;
            public IReadOnlyList<Movie> GetByGenre(Genre genre) => _movies.Values.Where(m => m.Genre == genre).ToList();
            public IReadOnlyList<Movie> Search(string query) => _movies.Values.ToList();
        }

        #endregion

        private StubReviewRepository _reviewRepo;
        private StubCustomerRepository _customerRepo;
        private StubMovieRepository _movieRepo;
        private ReviewService _service;

        [TestInitialize]
        public void Setup()
        {
            _reviewRepo = new StubReviewRepository();
            _customerRepo = new StubCustomerRepository();
            _movieRepo = new StubMovieRepository();
            _service = new ReviewService(_reviewRepo, _customerRepo, _movieRepo);

            _customerRepo.Seed(1, "Alice");
            _customerRepo.Seed(2, "Bob");
            _customerRepo.Seed(3, "Charlie");
            _movieRepo.Seed(1, "The Matrix", Genre.Action);
            _movieRepo.Seed(2, "Inception", Genre.ScienceFiction);
            _movieRepo.Seed(3, "Amelie", Genre.Romance);
        }

        #region Constructor Validation

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullReviewRepo_Throws()
        {
            new ReviewService(null, _customerRepo, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullCustomerRepo_Throws()
        {
            new ReviewService(_reviewRepo, null, _movieRepo);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_NullMovieRepo_Throws()
        {
            new ReviewService(_reviewRepo, _customerRepo, null);
        }

        #endregion

        #region SubmitReview

        [TestMethod]
        public void SubmitReview_ValidInput_CreatesReview()
        {
            var review = _service.SubmitReview(1, 1, 4, "Great movie!");

            Assert.IsNotNull(review);
            Assert.AreEqual(1, review.CustomerId);
            Assert.AreEqual(1, review.MovieId);
            Assert.AreEqual(4, review.Stars);
            Assert.AreEqual("Great movie!", review.ReviewText);
            Assert.AreEqual("Alice", review.CustomerName);
            Assert.AreEqual("The Matrix", review.MovieName);
        }

        [TestMethod]
        public void SubmitReview_NullText_Allowed()
        {
            var review = _service.SubmitReview(1, 1, 5, null);

            Assert.IsNotNull(review);
            Assert.IsNull(review.ReviewText);
        }

        [TestMethod]
        public void SubmitReview_TrimsWhitespace()
        {
            var review = _service.SubmitReview(1, 1, 3, "  spacious  ");

            Assert.AreEqual("spacious", review.ReviewText);
        }

        [TestMethod]
        public void SubmitReview_SetsCreatedDate()
        {
            var before = DateTime.Now;
            var review = _service.SubmitReview(1, 1, 4, "test");
            var after = DateTime.Now;

            Assert.IsTrue(review.CreatedDate >= before && review.CreatedDate <= after);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void SubmitReview_Stars0_Throws()
        {
            _service.SubmitReview(1, 1, 0, "bad stars");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void SubmitReview_Stars6_Throws()
        {
            _service.SubmitReview(1, 1, 6, "too many stars");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitReview_TextTooLong_Throws()
        {
            var longText = new string('x', Review.MaxReviewTextLength + 1);
            _service.SubmitReview(1, 1, 4, longText);
        }

        [TestMethod]
        public void SubmitReview_TextExactlyMaxLength_Succeeds()
        {
            var maxText = new string('a', Review.MaxReviewTextLength);
            var review = _service.SubmitReview(1, 1, 3, maxText);

            Assert.AreEqual(Review.MaxReviewTextLength, review.ReviewText.Length);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitReview_NonexistentCustomer_Throws()
        {
            _service.SubmitReview(999, 1, 4, "no such customer");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitReview_NonexistentMovie_Throws()
        {
            _service.SubmitReview(1, 999, 4, "no such movie");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitReview_DuplicateReview_Throws()
        {
            _service.SubmitReview(1, 1, 4, "first review");
            _service.SubmitReview(1, 1, 5, "duplicate");
        }

        [TestMethod]
        public void SubmitReview_SameCustomerDifferentMovies_Allowed()
        {
            var r1 = _service.SubmitReview(1, 1, 4, "Matrix");
            var r2 = _service.SubmitReview(1, 2, 5, "Inception");

            Assert.AreEqual("The Matrix", r1.MovieName);
            Assert.AreEqual("Inception", r2.MovieName);
        }

        [TestMethod]
        public void SubmitReview_SameMovieDifferentCustomers_Allowed()
        {
            var r1 = _service.SubmitReview(1, 1, 4, "Alice's review");
            var r2 = _service.SubmitReview(2, 1, 3, "Bob's review");

            Assert.AreEqual("Alice", r1.CustomerName);
            Assert.AreEqual("Bob", r2.CustomerName);
        }

        [TestMethod]
        public void SubmitReview_BoundaryStars1_Succeeds()
        {
            var review = _service.SubmitReview(1, 1, 1, "terrible");
            Assert.AreEqual(1, review.Stars);
        }

        [TestMethod]
        public void SubmitReview_BoundaryStars5_Succeeds()
        {
            var review = _service.SubmitReview(1, 1, 5, "perfect");
            Assert.AreEqual(5, review.Stars);
        }

        #endregion

        #region GetMovieReviews / GetCustomerReviews

        [TestMethod]
        public void GetMovieReviews_ReturnsOnlyThatMovie()
        {
            _service.SubmitReview(1, 1, 4, "Matrix");
            _service.SubmitReview(2, 1, 3, "Matrix too");
            _service.SubmitReview(1, 2, 5, "Inception");

            var reviews = _service.GetMovieReviews(1);

            Assert.AreEqual(2, reviews.Count);
            Assert.IsTrue(reviews.All(r => r.MovieId == 1));
        }

        [TestMethod]
        public void GetMovieReviews_NoReviews_ReturnsEmpty()
        {
            var reviews = _service.GetMovieReviews(99);
            Assert.AreEqual(0, reviews.Count);
        }

        [TestMethod]
        public void GetCustomerReviews_ReturnsOnlyThatCustomer()
        {
            _service.SubmitReview(1, 1, 4, "Matrix");
            _service.SubmitReview(1, 2, 5, "Inception");
            _service.SubmitReview(2, 1, 3, "Bob's");

            var reviews = _service.GetCustomerReviews(1);

            Assert.AreEqual(2, reviews.Count);
            Assert.IsTrue(reviews.All(r => r.CustomerId == 1));
        }

        #endregion

        #region GetMovieStats

        [TestMethod]
        public void GetMovieStats_ReturnsCorrectStats()
        {
            _service.SubmitReview(1, 1, 5, "great");
            _service.SubmitReview(2, 1, 3, "ok");
            _service.SubmitReview(3, 1, 4, "good");

            var stats = _service.GetMovieStats(1);

            Assert.AreEqual(1, stats.MovieId);
            Assert.AreEqual(3, stats.TotalReviews);
            Assert.AreEqual(4.0, stats.AverageStars, 0.01);
            Assert.AreEqual(1, stats.FiveStarCount);
            Assert.AreEqual(1, stats.FourStarCount);
            Assert.AreEqual(1, stats.ThreeStarCount);
        }

        [TestMethod]
        public void GetMovieStats_NoReviews_ReturnsZeros()
        {
            var stats = _service.GetMovieStats(99);
            Assert.AreEqual(0, stats.TotalReviews);
            Assert.AreEqual(0, stats.AverageStars, 0.01);
        }

        #endregion

        #region GetTopRated

        [TestMethod]
        public void GetTopRated_EnrichesWithMovieNames()
        {
            _service.SubmitReview(1, 1, 5, null);
            _service.SubmitReview(2, 2, 4, null);

            var topRated = _service.GetTopRated(10, 1);

            Assert.AreEqual(2, topRated.Count);
            Assert.AreEqual("The Matrix", topRated[0].MovieName);
            Assert.AreEqual(Genre.Action, topRated[0].Genre);
        }

        [TestMethod]
        public void GetTopRated_RespectsMinReviews()
        {
            _service.SubmitReview(1, 1, 5, null);  // Matrix: 1 review
            _service.SubmitReview(1, 2, 4, null);  // Inception: 2 reviews
            _service.SubmitReview(2, 2, 3, null);

            var topRated = _service.GetTopRated(10, 2);

            Assert.AreEqual(1, topRated.Count);
            Assert.AreEqual(2, topRated[0].MovieId);
        }

        [TestMethod]
        public void GetTopRated_RespectsCount()
        {
            _service.SubmitReview(1, 1, 5, null);
            _service.SubmitReview(2, 2, 4, null);
            _service.SubmitReview(3, 3, 3, null);

            var topRated = _service.GetTopRated(2, 1);

            Assert.AreEqual(2, topRated.Count);
        }

        #endregion

        #region GetSummary

        [TestMethod]
        public void GetSummary_EmptyReviews_ReturnsDefaults()
        {
            var summary = _service.GetSummary();

            Assert.AreEqual(0, summary.TotalReviews);
            Assert.AreEqual(0, summary.AverageStars, 0.01);
            Assert.AreEqual(0, summary.ReviewedMovieCount);
            Assert.AreEqual(3, summary.TotalMovieCount);  // 3 seeded movies
            Assert.AreEqual(0, summary.ReviewingCustomerCount);
            Assert.IsNull(summary.MostReviewedMovieId);
            Assert.AreEqual(5, summary.StarDistribution.Count);
            Assert.IsTrue(summary.StarDistribution.Values.All(v => v == 0));
        }

        [TestMethod]
        public void GetSummary_SingleReview_CorrectStats()
        {
            _service.SubmitReview(1, 1, 4, "good");

            var summary = _service.GetSummary();

            Assert.AreEqual(1, summary.TotalReviews);
            Assert.AreEqual(4.0, summary.AverageStars, 0.01);
            Assert.AreEqual(1, summary.ReviewedMovieCount);
            Assert.AreEqual(1, summary.ReviewingCustomerCount);
            Assert.AreEqual(1, summary.MostReviewedMovieId);
            Assert.AreEqual(1, summary.StarDistribution[4]);
        }

        [TestMethod]
        public void GetSummary_MultipleReviews_CorrectAggregation()
        {
            _service.SubmitReview(1, 1, 5, null);  // Alice -> Matrix
            _service.SubmitReview(2, 1, 3, null);  // Bob -> Matrix
            _service.SubmitReview(3, 1, 4, null);  // Charlie -> Matrix
            _service.SubmitReview(1, 2, 5, null);  // Alice -> Inception

            var summary = _service.GetSummary();

            Assert.AreEqual(4, summary.TotalReviews);
            Assert.AreEqual(4.3, summary.AverageStars, 0.1);  // (5+3+4+5)/4 = 4.25 rounds to 4.3
            Assert.AreEqual(2, summary.ReviewedMovieCount);    // Matrix, Inception
            Assert.AreEqual(3, summary.ReviewingCustomerCount); // Alice, Bob, Charlie
            Assert.AreEqual(1, summary.MostReviewedMovieId);   // Matrix with 3 reviews
        }

        [TestMethod]
        public void GetSummary_StarDistribution_AccurateHistogram()
        {
            _service.SubmitReview(1, 1, 5, null);
            _service.SubmitReview(2, 2, 5, null);
            _service.SubmitReview(3, 3, 3, null);
            _service.SubmitReview(1, 2, 1, null);

            var summary = _service.GetSummary();

            Assert.AreEqual(1, summary.StarDistribution[1]);
            Assert.AreEqual(0, summary.StarDistribution[2]);
            Assert.AreEqual(1, summary.StarDistribution[3]);
            Assert.AreEqual(0, summary.StarDistribution[4]);
            Assert.AreEqual(2, summary.StarDistribution[5]);
        }

        [TestMethod]
        public void GetSummary_MostReviewedMovie_TiedBreaksOnFirstSeen()
        {
            // Movie 1 and 2 each get 1 review — first one seen should win
            _service.SubmitReview(1, 1, 4, null);
            _service.SubmitReview(2, 2, 5, null);

            var summary = _service.GetSummary();

            Assert.IsNotNull(summary.MostReviewedMovieId);
        }

        #endregion

        #region DeleteReview

        [TestMethod]
        public void DeleteReview_ExistingReview_ReturnsTrue()
        {
            var review = _service.SubmitReview(1, 1, 4, "test");

            var result = _service.DeleteReview(review.Id);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void DeleteReview_NonexistentReview_ReturnsFalse()
        {
            var result = _service.DeleteReview(999);

            Assert.IsFalse(result);
        }

        #endregion

        #region Enrich

        [TestMethod]
        public void Enrich_FillsMissingCustomerNames()
        {
            var reviews = new List<Review>
            {
                new Review { CustomerId = 1, MovieId = 1, Stars = 4 },
                new Review { CustomerId = 2, MovieId = 1, Stars = 3 },
            };

            var enriched = _service.Enrich(reviews);

            Assert.AreEqual("Alice", enriched[0].CustomerName);
            Assert.AreEqual("Bob", enriched[1].CustomerName);
        }

        [TestMethod]
        public void Enrich_FillsMissingMovieNames()
        {
            var reviews = new List<Review>
            {
                new Review { CustomerId = 1, MovieId = 1, Stars = 4 },
                new Review { CustomerId = 1, MovieId = 2, Stars = 5 },
            };

            var enriched = _service.Enrich(reviews);

            Assert.AreEqual("The Matrix", enriched[0].MovieName);
            Assert.AreEqual("Inception", enriched[1].MovieName);
        }

        [TestMethod]
        public void Enrich_PreservesExistingNames()
        {
            var reviews = new List<Review>
            {
                new Review { CustomerId = 1, MovieId = 1, Stars = 4,
                    CustomerName = "PresetName", MovieName = "PresetMovie" },
            };

            var enriched = _service.Enrich(reviews);

            Assert.AreEqual("PresetName", enriched[0].CustomerName);
            Assert.AreEqual("PresetMovie", enriched[0].MovieName);
        }

        [TestMethod]
        public void Enrich_UnknownIds_FallbackToUnknown()
        {
            var reviews = new List<Review>
            {
                new Review { CustomerId = 999, MovieId = 888, Stars = 3 },
            };

            var enriched = _service.Enrich(reviews);

            Assert.AreEqual("Unknown", enriched[0].CustomerName);
            Assert.AreEqual("Unknown", enriched[0].MovieName);
        }

        [TestMethod]
        public void Enrich_EmptyList_ReturnsEmpty()
        {
            var enriched = _service.Enrich(new List<Review>());
            Assert.AreEqual(0, enriched.Count);
        }

        [TestMethod]
        public void Enrich_SameCustomerMultipleReviews_LooksUpOnce()
        {
            // Same customer ID appears in 3 reviews — should still work
            var reviews = new List<Review>
            {
                new Review { CustomerId = 1, MovieId = 1, Stars = 4 },
                new Review { CustomerId = 1, MovieId = 2, Stars = 5 },
                new Review { CustomerId = 1, MovieId = 3, Stars = 3 },
            };

            var enriched = _service.Enrich(reviews);

            Assert.IsTrue(enriched.All(r => r.CustomerName == "Alice"));
        }

        #endregion
    }
}
