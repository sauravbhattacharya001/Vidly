using System.Collections.Generic;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the Reviews index and per-movie review pages.
    /// </summary>
    public class ReviewIndexViewModel
    {
        /// <summary>All reviews to display (filtered or global).</summary>
        public IReadOnlyList<Review> Reviews { get; set; }

        /// <summary>Global review summary stats.</summary>
        public ReviewSummary Summary { get; set; }

        /// <summary>Top-rated movies list.</summary>
        public IReadOnlyList<MovieRating> TopRated { get; set; }

        /// <summary>Review stats for a specific movie (when viewing per-movie).</summary>
        public ReviewStats MovieStats { get; set; }

        /// <summary>Movie being reviewed (for per-movie view).</summary>
        public Movie SelectedMovie { get; set; }

        /// <summary>Search/filter query text.</summary>
        public string SearchQuery { get; set; }

        /// <summary>Minimum star filter.</summary>
        public int? MinStars { get; set; }

        /// <summary>Available customers for the review form.</summary>
        public IReadOnlyList<Customer> Customers { get; set; }

        /// <summary>Available movies for the review form.</summary>
        public IReadOnlyList<Movie> Movies { get; set; }

        /// <summary>Status message for user feedback.</summary>
        public string StatusMessage { get; set; }

        /// <summary>Whether the status message is an error.</summary>
        public bool IsError { get; set; }
    }
}
