using System.Collections.Generic;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.ViewModels
{
    public class CompareViewModel
    {
        /// <summary>All movies available for selection.</summary>
        public IReadOnlyList<Movie> AvailableMovies { get; set; }

        /// <summary>The IDs the user selected to compare.</summary>
        public List<int> SelectedIds { get; set; } = new List<int>();

        /// <summary>Comparison result (null if no comparison yet).</summary>
        public MovieComparisonResult Result { get; set; }

        /// <summary>Error message if comparison failed.</summary>
        public string ErrorMessage { get; set; }
    }
}
