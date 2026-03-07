using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class MovieRequestServiceTests
    {
        private InMemoryMovieRequestRepository _requestRepo;
        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private MovieRequestService _service;

        [TestInitialize]
        public void Setup()
        {
            _requestRepo = new InMemoryMovieRequestRepository();
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _service = new MovieRequestService(_requestRepo, _movieRepo, _customerRepo);

            // Seed customers
            _customerRepo.Add(new Customer { Name = "Alice" });
            _customerRepo.Add(new Customer { Name = "Bob" });
            _customerRepo.Add(new Customer { Name = "Charlie" });

            // Seed a movie that's already in catalog
            _movieRepo.Add(new Movie { Name = "The Matrix", Genre = Genre.Action, ReleaseDate = new DateTime(1999, 3, 31) });
        }

        // ── Submit Request ───────────────────────────────────────

        [TestMethod]
        public void SubmitRequest_ValidInput_CreatesRequest()
        {
            var result = _service.SubmitRequest(1, "Inception", 2010, Genre.Action, "Love Nolan");

            Assert.IsNotNull(result);
            Assert.AreEqual("Inception", result.Title);
            Assert.AreEqual(2010, result.Year);
            Assert.AreEqual(Genre.Action, result.Genre);
            Assert.AreEqual("Love Nolan", result.Reason);
            Assert.AreEqual(MovieRequestStatus.Open, result.Status);
            Assert.AreEqual(1, result.CustomerId);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitRequest_EmptyTitle_Throws()
        {
            _service.SubmitRequest(1, "");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitRequest_NullTitle_Throws()
        {
            _service.SubmitRequest(1, null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitRequest_TitleTooShort_Throws()
        {
            _service.SubmitRequest(1, "X");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitRequest_InvalidCustomer_Throws()
        {
            _service.SubmitRequest(999, "Some Movie");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitRequest_AlreadyInCatalog_Throws()
        {
            _service.SubmitRequest(1, "The Matrix");
        }

        [TestMethod]
        public void SubmitRequest_DuplicateTitle_AddsVoteInstead()
        {
            var first = _service.SubmitRequest(1, "Inception");
            var second = _service.SubmitRequest(2, "Inception");

            Assert.AreEqual(first.Id, second.Id);
            Assert.IsTrue(_requestRepo.HasVoted(first.Id, 2));
        }

        [TestMethod]
        public void SubmitRequest_DuplicateTitle_CaseInsensitive()
        {
            var first = _service.SubmitRequest(1, "inception");
            var second = _service.SubmitRequest(2, "INCEPTION");

            Assert.AreEqual(first.Id, second.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitRequest_ExceedsMaxOpen_Throws()
        {
            for (int i = 0; i < MovieRequestService.MaxOpenRequestsPerCustomer; i++)
            {
                _service.SubmitRequest(1, "Movie " + i);
            }
            _service.SubmitRequest(1, "One Too Many");
        }

        [TestMethod]
        public void SubmitRequest_TrimsWhitespace()
        {
            var result = _service.SubmitRequest(1, "  Inception  ");
            Assert.AreEqual("Inception", result.Title);
        }

        // ── Upvote ───────────────────────────────────────────────

        [TestMethod]
        public void Upvote_ValidVote_ReturnsTrue()
        {
            var request = _service.SubmitRequest(1, "Inception");
            var result = _service.Upvote(request.Id, 2);

            Assert.IsTrue(result);
            Assert.AreEqual(1, request.UpvoteCount);
        }

        [TestMethod]
        public void Upvote_OwnRequest_ReturnsFalse()
        {
            var request = _service.SubmitRequest(1, "Inception");
            var result = _service.Upvote(request.Id, 1);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Upvote_DuplicateVote_ReturnsFalse()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.Upvote(request.Id, 2);
            var result = _service.Upvote(request.Id, 2);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Upvote_DeclinedRequest_ReturnsFalse()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.Decline(request.Id, "Not available");

            var result = _service.Upvote(request.Id, 2);
            Assert.IsFalse(result);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Upvote_NonexistentRequest_Throws()
        {
            _service.Upvote(999, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Upvote_NonexistentCustomer_Throws()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.Upvote(request.Id, 999);
        }

        // ── Remove Vote ──────────────────────────────────────────

        [TestMethod]
        public void RemoveVote_ExistingVote_ReturnsTrue()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.Upvote(request.Id, 2);

            Assert.IsTrue(_service.RemoveVote(request.Id, 2));
            Assert.AreEqual(0, request.UpvoteCount);
        }

        [TestMethod]
        public void RemoveVote_NoVote_ReturnsFalse()
        {
            var request = _service.SubmitRequest(1, "Inception");
            Assert.IsFalse(_service.RemoveVote(request.Id, 2));
        }

        // ── Status Transitions ───────────────────────────────────

        [TestMethod]
        public void MarkUnderReview_OpenRequest_Succeeds()
        {
            var request = _service.SubmitRequest(1, "Inception");
            var result = _service.MarkUnderReview(request.Id, "Checking availability");

            Assert.AreEqual(MovieRequestStatus.UnderReview, result.Status);
            Assert.AreEqual("Checking availability", result.StaffNote);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void MarkUnderReview_FulfilledRequest_Throws()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.Fulfill(request.Id);
            _service.MarkUnderReview(request.Id);
        }

        [TestMethod]
        public void Fulfill_OpenRequest_Succeeds()
        {
            var request = _service.SubmitRequest(1, "Inception");
            var result = _service.Fulfill(request.Id, "Added to catalog");

            Assert.AreEqual(MovieRequestStatus.Fulfilled, result.Status);
            Assert.IsNotNull(result.FulfilledDate);
            Assert.AreEqual("Added to catalog", result.StaffNote);
        }

        [TestMethod]
        public void Fulfill_UnderReviewRequest_Succeeds()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.MarkUnderReview(request.Id);
            var result = _service.Fulfill(request.Id);

            Assert.AreEqual(MovieRequestStatus.Fulfilled, result.Status);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Fulfill_AlreadyFulfilled_Throws()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.Fulfill(request.Id);
            _service.Fulfill(request.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Fulfill_DeclinedRequest_Throws()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.Decline(request.Id, "No");
            _service.Fulfill(request.Id);
        }

        [TestMethod]
        public void Decline_OpenRequest_Succeeds()
        {
            var request = _service.SubmitRequest(1, "Inception");
            var result = _service.Decline(request.Id, "Licensing issue");

            Assert.AreEqual(MovieRequestStatus.Declined, result.Status);
            Assert.AreEqual("Licensing issue", result.StaffNote);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Decline_FulfilledRequest_Throws()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.Fulfill(request.Id);
            _service.Decline(request.Id, "No");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Fulfill_NonexistentRequest_Throws()
        {
            _service.Fulfill(999);
        }

        // ── Trending ─────────────────────────────────────────────

        [TestMethod]
        public void GetTrending_RankedByDemandScore()
        {
            var r1 = _service.SubmitRequest(1, "Movie A");
            var r2 = _service.SubmitRequest(1, "Movie B");

            _service.Upvote(r2.Id, 2);
            _service.Upvote(r2.Id, 3);

            var trending = _service.GetTrending();

            Assert.IsTrue(trending.Count >= 2);
            Assert.AreEqual("Movie B", trending[0].Request.Title);
            Assert.IsTrue(trending[0].DemandScore > trending[1].DemandScore);
        }

        [TestMethod]
        public void GetTrending_IncludesUnderReview()
        {
            var r = _service.SubmitRequest(1, "Movie A");
            _service.MarkUnderReview(r.Id);

            var trending = _service.GetTrending();
            Assert.AreEqual(1, trending.Count);
        }

        [TestMethod]
        public void GetTrending_ExcludesFulfilledAndDeclined()
        {
            var r1 = _service.SubmitRequest(1, "Movie A");
            var r2 = _service.SubmitRequest(1, "Movie B");
            var r3 = _service.SubmitRequest(1, "Movie C");

            _service.Fulfill(r1.Id);
            _service.Decline(r2.Id, "No");

            var trending = _service.GetTrending();
            Assert.AreEqual(1, trending.Count);
            Assert.AreEqual("Movie C", trending[0].Request.Title);
        }

        [TestMethod]
        public void GetTrending_LimitsResults()
        {
            for (int i = 0; i < 5; i++)
                _service.SubmitRequest(1, "Movie " + i);

            var trending = _service.GetTrending(3);
            Assert.AreEqual(3, trending.Count);
        }

        [TestMethod]
        public void GetTrending_TotalDemandIncludesRequester()
        {
            var r = _service.SubmitRequest(1, "Movie A");
            _service.Upvote(r.Id, 2);

            var trending = _service.GetTrending();
            Assert.AreEqual(2, trending[0].TotalDemand); // 1 vote + 1 requester
        }

        // ── Search ───────────────────────────────────────────────

        [TestMethod]
        public void Search_ByTitle_FindsMatch()
        {
            _service.SubmitRequest(1, "Inception");
            _service.SubmitRequest(1, "Interstellar");

            var results = _service.Search("incep");
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Inception", results[0].Title);
        }

        [TestMethod]
        public void Search_ByReason_FindsMatch()
        {
            _service.SubmitRequest(1, "Inception", reason: "Great sci-fi thriller");

            var results = _service.Search("thriller");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Search_FilterByStatus()
        {
            _service.SubmitRequest(1, "Movie A");
            var r = _service.SubmitRequest(1, "Movie B");
            _service.Fulfill(r.Id);

            var results = _service.Search(null, MovieRequestStatus.Fulfilled);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Movie B", results[0].Title);
        }

        [TestMethod]
        public void Search_FilterByGenre()
        {
            _service.SubmitRequest(1, "Action Movie", genre: Genre.Action);
            _service.SubmitRequest(1, "Comedy Movie", genre: Genre.Comedy);

            var results = _service.Search(null, genre: Genre.Action);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Action Movie", results[0].Title);
        }

        // ── Stats ────────────────────────────────────────────────

        [TestMethod]
        public void GetStats_EmptySystem_ReturnsZeros()
        {
            var stats = _service.GetStats();
            Assert.AreEqual(0, stats.TotalRequests);
            Assert.AreEqual(0, stats.FulfillmentRate);
        }

        [TestMethod]
        public void GetStats_WithData_CalculatesCorrectly()
        {
            _service.SubmitRequest(1, "Movie A", genre: Genre.Action);
            _service.SubmitRequest(2, "Movie B", genre: Genre.Action);
            var r = _service.SubmitRequest(1, "Movie C", genre: Genre.Comedy);
            _service.Fulfill(r.Id);
            _service.Upvote(_requestRepo.GetByTitle("Movie A").Id, 2);

            var stats = _service.GetStats();

            Assert.AreEqual(3, stats.TotalRequests);
            Assert.AreEqual(2, stats.OpenRequests);
            Assert.AreEqual(1, stats.FulfilledRequests);
            Assert.AreEqual(2, stats.UniqueRequesters);
            Assert.AreEqual("Action", stats.MostRequestedGenre);
            Assert.IsTrue(stats.FulfillmentRate > 0.3 && stats.FulfillmentRate < 0.4);
        }

        [TestMethod]
        public void GetStats_FulfillmentRate_Correct()
        {
            _service.SubmitRequest(1, "Movie A");
            var r = _service.SubmitRequest(1, "Movie B");
            _service.Fulfill(r.Id);

            var stats = _service.GetStats();
            Assert.AreEqual(0.5, stats.FulfillmentRate, 0.01);
        }

        // ── Genre Breakdown ──────────────────────────────────────

        [TestMethod]
        public void GetGenreBreakdown_ReturnsOpenOnly()
        {
            _service.SubmitRequest(1, "Action 1", genre: Genre.Action);
            _service.SubmitRequest(1, "Action 2", genre: Genre.Action);
            var r = _service.SubmitRequest(1, "Comedy 1", genre: Genre.Comedy);
            _service.Fulfill(r.Id); // fulfilled — excluded from breakdown

            var breakdown = _service.GetGenreBreakdown();
            Assert.AreEqual(1, breakdown.Count); // only Action
            Assert.AreEqual(2, breakdown["Action"]);
        }

        // ── Customer Requests ────────────────────────────────────

        [TestMethod]
        public void GetCustomerRequests_ReturnsOnlyThatCustomer()
        {
            _service.SubmitRequest(1, "Movie A");
            _service.SubmitRequest(2, "Movie B");
            _service.SubmitRequest(1, "Movie C");

            var results = _service.GetCustomerRequests(1);
            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.All(r => r.CustomerId == 1));
        }

        // ── Edge Cases ───────────────────────────────────────────

        [TestMethod]
        public void SubmitRequest_MinimalInput_Succeeds()
        {
            var result = _service.SubmitRequest(1, "AB");
            Assert.IsNotNull(result);
            Assert.AreEqual("AB", result.Title);
        }

        [TestMethod]
        public void Upvote_UnderReviewRequest_Succeeds()
        {
            var request = _service.SubmitRequest(1, "Inception");
            _service.MarkUnderReview(request.Id);

            Assert.IsTrue(_service.Upvote(request.Id, 2));
        }

        [TestMethod]
        public void SubmitRequest_FulfilledDuplicate_CreatesNewRequest()
        {
            var first = _service.SubmitRequest(1, "Inception");
            _service.Fulfill(first.Id);

            // Same title but fulfilled — should create new request
            var second = _service.SubmitRequest(2, "Inception");
            Assert.AreNotEqual(first.Id, second.Id);
            Assert.AreEqual(MovieRequestStatus.Open, second.Status);
        }
    }
}
