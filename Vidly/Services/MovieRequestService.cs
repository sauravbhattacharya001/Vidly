using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages customer movie requests — submission, voting, trending,
    /// fulfillment, and demand analytics.
    /// </summary>
    public class MovieRequestService
    {
        /// <summary>Maximum open requests per customer.</summary>
        public const int MaxOpenRequestsPerCustomer = 10;

        /// <summary>Minimum characters for a request title.</summary>
        public const int MinTitleLength = 2;

        /// <summary>Days of recent activity for trending calculation.</summary>
        public const int TrendingWindowDays = 7;

        /// <summary>Weight of recent votes in demand score (vs total).</summary>
        public const double RecencyWeight = 2.0;

        private readonly IMovieRequestRepository _requestRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IClock _clock;

        public MovieRequestService(
            IMovieRequestRepository requestRepository,
            IMovieRepository movieRepository,
            ICustomerRepository customerRepository,
            IClock clock = null)
        {
            _requestRepository = requestRepository
                ?? throw new ArgumentNullException(nameof(requestRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
            _customerRepository = customerRepository
                ?? throw new ArgumentNullException(nameof(customerRepository));
            _clock = clock ?? SystemClock.Instance;
        }

        /// <summary>
        /// Submit a new movie request. If a request for the same title already
        /// exists, the customer's vote is added to it instead.
        /// </summary>
        public MovieRequest SubmitRequest(
            int customerId, string title, int? year = null,
            Genre? genre = null, string reason = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title is required.", nameof(title));

            title = title.Trim();
            if (title.Length < MinTitleLength)
                throw new ArgumentException(
                    $"Title must be at least {MinTitleLength} characters.",
                    nameof(title));

            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException("Customer not found.", nameof(customerId));

            // Check if the movie is already in the catalog
            var existingMovies = _movieRepository.GetAll();
            var alreadyStocked = existingMovies.FirstOrDefault(m =>
                string.Equals(m.Name, title, StringComparison.OrdinalIgnoreCase));
            if (alreadyStocked != null)
                throw new InvalidOperationException(
                    $"'{title}' is already in the catalog (ID: {alreadyStocked.Id}).");

            // Check for duplicate request — add vote instead
            var existing = _requestRepository.GetByTitle(title);
            if (existing != null && existing.Status == MovieRequestStatus.Open)
            {
                Upvote(existing.Id, customerId);
                return existing;
            }

            // Check per-customer limit
            var customerRequests = _requestRepository.GetByCustomer(customerId);
            var openCount = customerRequests.Count(r => r.Status == MovieRequestStatus.Open);
            if (openCount >= MaxOpenRequestsPerCustomer)
                throw new InvalidOperationException(
                    $"You can have at most {MaxOpenRequestsPerCustomer} open requests.");

            var request = new MovieRequest
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                Title = title,
                Year = year,
                Genre = genre,
                Reason = reason,
                RequestedDate = _clock.Now,
                Status = MovieRequestStatus.Open,
                UpvoteCount = 0
            };

            _requestRepository.Add(request);
            return request;
        }

        /// <summary>
        /// Upvote a request. Each customer can vote once per request.
        /// Cannot vote on your own request.
        /// </summary>
        public bool Upvote(int requestId, int customerId)
        {
            var request = _requestRepository.GetById(requestId);
            if (request == null)
                throw new ArgumentException("Request not found.", nameof(requestId));

            if (request.Status != MovieRequestStatus.Open &&
                request.Status != MovieRequestStatus.UnderReview)
                return false; // can't vote on closed requests

            if (request.CustomerId == customerId)
                return false; // can't upvote your own request

            var customer = _customerRepository.GetById(customerId);
            if (customer == null)
                throw new ArgumentException("Customer not found.", nameof(customerId));

            var vote = new MovieRequestVote
            {
                RequestId = requestId,
                CustomerId = customerId,
                VotedDate = _clock.Now
            };

            return _requestRepository.AddVote(vote);
        }

        /// <summary>
        /// Remove a customer's vote from a request.
        /// </summary>
        public bool RemoveVote(int requestId, int customerId)
        {
            return _requestRepository.RemoveVote(requestId, customerId);
        }

        /// <summary>
        /// Mark a request as under review by staff.
        /// </summary>
        public MovieRequest MarkUnderReview(int requestId, string staffNote = null)
        {
            var request = _requestRepository.GetById(requestId);
            if (request == null)
                throw new ArgumentException("Request not found.", nameof(requestId));

            if (request.Status != MovieRequestStatus.Open)
                throw new InvalidOperationException(
                    "Only open requests can be marked for review.");

            request.Status = MovieRequestStatus.UnderReview;
            request.StaffNote = staffNote;
            _requestRepository.Update(request);
            return request;
        }

        /// <summary>
        /// Fulfill a request — the movie has been added to the catalog.
        /// </summary>
        public MovieRequest Fulfill(int requestId, string staffNote = null)
        {
            var request = _requestRepository.GetById(requestId);
            if (request == null)
                throw new ArgumentException("Request not found.", nameof(requestId));

            if (request.Status == MovieRequestStatus.Fulfilled)
                throw new InvalidOperationException("Request is already fulfilled.");

            if (request.Status == MovieRequestStatus.Declined)
                throw new InvalidOperationException("Cannot fulfill a declined request.");

            request.Status = MovieRequestStatus.Fulfilled;
            request.FulfilledDate = _clock.Now;
            request.StaffNote = staffNote;
            _requestRepository.Update(request);
            return request;
        }

        /// <summary>
        /// Decline a request with an explanation.
        /// </summary>
        public MovieRequest Decline(int requestId, string staffNote)
        {
            var request = _requestRepository.GetById(requestId);
            if (request == null)
                throw new ArgumentException("Request not found.", nameof(requestId));

            if (request.Status == MovieRequestStatus.Fulfilled)
                throw new InvalidOperationException("Cannot decline a fulfilled request.");

            request.Status = MovieRequestStatus.Declined;
            request.StaffNote = staffNote;
            _requestRepository.Update(request);
            return request;
        }

        /// <summary>
        /// Get trending requests ranked by demand score.
        /// Demand score = total votes + (recent votes * recency weight).
        /// </summary>
        public IReadOnlyList<TrendingRequest> GetTrending(int count = 10)
        {
            var openRequests = _requestRepository.GetByStatus(MovieRequestStatus.Open);
            var reviewRequests = _requestRepository.GetByStatus(MovieRequestStatus.UnderReview);
            var all = openRequests.Concat(reviewRequests).ToList();

            var cutoff = _clock.Now.AddDays(-TrendingWindowDays);

            var trending = all.Select(r =>
            {
                var votes = _requestRepository.GetVotes(r.Id);
                var recentVotes = votes.Count(v => v.VotedDate >= cutoff);
                var totalDemand = r.UpvoteCount + 1; // +1 for the requester

                return new TrendingRequest
                {
                    Request = r,
                    TotalDemand = totalDemand,
                    RecentVotes = recentVotes,
                    DemandScore = totalDemand + (recentVotes * RecencyWeight)
                };
            })
            .OrderByDescending(t => t.DemandScore)
            .ThenByDescending(t => t.Request.RequestedDate)
            .Take(count)
            .ToList();

            return trending.AsReadOnly();
        }

        /// <summary>
        /// Get all requests by a customer.
        /// </summary>
        public IReadOnlyList<MovieRequest> GetCustomerRequests(int customerId)
        {
            return _requestRepository.GetByCustomer(customerId);
        }

        /// <summary>
        /// Search requests by title/reason text with optional filters.
        /// </summary>
        public IReadOnlyList<MovieRequest> Search(
            string query, MovieRequestStatus? status = null, Genre? genre = null)
        {
            return _requestRepository.Search(query, status, genre);
        }

        /// <summary>
        /// Get aggregate statistics about the request system.
        /// </summary>
        public MovieRequestStats GetStats()
        {
            var all = _requestRepository.GetAll();

            var genreGroups = all
                .Where(r => r.Genre.HasValue)
                .GroupBy(r => r.Genre.Value)
                .OrderByDescending(g => g.Count())
                .ToList();

            var totalVotes = all.Sum(r => r.UpvoteCount);

            return new MovieRequestStats
            {
                TotalRequests = all.Count,
                OpenRequests = all.Count(r => r.Status == MovieRequestStatus.Open),
                FulfilledRequests = all.Count(r => r.Status == MovieRequestStatus.Fulfilled),
                DeclinedRequests = all.Count(r => r.Status == MovieRequestStatus.Declined),
                UnderReviewRequests = all.Count(r => r.Status == MovieRequestStatus.UnderReview),
                TotalVotes = totalVotes,
                UniqueRequesters = all.Select(r => r.CustomerId).Distinct().Count(),
                FulfillmentRate = all.Count > 0
                    ? (double)all.Count(r => r.Status == MovieRequestStatus.Fulfilled) / all.Count
                    : 0,
                AverageVotesPerRequest = all.Count > 0
                    ? (double)totalVotes / all.Count
                    : 0,
                MostRequestedGenre = genreGroups.Any()
                    ? genreGroups.First().Key.ToString()
                    : null
            };
        }

        /// <summary>
        /// Get genre distribution of open requests.
        /// Helps staff understand what types of movies customers want.
        /// </summary>
        public IDictionary<string, int> GetGenreBreakdown()
        {
            var open = _requestRepository.GetByStatus(MovieRequestStatus.Open);
            return open
                .Where(r => r.Genre.HasValue)
                .GroupBy(r => r.Genre.Value.ToString())
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
