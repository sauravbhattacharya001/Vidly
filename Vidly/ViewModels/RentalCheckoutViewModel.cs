using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the rental checkout form.
    /// Provides dropdown lists for customer and movie selection.
    /// </summary>
    public class RentalCheckoutViewModel
    {
        /// <summary>
        /// The rental being created.
        /// </summary>
        public Rental Rental { get; set; }

        /// <summary>
        /// Available customers for the dropdown.
        /// </summary>
        public IReadOnlyList<Customer> Customers { get; set; } = new List<Customer>();

        /// <summary>
        /// Available movies for the dropdown (excludes currently rented-out movies).
        /// </summary>
        public IReadOnlyList<Movie> AvailableMovies { get; set; } = new List<Movie>();
    }
}
