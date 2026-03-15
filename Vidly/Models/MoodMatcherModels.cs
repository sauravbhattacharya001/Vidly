using System;
using System.Collections.Generic;
using System.Linq;

namespace Vidly.Models
{
    /// <summary>
    /// Predefined moods that map to movie genres for mood-based recommendations.
    /// </summary>
    public enum Mood
    {
        Happy = 1,
        Sad = 2,
        Excited = 3,
        Relaxed = 4,
        Scared = 5,
        Romantic = 6,
        Curious = 7,
        Nostalgic = 8,
        Stressed = 9,
        Bored = 10,
        Adventurous = 11,
        Thoughtful = 12
    }

    /// <summary>
    /// Describes how strongly a mood maps to a genre (0.0–1.0).
    /// </summary>
    public class MoodGenreMapping
    {
        public Mood Mood { get; set; }
        public Genre Genre { get; set; }

        /// <summary>Affinity score between 0.0 and 1.0.</summary>
        public double Affinity { get; set; }
    }

    /// <summary>
    /// A single mood log entry for a customer.
    /// </summary>
    public class MoodEntry
    {
        public int CustomerId { get; set; }
        public Mood Mood { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>Optional note about why the customer feels this way.</summary>
        public string Note { get; set; }
    }

    /// <summary>
    /// A mood-scored movie recommendation.
    /// </summary>
    public class MoodRecommendation
    {
        public Movie Movie { get; set; }

        /// <summary>Mood affinity score (0.0–1.0).</summary>
        public double MoodScore { get; set; }

        /// <summary>Combined score factoring mood affinity and movie rating.</summary>
        public double CombinedScore { get; set; }

        /// <summary>Which genre mapping drove this recommendation.</summary>
        public Genre MatchedGenre { get; set; }
    }

    /// <summary>
    /// Mood frequency statistics for a customer.
    /// </summary>
    public class MoodStats
    {
        public int CustomerId { get; set; }
        public int TotalEntries { get; set; }
        public Mood? MostFrequentMood { get; set; }
        public Dictionary<Mood, int> MoodCounts { get; set; } = new Dictionary<Mood, int>();
        public Dictionary<Mood, double> MoodPercentages { get; set; } = new Dictionary<Mood, double>();

        /// <summary>Average mood entries per week (based on span from first to last entry).</summary>
        public double EntriesPerWeek { get; set; }
    }

    /// <summary>
    /// Mood trend over time for visualization.
    /// </summary>
    public class MoodTrend
    {
        public DateTime Date { get; set; }
        public Mood DominantMood { get; set; }
        public int EntryCount { get; set; }
    }
}
