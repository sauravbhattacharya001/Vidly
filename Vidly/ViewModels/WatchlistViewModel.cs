using System.Collections.Generic;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the watchlist page.
    /// </summary>
    public class WatchlistViewModel
    {
        /// <summary>
        /// Available customers for the dropdown.
        /// </summary>
        public IReadOnlyList<Customer> Customers { get; set; } = new List<Customer>();

        /// <summary>
        /// The selected customer's ID (null if no customer selected).
        /// </summary>
        public int? SelectedCustomerId { get; set; }

        /// <summary>
        /// The selected customer's name.
        /// </summary>
        public string SelectedCustomerName { get; set; }

        /// <summary>
        /// Watchlist items for the selected customer.
        /// </summary>
        public IReadOnlyList<WatchlistItem> Items { get; set; } = new List<WatchlistItem>();

        /// <summary>
        /// Statistics for the selected customer's watchlist.
        /// </summary>
        public WatchlistStats Stats { get; set; }

        /// <summary>
        /// Most popular movies across all watchlists.
        /// </summary>
        public IReadOnlyList<PopularWatchlistMovie> PopularMovies { get; set; } = new List<PopularWatchlistMovie>();

        /// <summary>
        /// Status message to display (e.g., after add/remove).
        /// </summary>
        public string StatusMessage { get; set; }

        /// <summary>
        /// Whether the status message is an error.
        /// </summary>
        public bool IsError { get; set; }
    }

    /// <summary>
    /// View model for the Add to Watchlist form.
    /// </summary>
    public class WatchlistAddViewModel
    {
        /// <summary>
        /// Available customers for the dropdown.
        /// </summary>
        public IReadOnlyList<Customer> Customers { get; set; } = new List<Customer>();

        /// <summary>
        /// Available movies for the dropdown (excludes movies already on the customer's watchlist).
        /// </summary>
        public IReadOnlyList<Movie> AvailableMovies { get; set; } = new List<Movie>();

        /// <summary>
        /// Pre-selected customer ID (from query string).
        /// </summary>
        public int? SelectedCustomerId { get; set; }

        /// <summary>
        /// Pre-selected movie ID (from query string).
        /// </summary>
        public int? SelectedMovieId { get; set; }

        /// <summary>
        /// The item being added.
        /// </summary>
        public WatchlistItem Item { get; set; } = new WatchlistItem();
    }
}
