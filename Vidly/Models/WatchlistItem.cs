using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a movie on a customer's watchlist ("Watch Later").
    /// Links a customer to a movie they intend to rent in the future.
    /// </summary>
    public class WatchlistItem
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Customer is required.")]
        [Display(Name = "Customer")]
        public int CustomerId { get; set; }

        /// <summary>
        /// Resolved customer name (read-only, populated by repository).
        /// </summary>
        public string CustomerName { get; set; }

        [Required(ErrorMessage = "Movie is required.")]
        [Display(Name = "Movie")]
        public int MovieId { get; set; }

        /// <summary>
        /// Resolved movie name (read-only, populated by repository).
        /// </summary>
        public string MovieName { get; set; }

        /// <summary>
        /// Genre of the movie (populated by repository for display).
        /// </summary>
        public Genre? MovieGenre { get; set; }

        /// <summary>
        /// Rating of the movie (populated by repository for display).
        /// </summary>
        public int? MovieRating { get; set; }

        [Display(Name = "Added On")]
        [DataType(DataType.Date)]
        public DateTime AddedDate { get; set; }

        /// <summary>
        /// Optional user note about why they want to watch this movie.
        /// </summary>
        [StringLength(500, ErrorMessage = "Note cannot exceed 500 characters.")]
        [Display(Name = "Note")]
        public string Note { get; set; }

        /// <summary>
        /// Priority level for ordering the watchlist.
        /// </summary>
        [Display(Name = "Priority")]
        public WatchlistPriority Priority { get; set; }
    }

    /// <summary>
    /// Watchlist item priority levels.
    /// </summary>
    public enum WatchlistPriority
    {
        [Display(Name = "Normal")]
        Normal = 1,

        [Display(Name = "High")]
        High = 2,

        [Display(Name = "Must Watch")]
        MustWatch = 3
    }
}
