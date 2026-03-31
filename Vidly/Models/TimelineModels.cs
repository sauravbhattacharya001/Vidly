using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// A single movie entry on the timeline.
    /// </summary>
    public class TimelineEntry
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int Year { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
        public bool IsNewRelease { get; set; }
    }

    /// <summary>
    /// Movies grouped by year for timeline display.
    /// </summary>
    public class TimelineYearGroup
    {
        public int Year { get; set; }
        public List<TimelineEntry> Movies { get; set; } = new List<TimelineEntry>();
    }

    /// <summary>
    /// Full timeline data with stats.
    /// </summary>
    public class TimelineViewModel
    {
        public List<TimelineYearGroup> YearGroups { get; set; } = new List<TimelineYearGroup>();
        public int TotalMovies { get; set; }
        public int EarliestYear { get; set; }
        public int LatestYear { get; set; }
        public int TotalYearsSpanned { get; set; }
        public Dictionary<string, int> GenreCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>Genre filter applied, if any.</summary>
        public Genre? FilterGenre { get; set; }
    }
}
