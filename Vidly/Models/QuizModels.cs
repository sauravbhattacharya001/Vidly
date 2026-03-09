using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    // ── Enums ───────────────────────────────────────────────────

    public enum QuizDifficulty { Easy, Medium, Hard }

    public enum QuizCategory
    {
        Genre,           // "What genre is X?"
        ReleaseYear,     // "What year was X released?"
        Rating,          // "What is the MPAA rating of X?"
        PriceRange,      // "Which movie costs the most to rent?"
        Availability,    // "Which of these is currently available?"
        Director,        // "Who directed X?" (if director data exists)
        MixedBag         // Random mix
    }

    public enum QuizStatus { InProgress, Completed, Abandoned, TimedOut }

    // ── Question ────────────────────────────────────────────────

    public class QuizQuestion
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public List<string> Options { get; set; } = new List<string>();
        public int CorrectOptionIndex { get; set; }
        public QuizCategory Category { get; set; }
        public QuizDifficulty Difficulty { get; set; }
        public int PointValue { get; set; }

        /// <summary>Optional hint shown after first wrong answer.</summary>
        public string Hint { get; set; }
    }

    // ── Answer ──────────────────────────────────────────────────

    public class QuizAnswer
    {
        public int QuestionId { get; set; }
        public int SelectedOptionIndex { get; set; }
        public bool IsCorrect { get; set; }
        public int PointsEarned { get; set; }
        public DateTime AnsweredAt { get; set; }
        public double ResponseTimeSeconds { get; set; }
    }

    // ── Session ─────────────────────────────────────────────────

    public class QuizSession
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public QuizDifficulty Difficulty { get; set; }
        public QuizCategory Category { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public QuizStatus Status { get; set; }

        public List<QuizQuestion> Questions { get; set; } = new List<QuizQuestion>();
        public List<QuizAnswer> Answers { get; set; } = new List<QuizAnswer>();

        public int TotalPoints { get; set; }
        public int MaxPossiblePoints { get; set; }
        public int CorrectCount { get; set; }
        public int LoyaltyPointsAwarded { get; set; }

        /// <summary>Time limit in minutes (0 = unlimited).</summary>
        public int TimeLimitMinutes { get; set; }
    }

    // ── Leaderboard ─────────────────────────────────────────────

    public class LeaderboardEntry
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int TotalPoints { get; set; }
        public int QuizzesCompleted { get; set; }
        public double AverageAccuracy { get; set; }
        public int CurrentStreak { get; set; }
        public int BestStreak { get; set; }
        public int Rank { get; set; }
    }

    // ── Stats ───────────────────────────────────────────────────

    public class QuizStats
    {
        public int CustomerId { get; set; }
        public int TotalQuizzes { get; set; }
        public int TotalQuestions { get; set; }
        public int TotalCorrect { get; set; }
        public int TotalPointsEarned { get; set; }
        public int TotalLoyaltyPointsAwarded { get; set; }
        public double OverallAccuracy { get; set; }
        public double AverageResponseTime { get; set; }
        public int CurrentStreak { get; set; }
        public int BestStreak { get; set; }
        public QuizCategory StrongestCategory { get; set; }
        public QuizCategory WeakestCategory { get; set; }
        public Dictionary<QuizCategory, double> CategoryAccuracy { get; set; }
            = new Dictionary<QuizCategory, double>();
        public Dictionary<QuizDifficulty, int> QuizzesByDifficulty { get; set; }
            = new Dictionary<QuizDifficulty, int>();
    }

    // ── Daily Challenge ─────────────────────────────────────────

    public class DailyChallenge
    {
        public DateTime Date { get; set; }
        public QuizQuestion Question { get; set; }
        public int BonusMultiplier { get; set; } = 2;
        public List<int> CompletedByCustomerIds { get; set; } = new List<int>();
    }
}
