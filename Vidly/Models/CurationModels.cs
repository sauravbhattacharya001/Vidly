using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// A staff-curated themed movie list (e.g., "Staff Picks", "Hidden Gems").
    /// </summary>
    public class CuratedList
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Theme { get; set; }
        public string CuratorName { get; set; }
        public int CuratorStaffId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsFeatured { get; set; }
        public int UpVotes { get; set; }
        public int DownVotes { get; set; }
        public List<CuratedListEntry> Entries { get; set; } = new List<CuratedListEntry>();
    }

    /// <summary>
    /// A movie entry within a curated list, with optional curator note.
    /// </summary>
    public class CuratedListEntry
    {
        public int MovieId { get; set; }
        public string CuratorNote { get; set; }
        public int Position { get; set; }
        public DateTime AddedAt { get; set; }
    }

    /// <summary>
    /// Theme categories for curated lists.
    /// </summary>
    public static class CurationThemes
    {
        public const string StaffPicks = "Staff Picks";
        public const string HiddenGems = "Hidden Gems";
        public const string DateNight = "Date Night";
        public const string RainyDay = "Rainy Day";
        public const string FamilyFun = "Family Fun";
        public const string MindBenders = "Mind Benders";
        public const string FeelGood = "Feel Good";
        public const string ClassicCinema = "Classic Cinema";
        public const string WeekendBinge = "Weekend Binge";
        public const string CultFavorites = "Cult Favorites";

        public static readonly string[] All = new[]
        {
            StaffPicks, HiddenGems, DateNight, RainyDay, FamilyFun,
            MindBenders, FeelGood, ClassicCinema, WeekendBinge, CultFavorites
        };
    }

    /// <summary>
    /// Summary statistics for a curated list.
    /// </summary>
    public class CuratedListStats
    {
        public int ListId { get; set; }
        public string Title { get; set; }
        public int MovieCount { get; set; }
        public int UpVotes { get; set; }
        public int DownVotes { get; set; }
        public double ApprovalRate { get; set; }
        public int TotalRentalsFromList { get; set; }
        public double AvgMovieRating { get; set; }
        public Dictionary<Genre, int> GenreBreakdown { get; set; } = new Dictionary<Genre, int>();
    }

    /// <summary>
    /// Store-wide curation report.
    /// </summary>
    public class CurationReport
    {
        public int TotalLists { get; set; }
        public int FeaturedLists { get; set; }
        public int ExpiredLists { get; set; }
        public int TotalCurators { get; set; }
        public int TotalMoviesCurated { get; set; }
        public string TopCurator { get; set; }
        public int TopCuratorListCount { get; set; }
        public string MostPopularTheme { get; set; }
        public CuratedList MostUpvotedList { get; set; }
        public CuratedList MostRecentList { get; set; }
        public Dictionary<string, int> ThemeDistribution { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> CuratorLeaderboard { get; set; } = new Dictionary<string, int>();
        public List<string> Recommendations { get; set; } = new List<string>();
    }
}
