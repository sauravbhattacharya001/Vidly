using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// In-memory implementation of the movie request repository.
    /// </summary>
    public class InMemoryMovieRequestRepository : IMovieRequestRepository
    {
        private readonly List<MovieRequest> _requests = new List<MovieRequest>();
        private readonly List<MovieRequestVote> _votes = new List<MovieRequestVote>();
        private int _nextRequestId = 1;
        private int _nextVoteId = 1;

        public MovieRequest GetById(int id)
        {
            return _requests.FirstOrDefault(r => r.Id == id);
        }

        public IReadOnlyList<MovieRequest> GetAll()
        {
            return _requests
                .OrderByDescending(r => r.RequestedDate)
                .ToList()
                .AsReadOnly();
        }

        public void Add(MovieRequest entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            entity.Id = _nextRequestId++;
            _requests.Add(entity);
        }

        public void Update(MovieRequest entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            var index = _requests.FindIndex(r => r.Id == entity.Id);
            if (index >= 0) _requests[index] = entity;
        }

        public void Remove(int id)
        {
            _requests.RemoveAll(r => r.Id == id);
            _votes.RemoveAll(v => v.RequestId == id);
        }

        public IReadOnlyList<MovieRequest> GetByCustomer(int customerId)
        {
            return _requests
                .Where(r => r.CustomerId == customerId)
                .OrderByDescending(r => r.RequestedDate)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<MovieRequest> GetByStatus(MovieRequestStatus status)
        {
            return _requests
                .Where(r => r.Status == status)
                .OrderByDescending(r => r.RequestedDate)
                .ToList()
                .AsReadOnly();
        }

        public IReadOnlyList<MovieRequest> Search(
            string query, MovieRequestStatus? status, Genre? genre)
        {
            var results = _requests.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var q = query.Trim();
                results = results.Where(r =>
                    (r.Title != null && r.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (r.Reason != null && r.Reason.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            if (status.HasValue)
                results = results.Where(r => r.Status == status.Value);

            if (genre.HasValue)
                results = results.Where(r => r.Genre == genre.Value);

            return results
                .OrderByDescending(r => r.UpvoteCount)
                .ThenByDescending(r => r.RequestedDate)
                .ToList()
                .AsReadOnly();
        }

        public MovieRequest GetByTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            return _requests.FirstOrDefault(r =>
                string.Equals(r.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public bool AddVote(MovieRequestVote vote)
        {
            if (vote == null) throw new ArgumentNullException(nameof(vote));
            if (HasVoted(vote.RequestId, vote.CustomerId)) return false;

            vote.Id = _nextVoteId++;
            _votes.Add(vote);

            var request = GetById(vote.RequestId);
            if (request != null)
                request.UpvoteCount = GetVoteCount(vote.RequestId);

            return true;
        }

        public bool RemoveVote(int requestId, int customerId)
        {
            var removed = _votes.RemoveAll(v =>
                v.RequestId == requestId && v.CustomerId == customerId);

            if (removed > 0)
            {
                var request = GetById(requestId);
                if (request != null)
                    request.UpvoteCount = GetVoteCount(requestId);
            }

            return removed > 0;
        }

        public bool HasVoted(int requestId, int customerId)
        {
            return _votes.Any(v =>
                v.RequestId == requestId && v.CustomerId == customerId);
        }

        public IReadOnlyList<MovieRequestVote> GetVotes(int requestId)
        {
            return _votes
                .Where(v => v.RequestId == requestId)
                .OrderByDescending(v => v.VotedDate)
                .ToList()
                .AsReadOnly();
        }

        public int GetVoteCount(int requestId)
        {
            return _votes.Count(v => v.RequestId == requestId);
        }
    }
}
