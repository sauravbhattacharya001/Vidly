using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Ordering strategies for marathon movie sequences.
    /// </summary>
    public enum MarathonOrder
    {
        [Display(Name = "Release Date (Oldest First)")]
        Chronological = 1,

        [Display(Name = "Release Date (Newest First)")]
        ReverseChronological = 2,

        [Display(Name = "Highest Rated First")]
        RatingDescending = 3,

        [Display(Name = "Genre Grouped")]
        GenreGrouped = 4,

        [Display(Name = "Random Shuffle")]
        Random = 5
    }

    /// <summary>
    /// A request to build a movie marathon plan.
    /// </summary>
    public class MarathonRequest
    {
        /// <summary>
        /// IDs of movies to include in the marathon.
        /// </summary>
        [Required]
        public List<int> MovieIds { get; set; } = new List<int>();

        /// <summary>
        /// Start time of the marathon.
        /// </summary>
        [Display(Name = "Start Time")]
        public DateTime StartTime { get; set; } = DateTime.Today.AddHours(18); // default 6 PM

        /// <summary>
        /// Assumed runtime per movie in minutes (since Movie model has no duration).
        /// </summary>
        [Display(Name = "Avg Runtime (min)")]
        [Range(30, 300, ErrorMessage = "Runtime must be between 30 and 300 minutes.")]
        public int AvgRuntimeMinutes { get; set; } = 120;

        /// <summary>
        /// Break duration between movies in minutes (0 = no breaks).
        /// </summary>
        [Display(Name = "Break Duration (min)")]
        [Range(0, 60, ErrorMessage = "Break must be between 0 and 60 minutes.")]
        public int BreakMinutes { get; set; } = 15;

        /// <summary>
        /// Desired viewing order.
        /// </summary>
        [Display(Name = "Viewing Order")]
        public MarathonOrder Order { get; set; } = MarathonOrder.Chronological;
    }

    /// <summary>
    /// A single entry in the marathon schedule.
    /// </summary>
    public class MarathonEntry
    {
        public int Position { get; set; }
        public Movie Movie { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public bool HasBreakAfter { get; set; }
        public DateTime? BreakEndTime { get; set; }
    }

    /// <summary>
    /// The complete marathon plan with schedule and stats.
    /// </summary>
    public class MarathonPlan
    {
        public List<MarathonEntry> Entries { get; set; } = new List<MarathonEntry>();
        public DateTime OverallStart { get; set; }
        public DateTime OverallEnd { get; set; }

        /// <summary>Total viewing time (movies only, no breaks).</summary>
        public TimeSpan TotalWatchTime { get; set; }

        /// <summary>Total break time.</summary>
        public TimeSpan TotalBreakTime { get; set; }

        /// <summary>Total duration from start to finish.</summary>
        public TimeSpan TotalDuration => TotalWatchTime + TotalBreakTime;

        public int MovieCount => Entries.Count;

        /// <summary>Genre breakdown (genre → count).</summary>
        public Dictionary<string, int> GenreBreakdown { get; set; } = new Dictionary<string, int>();

        /// <summary>Average rating of selected movies (null if no ratings).</summary>
        public double? AverageRating { get; set; }

        /// <summary>Whether the marathon extends past midnight.</summary>
        public bool SpansMidnight => OverallEnd.Date > OverallStart.Date;

        /// <summary>Estimated total rental cost.</summary>
        public decimal EstimatedCost { get; set; }
    }
}
