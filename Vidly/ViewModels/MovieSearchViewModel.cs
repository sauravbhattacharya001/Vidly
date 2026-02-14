using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the movie index page with search and filter support.
    /// </summary>
    public class MovieSearchViewModel
    {
        /// <summary>
        /// The filtered/searched list of movies to display.
        /// </summary>
        public List<Movie> Movies { get; set; } = new List<Movie>();

        /// <summary>
        /// Current search query (name substring).
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// Current genre filter.
        /// </summary>
        public Genre? Genre { get; set; }

        /// <summary>
        /// Current minimum rating filter.
        /// </summary>
        public int? MinRating { get; set; }

        /// <summary>
        /// Current sort field.
        /// </summary>
        public string SortBy { get; set; }

        /// <summary>
        /// Total number of movies before filtering (for display).
        /// </summary>
        public int TotalCount { get; set; }
    }
}
