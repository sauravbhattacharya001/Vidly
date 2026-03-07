using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Repository for movie requests with voting and search support.
    /// </summary>
    public interface IMovieRequestRepository : IRepository<MovieRequest>
    {
        /// <summary>Returns all requests by a customer, newest first.</summary>
        IReadOnlyList<MovieRequest> GetByCustomer(int customerId);

        /// <summary>Returns all requests with a given status.</summary>
        IReadOnlyList<MovieRequest> GetByStatus(MovieRequestStatus status);

        /// <summary>
        /// Searches requests by title or reason text.
        /// Optionally filters by status and/or genre.
        /// </summary>
        IReadOnlyList<MovieRequest> Search(
            string query, MovieRequestStatus? status, Genre? genre);

        /// <summary>
        /// Returns the existing request for this exact title (case-insensitive),
        /// or null if none exists.
        /// </summary>
        MovieRequest GetByTitle(string title);

        /// <summary>Records a vote for a request. Returns false if already voted.</summary>
        bool AddVote(MovieRequestVote vote);

        /// <summary>Removes a vote. Returns false if no such vote existed.</summary>
        bool RemoveVote(int requestId, int customerId);

        /// <summary>Returns true if the customer has voted on this request.</summary>
        bool HasVoted(int requestId, int customerId);

        /// <summary>Returns all votes for a request.</summary>
        IReadOnlyList<MovieRequestVote> GetVotes(int requestId);

        /// <summary>Returns vote count for a request.</summary>
        int GetVoteCount(int requestId);
    }
}
