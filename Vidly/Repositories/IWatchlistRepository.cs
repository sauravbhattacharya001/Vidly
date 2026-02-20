using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Watchlist-specific repository for managing customer movie wishlists.
    /// </summary>
    public interface IWatchlistRepository
    {
        /// <summary>
        /// Gets a watchlist item by its ID.
        /// </summary>
        WatchlistItem GetById(int id);

        /// <summary>
        /// Gets all watchlist items across all customers.
        /// </summary>
        IReadOnlyList<WatchlistItem> GetAll();

        /// <summary>
        /// Gets all watchlist items for a specific customer,
        /// ordered by priority (highest first), then by added date (newest first).
        /// </summary>
        IReadOnlyList<WatchlistItem> GetByCustomer(int customerId);

        /// <summary>
        /// Checks whether a specific movie is on a customer's watchlist.
        /// </summary>
        bool IsOnWatchlist(int customerId, int movieId);

        /// <summary>
        /// Adds a movie to a customer's watchlist.
        /// Throws InvalidOperationException if the movie is already on their watchlist.
        /// </summary>
        WatchlistItem Add(WatchlistItem item);

        /// <summary>
        /// Removes a watchlist item by its ID.
        /// </summary>
        void Remove(int id);

        /// <summary>
        /// Removes a specific movie from a customer's watchlist.
        /// Returns true if the item was found and removed, false otherwise.
        /// </summary>
        bool RemoveByCustomerAndMovie(int customerId, int movieId);

        /// <summary>
        /// Updates the note and/or priority of a watchlist item.
        /// </summary>
        void Update(WatchlistItem item);

        /// <summary>
        /// Clears all watchlist items for a customer.
        /// Returns the number of items removed.
        /// </summary>
        int ClearCustomerWatchlist(int customerId);

        /// <summary>
        /// Gets watchlist statistics for a customer.
        /// </summary>
        WatchlistStats GetStats(int customerId);

        /// <summary>
        /// Gets the most-watchlisted movies across all customers.
        /// Returns movie IDs with their watchlist count, ordered by count descending.
        /// </summary>
        IReadOnlyList<PopularWatchlistMovie> GetMostWatchlisted(int limit = 10);
    }

    /// <summary>
    /// Summary statistics for a customer's watchlist.
    /// </summary>
    public class WatchlistStats
    {
        public int TotalItems { get; set; }
        public int NormalCount { get; set; }
        public int HighCount { get; set; }
        public int MustWatchCount { get; set; }
    }

    /// <summary>
    /// A movie that appears on multiple customers' watchlists.
    /// </summary>
    public class PopularWatchlistMovie
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int WatchlistCount { get; set; }
    }
}
