using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// Index page: list all franchises with search and stats.
    /// </summary>
    public class FranchiseIndexViewModel
    {
        public List<FranchiseListItem> Franchises { get; set; } = new List<FranchiseListItem>();
        public string SearchQuery { get; set; }
        public int TotalCount { get; set; }
    }

    /// <summary>
    /// Summary item for franchise listing.
    /// </summary>
    public class FranchiseListItem
    {
        public Franchise Franchise { get; set; }
        public int MovieCount { get; set; }
        public int TotalRentals { get; set; }
        public double AverageRating { get; set; }
    }

    /// <summary>
    /// Detail page: full franchise info, movies in order, report, and drop-off data.
    /// </summary>
    public class FranchiseDetailViewModel
    {
        public Franchise Franchise { get; set; }
        public List<FranchiseMovieItem> Movies { get; set; } = new List<FranchiseMovieItem>();
        public FranchiseReport Report { get; set; }
    }

    /// <summary>
    /// A movie within a franchise, enriched with rental/position data.
    /// </summary>
    public class FranchiseMovieItem
    {
        public int Position { get; set; }
        public Movie Movie { get; set; }
        public int RentalCount { get; set; }
    }

    /// <summary>
    /// Create/edit franchise form.
    /// </summary>
    public class FranchiseFormViewModel
    {
        [Required(ErrorMessage = "Franchise name is required.")]
        [StringLength(255)]
        public string Name { get; set; }

        [StringLength(1000)]
        public string Description { get; set; }

        public int? StartYear { get; set; }

        public bool IsOngoing { get; set; }

        [Required(ErrorMessage = "Select at least one movie.")]
        public List<int> SelectedMovieIds { get; set; } = new List<int>();

        public string Tags { get; set; }

        /// <summary>
        /// Available movies for the selection list.
        /// </summary>
        public List<Movie> AvailableMovies { get; set; } = new List<Movie>();
    }

    /// <summary>
    /// Customer franchise progress dashboard.
    /// </summary>
    public class FranchiseProgressViewModel
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public List<FranchiseProgress> InProgress { get; set; } = new List<FranchiseProgress>();
        public List<FranchiseProgress> Completed { get; set; } = new List<FranchiseProgress>();
        public List<FranchiseRecommendation> Recommendations { get; set; } = new List<FranchiseRecommendation>();

        /// <summary>
        /// Franchise lookup for display purposes.
        /// </summary>
        public Dictionary<int, string> FranchiseNames { get; set; } = new Dictionary<int, string>();
    }
}
