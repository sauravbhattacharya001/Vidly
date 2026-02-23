using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A customer review for a movie. Customers can rate (1-5 stars) and
    /// optionally write a text review. One review per customer per movie.
    /// </summary>
    public class Review
    {
        /// <summary>Maximum allowed length for review text.</summary>
        public const int MaxReviewTextLength = 2000;

        public int Id { get; set; }

        /// <summary>Customer who wrote the review.</summary>
        [Required]
        public int CustomerId { get; set; }

        /// <summary>Movie being reviewed.</summary>
        [Required]
        public int MovieId { get; set; }

        /// <summary>Star rating from 1 (poor) to 5 (excellent).</summary>
        [Required(ErrorMessage = "Star rating is required.")]
        [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5 stars.")]
        [Display(Name = "Rating")]
        public int Stars { get; set; }

        /// <summary>Optional text review.</summary>
        [StringLength(MaxReviewTextLength, ErrorMessage = "Review text cannot exceed 2000 characters.")]
        [Display(Name = "Review")]
        public string ReviewText { get; set; }

        /// <summary>When the review was submitted.</summary>
        [Display(Name = "Reviewed On")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Customer name — populated by the service layer for display.
        /// Not persisted; denormalized for convenience.
        /// </summary>
        public string CustomerName { get; set; }

        /// <summary>
        /// Movie name — populated by the service layer for display.
        /// </summary>
        public string MovieName { get; set; }
    }
}
