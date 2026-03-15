using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Unified search results across movies, customers, and rentals.
    /// </summary>
    public class GlobalSearchResults
    {
        public string Query { get; set; }
        public IReadOnlyList<Movie> Movies { get; set; } = new List<Movie>();
        public IReadOnlyList<Customer> Customers { get; set; } = new List<Customer>();
        public IReadOnlyList<Rental> Rentals { get; set; } = new List<Rental>();

        /// <summary>
        /// Total number of results across all categories.
        /// </summary>
        public int TotalResults => Movies.Count + Customers.Count + Rentals.Count;
    }
}
