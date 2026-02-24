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
    public class ReviewTests
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
                _movies.Values.Where(m => m.ReleaseDate?.Year == year && m.ReleaseDate?.Month == month)
                    .ToList().AsReadOnly();
            public Movie GetRandom() => _movies.Values.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string q, Genre? g, int? r) =>
                _movies.Values.ToList().AsReadOnly();
        }

        private class TestCustomerRepository : ICustomerRepository
        {
            private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
            private int _nextId = 1;

            public void Add(Customer c)
            {
                if (c.Id == 0) c.Id = _nextId++;
                _customers[c.Id] = c;
            }
            public Customer GetById(int id) =>
                _customers.TryGetValue(id, out var c) ? c : null;
            public IReadOnlyList<Customer> GetAll() =>
                _customers.Values.ToList().AsReadOnly();
            public void Update(Customer c) { _customers[c.Id] = c; }
            public void Remove(int id) { _customers.Remove(id); }
            public IReadOnlyList<Customer> Search(string q, MembershipType? m) =>
                _customers.Values.ToList().AsReadOnly();
            public IReadOnlyList<Customer> GetByMemberSince(int y, int m) =>
                new List<Customer>().AsReadOnly();

            public CustomerStats GetStats() => new CustomerStats
            {
                TotalCustomers = _customers.Count,
            };
        }

        private TestMovieRepository _movieRepo;
        private TestCustomerRepository _customerRepo;
        private InMemoryReviewRepository _reviewRepo;
        private ReviewService _reviewService;

        [TestInitialize]
        public void Setup()
        {
            InMemoryReviewRepository.Reset();
            _movieRepo = new TestMovieRepository();
            _customerRepo = new TestCustomerRepository();
            _reviewRepo = new InMemoryReviewRepository();
            _reviewService = new ReviewService(_reviewRepo, _customerRepo, _movieRepo);

            // Seed data
            _movieRepo.Add(new Movie { Id = 1, Name = "The Matrix", Genre = Genre.SciFi, Rating = 5 });
            _movieRepo.Add(new Movie { Id = 2, Name = "Toy Story", Genre = Genre.Animation, Rating = 4 });
            _movieRepo.Add(new Movie { Id = 3, Name = "The Godfather", Genre = Genre.Drama, Rating = 5 });

            _customerRepo.Add(new Customer { Id = 1, Name = "Alice", Email = "alice@test.com", MembershipType = MembershipType.Gold });
            _customerRepo.Add(new Customer { Id = 2, Name = "Bob", Email = "bob@test.com", MembershipType = MembershipType.Silver });
            _customerRepo.Add(new Customer { Id = 3, Name = "Charlie", Email = "charlie@test.com", MembershipType = MembershipType.Basic });
        }

        #endregion

        // ═══════════════════════════════════════════════════════
        // Review Model Tests
        // ═══════════════════════════════════════════════════════

        [TestMethod]
        public void Review_DefaultCreatedDate_IsSet()
        {
            var review = new Review();
            Assert.IsTrue(review.CreatedDate > DateTime.MinValue);
        }

        [TestMethod]
        public void Review_Properties_SetCorrectly()
        {
            var review = new Review
            {
                CustomerId = 1,
                MovieId = 2,
                Stars = 4,
                ReviewText = "Great movie!",
                CustomerName = "Alice",
                MovieName = "Toy Story",
            };

            Assert.AreEqual(1, review.CustomerId);
            Assert.AreEqual(2, review.MovieId);
            Assert.AreEqual(4, review.Stars);
            Assert.AreEqual("Great movie!", review.ReviewText);
        }

        // ═══════════════════════════════════════════════════════
        // Repository Tests
        // ═══════════════════════════════════════════════════════

        [TestMethod]
        public void Repository_Add_AssignsId()
        {
            var review = new Review { CustomerId = 1, MovieId = 1, Stars = 5 };
            _reviewRepo.Add(review);

            Assert.IsTrue(review.Id > 0);
        }

        [TestMethod]
        public void Repository_GetById_ReturnsCorrectReview()
        {
            var review = new Review { CustomerId = 1, MovieId = 1, Stars = 4, ReviewText = "Good" };
            _reviewRepo.Add(review);

            var retrieved = _reviewRepo.GetById(review.Id);
            Assert.IsNotNull(retrieved);
            Assert.AreEqual(4, retrieved.Stars);
            Assert.AreEqual("Good", retrieved.ReviewText);
        }

        [TestMethod]
        public void Repository_GetById_ReturnsClone()
        {
            var review = new Review { CustomerId = 1, MovieId = 1, Stars = 5 };
            _reviewRepo.Add(review);

            var a = _reviewRepo.GetById(review.Id);
            var b = _reviewRepo.GetById(review.Id);
            Assert.AreNotSame(a, b);
        }

        [TestMethod]
        public void Repository_GetById_NotFound_ReturnsNull()
        {
            Assert.IsNull(_reviewRepo.GetById(999));
        }

        [TestMethod]
        public void Repository_GetAll_ReturnsAllReviews()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 2, Stars = 3 });

            var all = _reviewRepo.GetAll();
            Assert.AreEqual(2, all.Count);
        }

        [TestMethod]
        public void Repository_GetAll_OrderedByDateDescending()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 3, CreatedDate = new DateTime(2025, 1, 1) });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 2, Stars = 5, CreatedDate = new DateTime(2026, 1, 1) });

            var all = _reviewRepo.GetAll();
            Assert.IsTrue(all[0].CreatedDate >= all[1].CreatedDate);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Repository_Add_DuplicateCustomerMovie_Throws()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 3 }); // duplicate
        }

        [TestMethod]
        public void Repository_Add_SameCustomerDifferentMovie_Succeeds()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 2, Stars = 3 });

            Assert.AreEqual(2, _reviewRepo.GetAll().Count);
        }

        [TestMethod]
        public void Repository_Add_DifferentCustomerSameMovie_Succeeds()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 1, Stars = 4 });

            Assert.AreEqual(2, _reviewRepo.GetAll().Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Repository_Add_Null_Throws()
        {
            _reviewRepo.Add(null);
        }

        [TestMethod]
        public void Repository_Update_ModifiesReview()
        {
            var review = new Review { CustomerId = 1, MovieId = 1, Stars = 3 };
            _reviewRepo.Add(review);

            review.Stars = 5;
            review.ReviewText = "Updated!";
            _reviewRepo.Update(review);

            var updated = _reviewRepo.GetById(review.Id);
            Assert.AreEqual(5, updated.Stars);
            Assert.AreEqual("Updated!", updated.ReviewText);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Repository_Update_NotFound_Throws()
        {
            _reviewRepo.Update(new Review { Id = 999, CustomerId = 1, MovieId = 1, Stars = 3 });
        }

        [TestMethod]
        public void Repository_Remove_DeletesReview()
        {
            var review = new Review { CustomerId = 1, MovieId = 1, Stars = 5 };
            _reviewRepo.Add(review);

            _reviewRepo.Remove(review.Id);
            Assert.IsNull(_reviewRepo.GetById(review.Id));
        }

        [TestMethod]
        public void Repository_Remove_FreesCustomerMovieSlot()
        {
            var review = new Review { CustomerId = 1, MovieId = 1, Stars = 5 };
            _reviewRepo.Add(review);
            _reviewRepo.Remove(review.Id);

            // Can now add a new review for same customer+movie
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 3 });
            Assert.AreEqual(1, _reviewRepo.GetAll().Count);
        }

        [TestMethod]
        public void Repository_Remove_NonExistent_NoError()
        {
            _reviewRepo.Remove(999); // should not throw
        }

        [TestMethod]
        public void Repository_GetByMovie_ReturnsCorrectReviews()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 1, Stars = 3 });
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 2, Stars = 4 });

            var movieReviews = _reviewRepo.GetByMovie(1);
            Assert.AreEqual(2, movieReviews.Count);
            Assert.IsTrue(movieReviews.All(r => r.MovieId == 1));
        }

        [TestMethod]
        public void Repository_GetByCustomer_ReturnsCorrectReviews()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 2, Stars = 4 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 1, Stars = 3 });

            var custReviews = _reviewRepo.GetByCustomer(1);
            Assert.AreEqual(2, custReviews.Count);
            Assert.IsTrue(custReviews.All(r => r.CustomerId == 1));
        }

        [TestMethod]
        public void Repository_GetByCustomerAndMovie_Found()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });

            var review = _reviewRepo.GetByCustomerAndMovie(1, 1);
            Assert.IsNotNull(review);
            Assert.AreEqual(5, review.Stars);
        }

        [TestMethod]
        public void Repository_GetByCustomerAndMovie_NotFound()
        {
            Assert.IsNull(_reviewRepo.GetByCustomerAndMovie(1, 99));
        }

        [TestMethod]
        public void Repository_HasReviewed_True()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            Assert.IsTrue(_reviewRepo.HasReviewed(1, 1));
        }

        [TestMethod]
        public void Repository_HasReviewed_False()
        {
            Assert.IsFalse(_reviewRepo.HasReviewed(1, 1));
        }

        [TestMethod]
        public void Repository_GetMovieStats_CorrectCounts()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 1, Stars = 4 });
            _reviewRepo.Add(new Review { CustomerId = 3, MovieId = 1, Stars = 4 });

            var stats = _reviewRepo.GetMovieStats(1);
            Assert.AreEqual(3, stats.TotalReviews);
            Assert.AreEqual(4.3, stats.AverageStars, 0.1);
            Assert.AreEqual(1, stats.FiveStarCount);
            Assert.AreEqual(2, stats.FourStarCount);
            Assert.AreEqual(0, stats.ThreeStarCount);
        }

        [TestMethod]
        public void Repository_GetMovieStats_NoReviews_ZeroAverage()
        {
            var stats = _reviewRepo.GetMovieStats(99);
            Assert.AreEqual(0, stats.TotalReviews);
            Assert.AreEqual(0, stats.AverageStars);
        }

        [TestMethod]
        public void Repository_GetTopRatedMovies_OrderedByAverage()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 2, Stars = 3 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 2, Stars = 3 });

            var topRated = _reviewRepo.GetTopRatedMovies(10);
            Assert.AreEqual(2, topRated.Count);
            Assert.AreEqual(1, topRated[0].MovieId); // 5.0 avg
            Assert.AreEqual(2, topRated[1].MovieId); // 3.0 avg
        }

        [TestMethod]
        public void Repository_GetTopRatedMovies_MinReviewsFilter()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 2, Stars = 5 }); // only 1 review

            var topRated = _reviewRepo.GetTopRatedMovies(10, minReviews: 2);
            Assert.AreEqual(1, topRated.Count);
            Assert.AreEqual(1, topRated[0].MovieId);
        }

        [TestMethod]
        public void Repository_Search_ByReviewText()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5, ReviewText = "Amazing movie!" });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 2, Stars = 3, ReviewText = "Okay film." });

            var results = _reviewRepo.Search("amazing", null);
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Repository_Search_ByMinStars()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 2, Stars = 2 });

            var results = _reviewRepo.Search(null, 4);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(5, results[0].Stars);
        }

        [TestMethod]
        public void Repository_Reset_ClearsAll()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            InMemoryReviewRepository.Reset();

            Assert.AreEqual(0, _reviewRepo.GetAll().Count);
            Assert.IsFalse(_reviewRepo.HasReviewed(1, 1));
        }

        // ═══════════════════════════════════════════════════════
        // Service Tests
        // ═══════════════════════════════════════════════════════

        [TestMethod]
        public void Service_SubmitReview_CreatesReviewWithNames()
        {
            var review = _reviewService.SubmitReview(1, 1, 5, "Brilliant!");

            Assert.IsTrue(review.Id > 0);
            Assert.AreEqual("Alice", review.CustomerName);
            Assert.AreEqual("The Matrix", review.MovieName);
            Assert.AreEqual(5, review.Stars);
            Assert.AreEqual("Brilliant!", review.ReviewText);
        }

        [TestMethod]
        public void Service_SubmitReview_TrimsWhitespace()
        {
            var review = _reviewService.SubmitReview(1, 1, 4, "  Nice movie!  ");
            Assert.AreEqual("Nice movie!", review.ReviewText);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Service_SubmitReview_InvalidCustomer_Throws()
        {
            _reviewService.SubmitReview(99, 1, 5, "Good");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Service_SubmitReview_InvalidMovie_Throws()
        {
            _reviewService.SubmitReview(1, 99, 5, "Good");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Service_SubmitReview_Duplicate_Throws()
        {
            _reviewService.SubmitReview(1, 1, 5, "Great!");
            _reviewService.SubmitReview(1, 1, 3, "Changed my mind.");
        }

        [TestMethod]
        public void Service_GetMovieReviews_EnrichedWithNames()
        {
            _reviewService.SubmitReview(1, 1, 5, "Awesome");
            _reviewService.SubmitReview(2, 1, 4, "Good");

            var reviews = _reviewService.GetMovieReviews(1);
            Assert.AreEqual(2, reviews.Count);
            Assert.IsTrue(reviews.All(r => !string.IsNullOrEmpty(r.CustomerName)));
        }

        [TestMethod]
        public void Service_GetCustomerReviews_EnrichedWithNames()
        {
            _reviewService.SubmitReview(1, 1, 5, null);
            _reviewService.SubmitReview(1, 2, 4, null);

            var reviews = _reviewService.GetCustomerReviews(1);
            Assert.AreEqual(2, reviews.Count);
            Assert.IsTrue(reviews.All(r => !string.IsNullOrEmpty(r.MovieName)));
        }

        [TestMethod]
        public void Service_GetMovieStats_ReturnsCorrect()
        {
            _reviewService.SubmitReview(1, 1, 5, null);
            _reviewService.SubmitReview(2, 1, 3, null);

            var stats = _reviewService.GetMovieStats(1);
            Assert.AreEqual(2, stats.TotalReviews);
            Assert.AreEqual(4.0, stats.AverageStars, 0.01);
        }

        [TestMethod]
        public void Service_GetTopRated_EnrichedWithMovieNames()
        {
            _reviewService.SubmitReview(1, 1, 5, null);
            _reviewService.SubmitReview(2, 1, 5, null);
            _reviewService.SubmitReview(1, 2, 3, null);
            _reviewService.SubmitReview(2, 2, 3, null);

            var topRated = _reviewService.GetTopRated(5);
            Assert.AreEqual(2, topRated.Count);
            Assert.AreEqual("The Matrix", topRated[0].MovieName);
            Assert.AreEqual(Genre.SciFi, topRated[0].Genre);
        }

        [TestMethod]
        public void Service_GetSummary_ReturnsGlobalStats()
        {
            _reviewService.SubmitReview(1, 1, 5, null);
            _reviewService.SubmitReview(2, 1, 4, null);
            _reviewService.SubmitReview(1, 2, 3, null);

            var summary = _reviewService.GetSummary();
            Assert.AreEqual(3, summary.TotalReviews);
            Assert.AreEqual(4.0, summary.AverageStars, 0.01);
            Assert.AreEqual(2, summary.ReviewedMovieCount);
            Assert.AreEqual(2, summary.ReviewingCustomerCount);
            Assert.AreEqual(1, summary.StarDistribution[5]);
            Assert.AreEqual(1, summary.StarDistribution[4]);
            Assert.AreEqual(1, summary.StarDistribution[3]);
        }

        [TestMethod]
        public void Service_GetSummary_Empty_ZeroStats()
        {
            var summary = _reviewService.GetSummary();
            Assert.AreEqual(0, summary.TotalReviews);
            Assert.AreEqual(0, summary.AverageStars);
        }

        [TestMethod]
        public void Service_DeleteReview_ReturnsTrue()
        {
            _reviewService.SubmitReview(1, 1, 5, null);
            Assert.IsTrue(_reviewService.DeleteReview(1));
        }

        [TestMethod]
        public void Service_DeleteReview_NotFound_ReturnsFalse()
        {
            Assert.IsFalse(_reviewService.DeleteReview(999));
        }

        [TestMethod]
        public void Service_DeleteReview_AllowsResubmission()
        {
            var review = _reviewService.SubmitReview(1, 1, 5, null);
            _reviewService.DeleteReview(review.Id);

            // Should be able to review again
            var newReview = _reviewService.SubmitReview(1, 1, 3, "New opinion");
            Assert.AreEqual(3, newReview.Stars);
        }

        // ═══════════════════════════════════════════════════════
        // Controller Tests
        // ═══════════════════════════════════════════════════════

        [TestMethod]
        public void Controller_Index_ReturnsViewWithModel()
        {
            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Index(null, null, null, null) as ViewResult;

            Assert.IsNotNull(result);
            var model = result.Model as ReviewIndexViewModel;
            Assert.IsNotNull(model);
            Assert.IsNotNull(model.Summary);
            Assert.IsNotNull(model.Customers);
            Assert.IsNotNull(model.Movies);
        }

        [TestMethod]
        public void Controller_Index_WithSearch_FiltersResults()
        {
            _reviewService.SubmitReview(1, 1, 5, "Loved the action!");
            _reviewService.SubmitReview(2, 2, 3, "Boring plot.");

            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Index("action", null, null, null) as ViewResult;
            var model = result?.Model as ReviewIndexViewModel;

            Assert.IsNotNull(model);
            Assert.AreEqual("action", model.SearchQuery);
        }

        [TestMethod]
        public void Controller_Index_ShowsStatusMessage()
        {
            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Index(null, null, "Review submitted!", null) as ViewResult;
            var model = result?.Model as ReviewIndexViewModel;

            Assert.AreEqual("Review submitted!", model?.StatusMessage);
            Assert.IsFalse(model?.IsError ?? true);
        }

        [TestMethod]
        public void Controller_Movie_ReturnsMovieReviews()
        {
            _reviewService.SubmitReview(1, 1, 5, "Epic!");
            _reviewService.SubmitReview(2, 1, 4, "Good");

            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Movie(1, null, null) as ViewResult;
            var model = result?.Model as ReviewIndexViewModel;

            Assert.IsNotNull(model);
            Assert.IsNotNull(model.SelectedMovie);
            Assert.AreEqual("The Matrix", model.SelectedMovie.Name);
            Assert.AreEqual(2, model.Reviews.Count);
            Assert.IsNotNull(model.MovieStats);
            Assert.AreEqual(2, model.MovieStats.TotalReviews);
        }

        [TestMethod]
        public void Controller_Movie_NotFound_Returns404()
        {
            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Movie(999, null, null);

            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Controller_Create_ValidInput_RedirectsWithSuccess()
        {
            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Create(1, 1, 5, "Excellent!") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
            Assert.IsTrue(result.RouteValues["message"]?.ToString().Contains("submitted"));
        }

        [TestMethod]
        public void Controller_Create_InvalidStars_RedirectsWithError()
        {
            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Create(1, 1, 0, "Bad stars") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(true, result.RouteValues["error"]);
        }

        [TestMethod]
        public void Controller_Create_Duplicate_RedirectsWithError()
        {
            _reviewService.SubmitReview(1, 1, 5, null);

            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Create(1, 1, 3, "Again") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(true, result.RouteValues["error"]);
        }

        [TestMethod]
        public void Controller_Create_InvalidCustomer_RedirectsWithError()
        {
            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Create(99, 1, 5, "Test") as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(true, result.RouteValues["error"]);
        }

        [TestMethod]
        public void Controller_Delete_ExistingReview_Succeeds()
        {
            _reviewService.SubmitReview(1, 1, 5, null);

            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Delete(1, null) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("Review deleted.", result.RouteValues["message"]);
        }

        [TestMethod]
        public void Controller_Delete_NonExistent_ShowsError()
        {
            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Delete(999, null) as RedirectToRouteResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(true, result.RouteValues["error"]);
        }

        [TestMethod]
        public void Controller_Delete_WithReturnUrl_Redirects()
        {
            _reviewService.SubmitReview(1, 1, 5, null);

            var controller = new ReviewsController(_reviewRepo, _customerRepo, _movieRepo);
            var result = controller.Delete(1, "/Reviews/Movie/1") as RedirectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual("/Reviews/Movie/1", result.Url);
        }

        // ═══════════════════════════════════════════════════════
        // Edge Cases
        // ═══════════════════════════════════════════════════════

        [TestMethod]
        public void Repository_Search_NullQuery_ReturnsAll()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 2, Stars = 3 });

            var results = _reviewRepo.Search(null, null);
            Assert.AreEqual(2, results.Count);
        }

        [TestMethod]
        public void Repository_Search_EmptyQuery_ReturnsAll()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });

            var results = _reviewRepo.Search("   ", null);
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Service_SubmitReview_NullText_Allowed()
        {
            var review = _reviewService.SubmitReview(1, 1, 5, null);
            Assert.IsNull(review.ReviewText);
        }

        [TestMethod]
        public void Service_SubmitReview_EmptyText_Trimmed()
        {
            var review = _reviewService.SubmitReview(1, 1, 5, "   ");
            Assert.AreEqual("", review.ReviewText);
        }

        [TestMethod]
        public void Service_GetSummary_MostReviewedMovie()
        {
            _reviewService.SubmitReview(1, 1, 5, null);
            _reviewService.SubmitReview(2, 1, 4, null);
            _reviewService.SubmitReview(3, 1, 3, null);
            _reviewService.SubmitReview(1, 2, 5, null);

            var summary = _reviewService.GetSummary();
            Assert.AreEqual(1, summary.MostReviewedMovieId); // 3 reviews vs 1
        }

        [TestMethod]
        public void Service_GetSummary_StarDistributionComplete()
        {
            _reviewService.SubmitReview(1, 1, 1, null);
            _reviewService.SubmitReview(2, 1, 2, null);
            _reviewService.SubmitReview(3, 1, 3, null);
            _reviewService.SubmitReview(1, 2, 4, null);
            _reviewService.SubmitReview(2, 2, 5, null);

            var summary = _reviewService.GetSummary();
            Assert.AreEqual(5, summary.StarDistribution.Count);
            foreach (var count in summary.StarDistribution.Values)
                Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void Repository_GetTopRatedMovies_TieBreaksByReviewCount()
        {
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 2, MovieId = 1, Stars = 5 });
            _reviewRepo.Add(new Review { CustomerId = 1, MovieId = 2, Stars = 5 }); // same avg, fewer reviews

            var topRated = _reviewRepo.GetTopRatedMovies(10);
            Assert.AreEqual(1, topRated[0].MovieId); // more reviews comes first
        }

        // ── Input Validation (Issue #18) ─────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Service_SubmitReview_ZeroStars_Throws()
        {
            _reviewService.SubmitReview(1, 1, 0, "text");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Service_SubmitReview_NegativeStars_Throws()
        {
            _reviewService.SubmitReview(1, 1, -1, "text");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Service_SubmitReview_SixStars_Throws()
        {
            _reviewService.SubmitReview(1, 1, 6, "text");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Service_SubmitReview_HundredStars_Throws()
        {
            _reviewService.SubmitReview(1, 1, 100, "text");
        }

        [TestMethod]
        public void Service_SubmitReview_BoundaryOneStar_Succeeds()
        {
            var review = _reviewService.SubmitReview(1, 1, 1, "Terrible");
            Assert.AreEqual(1, review.Stars);
        }

        [TestMethod]
        public void Service_SubmitReview_BoundaryFiveStars_Succeeds()
        {
            var review = _reviewService.SubmitReview(1, 1, 5, "Perfect");
            Assert.AreEqual(5, review.Stars);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Service_SubmitReview_TextTooLong_Throws()
        {
            var longText = new string('x', Review.MaxReviewTextLength + 1);
            _reviewService.SubmitReview(1, 1, 5, longText);
        }

        [TestMethod]
        public void Service_SubmitReview_TextAtMaxLength_Succeeds()
        {
            var maxText = new string('x', Review.MaxReviewTextLength);
            var review = _reviewService.SubmitReview(1, 1, 5, maxText);
            Assert.AreEqual(Review.MaxReviewTextLength, review.ReviewText.Length);
        }

        [TestMethod]
        public void Service_SubmitReview_NullText_StillAllowed()
        {
            var review = _reviewService.SubmitReview(1, 1, 4, null);
            Assert.IsNull(review.ReviewText);
        }

        [TestMethod]
        public void Review_MaxReviewTextLength_Is2000()
        {
            Assert.AreEqual(2000, Review.MaxReviewTextLength);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Service_SubmitReview_IntMinStars_Throws()
        {
            _reviewService.SubmitReview(1, 1, int.MinValue, "text");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Service_SubmitReview_IntMaxStars_Throws()
        {
            _reviewService.SubmitReview(1, 1, int.MaxValue, "text");
        }

        [TestMethod]
        public void Repository_GetByCustomerAndMovie_AfterRemoval_ReturnsNull()
        {
            var review = _reviewService.SubmitReview(1, 1, 5, "Great");
            _reviewRepo.Remove(review.Id);
            Assert.IsNull(_reviewRepo.GetByCustomerAndMovie(1, 1));
        }

        [TestMethod]
        public void Repository_HasReviewed_ConsistentWithGetByCustomerAndMovie()
        {
            // Before adding: both should indicate no review
            Assert.IsFalse(_reviewRepo.HasReviewed(1, 1));
            Assert.IsNull(_reviewRepo.GetByCustomerAndMovie(1, 1));

            // After adding: both should indicate review exists
            var review = _reviewService.SubmitReview(1, 1, 4, "Good");
            Assert.IsTrue(_reviewRepo.HasReviewed(1, 1));
            Assert.IsNotNull(_reviewRepo.GetByCustomerAndMovie(1, 1));

            // After removing: both should indicate no review
            _reviewRepo.Remove(review.Id);
            Assert.IsFalse(_reviewRepo.HasReviewed(1, 1));
            Assert.IsNull(_reviewRepo.GetByCustomerAndMovie(1, 1));
        }
    }
}
