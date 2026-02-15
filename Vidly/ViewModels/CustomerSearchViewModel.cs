using System.Collections.Generic;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the customer index page with search and filter support.
    /// </summary>
    public class CustomerSearchViewModel
    {
        /// <summary>
        /// The filtered/searched list of customers to display.
        /// </summary>
        public List<Customer> Customers { get; set; } = new List<Customer>();

        /// <summary>
        /// Current search query (name/email substring).
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// Current membership type filter.
        /// </summary>
        public MembershipType? MembershipType { get; set; }

        /// <summary>
        /// Current sort field.
        /// </summary>
        public string SortBy { get; set; }

        /// <summary>
        /// Total number of customers before filtering.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Customer base statistics.
        /// </summary>
        public CustomerStats Stats { get; set; }
    }
}
