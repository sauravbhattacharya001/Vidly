using System.Collections.Generic;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the rental index page with search, filter, and stats.
    /// </summary>
    public class RentalSearchViewModel
    {
        /// <summary>
        /// The filtered/searched list of rentals to display.
        /// </summary>
        public List<Rental> Rentals { get; set; } = new List<Rental>();

        /// <summary>
        /// Current search query (customer name or movie name).
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// Current status filter.
        /// </summary>
        public RentalStatus? Status { get; set; }

        /// <summary>
        /// Current sort field.
        /// </summary>
        public string SortBy { get; set; }

        /// <summary>
        /// Total number of rentals before filtering.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Rental statistics for the dashboard.
        /// </summary>
        public RentalStats Stats { get; set; }
    }
}
