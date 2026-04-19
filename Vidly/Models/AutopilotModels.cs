using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Customer's autopilot preferences that guide autonomous queue generation.
    /// </summary>
    public class AutopilotProfile
    {
        public int CustomerId { get; set; }
        public bool Enabled { get; set; }
        public List<Genre> FavoriteGenres { get; set; } = new List<Genre>();
        public List<string> MoodPreferences { get; set; } = new List<string>();
        public string DecadePreference { get; set; }
        public int MaxQueueSize { get; set; } = 5;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// A movie autonomously selected by the Autopilot engine.
    /// </summary>
    public class AutopilotPick
    {
        public Movie Movie { get; set; }
        public double RelevanceScore { get; set; }
        public string Reason { get; set; }
        public string Category { get; set; }
        public bool? Accepted { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public DateTime? SkippedAt { get; set; }
    }

    /// <summary>
    /// A weekly queue of autonomously curated movie picks.
    /// </summary>
    public class AutopilotWeeklyQueue
    {
        public DateTime WeekStart { get; set; }
        public List<AutopilotPick> Picks { get; set; } = new List<AutopilotPick>();
        public DateTime GeneratedAt { get; set; }
        public int TotalMoviesConsidered { get; set; }
        public double PriorAcceptanceRate { get; set; }
    }

    /// <summary>
    /// A learning insight derived from accept/skip patterns.
    /// </summary>
    public class AutopilotInsight
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public double Confidence { get; set; }
        public DateTime DetectedAt { get; set; }
        public string Emoji { get; set; }
    }

    /// <summary>
    /// View model for the Autopilot dashboard.
    /// </summary>
    public class AutopilotViewModel
    {
        public AutopilotProfile Profile { get; set; }
        public AutopilotWeeklyQueue CurrentQueue { get; set; }
        public List<AutopilotWeeklyQueue> PastQueues { get; set; } = new List<AutopilotWeeklyQueue>();
        public List<AutopilotInsight> Insights { get; set; } = new List<AutopilotInsight>();
        public List<Customer> Customers { get; set; } = new List<Customer>();
        public int? SelectedCustomerId { get; set; }
        public int TotalAccepted { get; set; }
        public int TotalSkipped { get; set; }
        public int Streak { get; set; }
        public double AcceptanceRate { get; set; }
        public string ErrorMessage { get; set; }
    }
}
