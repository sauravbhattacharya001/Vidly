using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A customer request for a movie that Vidly doesn't currently stock.
    /// Other customers can upvote requests to signal demand.
    /// </summary>
    public class MovieRequest
    {
        public int Id { get; set; }

        /// <summary>Customer who submitted the request.</summary>
        [Required]
        public int CustomerId { get; set; }

        /// <summary>Resolved customer name (populated by repository).</summary>
        public string CustomerName { get; set; }

        /// <summary>Title of the requested movie.</summary>
        [Required(ErrorMessage = "Movie title is required.")]
        [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters.")]
        public string Title { get; set; }

        /// <summary>Year of release (optional, for disambiguation).</summary>
        [Range(1888, 2100, ErrorMessage = "Year must be between 1888 and 2100.")]
        public int? Year { get; set; }

        /// <summary>Preferred genre (optional).</summary>
        public Genre? Genre { get; set; }

        /// <summary>Why the customer wants this movie.</summary>
        [StringLength(1000, ErrorMessage = "Reason cannot exceed 1000 characters.")]
        public string Reason { get; set; }

        /// <summary>When the request was created.</summary>
        [DataType(DataType.Date)]
        public DateTime RequestedDate { get; set; }

        /// <summary>Current status of the request.</summary>
        public MovieRequestStatus Status { get; set; }

        /// <summary>Date when the request was fulfilled (movie added to catalog).</summary>
        [DataType(DataType.Date)]
        public DateTime? FulfilledDate { get; set; }

        /// <summary>Staff note when fulfilling or declining.</summary>
        [StringLength(500)]
        public string StaffNote { get; set; }

        /// <summary>Number of upvotes from other customers.</summary>
        public int UpvoteCount { get; set; }
    }

    /// <summary>
    /// Records a customer's upvote on a movie request.
    /// </summary>
    public class MovieRequestVote
    {
        public int Id { get; set; }
        public int RequestId { get; set; }
        public int CustomerId { get; set; }
        public DateTime VotedDate { get; set; }
    }

    /// <summary>
    /// Lifecycle status of a movie request.
    /// </summary>
    public enum MovieRequestStatus
    {
        [Display(Name = "Open")]
        Open = 0,

        [Display(Name = "Under Review")]
        UnderReview = 1,

        [Display(Name = "Fulfilled")]
        Fulfilled = 2,

        [Display(Name = "Declined")]
        Declined = 3
    }

    /// <summary>
    /// Trending request with demand metrics.
    /// </summary>
    public class TrendingRequest
    {
        public MovieRequest Request { get; set; }

        /// <summary>Total votes (upvotes + 1 for original requester).</summary>
        public int TotalDemand { get; set; }

        /// <summary>Votes received in the last 7 days.</summary>
        public int RecentVotes { get; set; }

        /// <summary>Demand score factoring recency and total votes.</summary>
        public double DemandScore { get; set; }
    }

    /// <summary>
    /// Summary statistics for the request system.
    /// </summary>
    public class MovieRequestStats
    {
        public int TotalRequests { get; set; }
        public int OpenRequests { get; set; }
        public int FulfilledRequests { get; set; }
        public int DeclinedRequests { get; set; }
        public int UnderReviewRequests { get; set; }
        public int TotalVotes { get; set; }
        public int UniqueRequesters { get; set; }
        public double FulfillmentRate { get; set; }
        public double AverageVotesPerRequest { get; set; }
        public string MostRequestedGenre { get; set; }
    }
}
