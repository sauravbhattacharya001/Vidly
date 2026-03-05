using System;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A user-defined tag that can be applied to movies.
    /// Tags complement the fixed Genre enum with flexible, cross-genre
    /// categorization (e.g., "Staff Pick", "Date Night", "Award Winner").
    /// </summary>
    public class MovieTag
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tag name is required.")]
        [StringLength(50, MinimumLength = 2,
            ErrorMessage = "Tag name must be between 2 and 50 characters.")]
        public string Name { get; set; }

        [StringLength(200, ErrorMessage = "Description cannot exceed 200 characters.")]
        public string Description { get; set; }

        /// <summary>
        /// Optional color for UI display (e.g., "#FF5733").
        /// </summary>
        [StringLength(20)]
        public string Color { get; set; }

        /// <summary>
        /// Whether this tag is a curated "Staff Pick" type tag,
        /// visible in promotional sections.
        /// </summary>
        public bool IsStaffPick { get; set; }

        /// <summary>
        /// Who created the tag (staff member name or "system").
        /// </summary>
        [StringLength(100)]
        public string CreatedBy { get; set; }

        public DateTime CreatedDate { get; set; }

        /// <summary>
        /// Tags can be deactivated without deleting existing assignments.
        /// </summary>
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Join record linking a tag to a movie.
    /// </summary>
    public class MovieTagAssignment
    {
        public int Id { get; set; }

        [Required]
        public int TagId { get; set; }

        /// <summary>Tag name (populated on read).</summary>
        public string TagName { get; set; }

        [Required]
        public int MovieId { get; set; }

        /// <summary>Movie name (populated on read).</summary>
        public string MovieName { get; set; }

        /// <summary>Who applied this tag.</summary>
        [StringLength(100)]
        public string AppliedBy { get; set; }

        public DateTime AppliedDate { get; set; }
    }

    /// <summary>
    /// Tag usage statistics for tag cloud or analytics.
    /// </summary>
    public class TagUsageStats
    {
        public int TagId { get; set; }
        public string TagName { get; set; }
        public string Color { get; set; }
        public bool IsStaffPick { get; set; }
        public int MovieCount { get; set; }
        /// <summary>
        /// Normalized weight 0.0–1.0 for tag cloud sizing.
        /// </summary>
        public double Weight { get; set; }
    }

    /// <summary>
    /// Overall tagging system summary.
    /// </summary>
    public class TaggingSummary
    {
        public int TotalTags { get; set; }
        public int ActiveTags { get; set; }
        public int TotalAssignments { get; set; }
        public int StaffPickCount { get; set; }
        public int UntaggedMovies { get; set; }
        public double AverageTagsPerMovie { get; set; }
    }
}
