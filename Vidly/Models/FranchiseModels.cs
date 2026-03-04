using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a movie franchise/series (e.g., "Star Wars", "The Godfather").
    /// </summary>
    public class Franchise
    {
        public int Id { get; set; }

        [Required]
        [StringLength(255)]
        public string Name { get; set; }

        [StringLength(1000)]
        public string Description { get; set; }

        /// <summary>
        /// Ordered list of movie IDs that belong to this franchise.
        /// Order matters — represents the intended viewing order.
        /// </summary>
        public List<int> MovieIds { get; set; } = new List<int>();

        /// <summary>
        /// Optional year the franchise started.
        /// </summary>
        public int? StartYear { get; set; }

        /// <summary>
        /// Whether the franchise is still producing new entries.
        /// </summary>
        public bool IsOngoing { get; set; }

        /// <summary>
        /// Tags for categorization (e.g., "superhero", "sci-fi", "horror").
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();
    }

    /// <summary>
    /// Tracks a customer's progress through a franchise.
    /// </summary>
    public class FranchiseProgress
    {
        public int CustomerId { get; set; }
        public int FranchiseId { get; set; }

        /// <summary>
        /// Movie IDs the customer has rented/watched from this franchise.
        /// </summary>
        public List<int> WatchedMovieIds { get; set; } = new List<int>();

        /// <summary>
        /// Completion percentage (0-100).
        /// </summary>
        public double CompletionPercent { get; set; }

        /// <summary>
        /// The next movie in the franchise the customer should watch.
        /// Null if completed.
        /// </summary>
        public int? NextMovieId { get; set; }

        /// <summary>
        /// Date the customer first started this franchise.
        /// </summary>
        public DateTime? StartedDate { get; set; }

        /// <summary>
        /// Date the customer completed the franchise (watched all entries).
        /// </summary>
        public DateTime? CompletedDate { get; set; }
    }

    /// <summary>
    /// Summary report for a franchise.
    /// </summary>
    public class FranchiseReport
    {
        public Franchise Franchise { get; set; }
        public int TotalMovies { get; set; }
        public int TotalRentals { get; set; }
        public double AverageRating { get; set; }
        public decimal TotalRevenue { get; set; }
        public int CustomersStarted { get; set; }
        public int CustomersCompleted { get; set; }
        public double CompletionRate { get; set; }
        public int MostPopularMovieId { get; set; }
        public string MostPopularMovieName { get; set; }
        public int LeastPopularMovieId { get; set; }
        public string LeastPopularMovieName { get; set; }
        public List<FranchiseDropoff> Dropoffs { get; set; } = new List<FranchiseDropoff>();
    }

    /// <summary>
    /// Identifies where customers stop watching a franchise.
    /// </summary>
    public class FranchiseDropoff
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int Position { get; set; }
        public int WatchedCount { get; set; }
        public int DroppedCount { get; set; }
        public double DropoffRate { get; set; }
    }

    /// <summary>
    /// Recommendation to continue or start a franchise.
    /// </summary>
    public class FranchiseRecommendation
    {
        public Franchise Franchise { get; set; }
        public string Reason { get; set; }
        public double Score { get; set; }
        public int? NextMovieId { get; set; }
        public string NextMovieName { get; set; }
        public RecommendationType Type { get; set; }
    }

    public enum RecommendationType
    {
        ContinueFranchise,
        StartNewFranchise,
        CompleteFranchise
    }
}
