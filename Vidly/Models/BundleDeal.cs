using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// A bundle deal offering discounts for renting multiple movies together.
    /// E.g., "3 for 2" or "Weekend Binge: 5 movies for $15".
    /// </summary>
    public class BundleDeal
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Bundle name is required.")]
        [StringLength(100, ErrorMessage = "Bundle name cannot exceed 100 characters.")]
        public string Name { get; set; }

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string Description { get; set; }

        /// <summary>
        /// Minimum number of movies required to activate this bundle.
        /// </summary>
        [Required]
        [Range(2, 20, ErrorMessage = "Minimum movies must be between 2 and 20.")]
        [Display(Name = "Minimum Movies")]
        public int MinMovies { get; set; }

        /// <summary>
        /// Maximum number of movies this bundle applies to (0 = unlimited).
        /// </summary>
        [Range(0, 50, ErrorMessage = "Maximum movies cannot exceed 50.")]
        [Display(Name = "Maximum Movies")]
        public int MaxMovies { get; set; }

        /// <summary>
        /// The type of discount applied.
        /// </summary>
        [Required]
        [Display(Name = "Discount Type")]
        public BundleDiscountType DiscountType { get; set; }

        /// <summary>
        /// Discount value: percentage (0-100) for Percentage type,
        /// dollar amount for FixedAmount, or number of free movies for FreeMovies.
        /// </summary>
        [Required]
        [Range(0.01, 9999.99, ErrorMessage = "Discount value must be positive.")]
        [Display(Name = "Discount Value")]
        public decimal DiscountValue { get; set; }

        /// <summary>
        /// Optional genre restriction. Null means any genre qualifies.
        /// </summary>
        [Display(Name = "Genre Restriction")]
        public Genre? RequiredGenre { get; set; }

        /// <summary>
        /// Whether this bundle is currently active.
        /// </summary>
        [Display(Name = "Active")]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// When the bundle becomes available.
        /// </summary>
        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// When the bundle expires.
        /// </summary>
        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// Number of times this bundle has been used.
        /// </summary>
        [Display(Name = "Times Used")]
        public int TimesUsed { get; set; }

        /// <summary>
        /// Whether this bundle is currently valid (active and within date range).
        /// </summary>
        public bool IsCurrentlyValid
        {
            get
            {
                if (!IsActive) return false;
                var today = DateTime.Today;
                if (StartDate.HasValue && today < StartDate.Value) return false;
                if (EndDate.HasValue && today > EndDate.Value) return false;
                return true;
            }
        }
    }

    /// <summary>
    /// Type of discount a bundle provides.
    /// </summary>
    public enum BundleDiscountType
    {
        /// <summary>Percentage off total rental cost.</summary>
        [Display(Name = "Percentage Off")]
        Percentage = 1,

        /// <summary>Fixed dollar amount off total.</summary>
        [Display(Name = "Fixed Amount Off")]
        FixedAmount = 2,

        /// <summary>N cheapest movies are free (e.g., "3 for 2" = 1 free movie).</summary>
        [Display(Name = "Free Movies")]
        FreeMovies = 3
    }

    /// <summary>
    /// Result of applying a bundle deal to a set of movies.
    /// </summary>
    public class BundleApplyResult
    {
        public BundleDeal Bundle { get; set; }
        public decimal OriginalTotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalTotal => Math.Max(0, OriginalTotal - DiscountAmount);
        public decimal DiscountPercent =>
            OriginalTotal > 0 ? Math.Round(DiscountAmount / OriginalTotal * 100, 1) : 0;
        public IReadOnlyList<MoviePrice> MoviePrices { get; set; } = new List<MoviePrice>();
        public bool Applied => Bundle != null && DiscountAmount > 0;
    }

    /// <summary>
    /// Price breakdown for a single movie in a bundle.
    /// </summary>
    public class MoviePrice
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public decimal DailyRate { get; set; }
        public bool IsFree { get; set; }
    }
}
