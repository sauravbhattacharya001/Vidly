using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    public enum MovieNightTheme
    {
        GenreFocus,
        GenreMix,
        DecadeFocus,
        CriticsChoice,
        FanFavorites,
        HiddenGems,
        NewReleases,
        SurpriseMe
    }

    public class MovieNightRequest
    {
        public MovieNightTheme Theme { get; set; } = MovieNightTheme.SurpriseMe;
        public Genre? Genre { get; set; }
        public int? Decade { get; set; }
        public int MovieCount { get; set; } = 3;
        public int EstimatedRuntimeMinutes { get; set; } = 120;
        public int? CustomerId { get; set; }
        public DateTime? StartTime { get; set; }
        public int BreakMinutes { get; set; } = 15;
    }

    public class MovieNightPlan
    {
        public string Title { get; set; }
        public MovieNightTheme Theme { get; set; }
        public List<MovieNightSlot> Slots { get; set; } = new List<MovieNightSlot>();
        public int TotalMinutes { get; set; }
        public string TotalDuration { get; set; }
        public DateTime EstimatedEndTime { get; set; }
        public List<string> SnackSuggestions { get; set; } = new List<string>();
        public string ThemeDescription { get; set; }
        public int MovieCount { get; set; }
        public int AvailableCount { get; set; }
        public string AvailabilityNote { get; set; }
    }

    public class MovieNightSlot
    {
        public int Order { get; set; }
        public Movie Movie { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int RuntimeMinutes { get; set; }
        public bool IsAvailable { get; set; }
        public string SlotNote { get; set; }
        public string BreakSuggestion { get; set; }
    }
}
