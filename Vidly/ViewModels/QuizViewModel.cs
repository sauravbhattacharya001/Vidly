using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// View model for the Movie Quiz feature.
    /// </summary>
    public class QuizViewModel
    {
        public QuizSession ActiveSession { get; set; }
        public QuizQuestion CurrentQuestion { get; set; }
        public int CurrentQuestionIndex { get; set; }
        public int TotalQuestions { get; set; }
        public QuizSession CompletedSession { get; set; }
        public QuizStats CustomerStats { get; set; }
        public List<LeaderboardEntry> Leaderboard { get; set; }
            = new List<LeaderboardEntry>();
        public DailyChallenge DailyChallenge { get; set; }
        public bool DailyChallengeCompleted { get; set; }
        public QuizAnswer DailyChallengeAnswer { get; set; }
        public List<Customer> Customers { get; set; }
            = new List<Customer>();
        public int SelectedCustomerId { get; set; }
        public string Message { get; set; }
        public bool IsError { get; set; }
    }
}
