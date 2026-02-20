using System.Collections.Generic;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the Recommendations page with customer selection
    /// and personalized movie suggestions.
    /// </summary>
    public class RecommendationViewModel
    {
        /// <summary>
        /// All customers for the dropdown selector.
        /// </summary>
        public IReadOnlyList<Customer> Customers { get; set; } = new List<Customer>();

        /// <summary>
        /// Currently selected customer ID (null = none selected).
        /// </summary>
        public int? SelectedCustomerId { get; set; }

        /// <summary>
        /// Name of the selected customer for display.
        /// </summary>
        public string SelectedCustomerName { get; set; }

        /// <summary>
        /// The recommendation result (null if no customer selected yet).
        /// </summary>
        public RecommendationResult Result { get; set; }
    }
}
