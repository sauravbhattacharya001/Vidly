using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a single head-to-head matchup between two movies.
    /// </summary>
    public class ShowdownMatchup
    {
        public Movie MovieA { get; set; }
        public Movie MovieB { get; set; }
        public int RoundNumber { get; set; }
    }

    /// <summary>
    /// Tracks win/loss stats for a movie across showdown rounds.
    /// </summary>
    public class ShowdownScore
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public double WinRate => (Wins + Losses) > 0
            ? (double)Wins / (Wins + Losses) * 100.0
            : 0;
    }

    /// <summary>
    /// View model for the showdown page.
    /// </summary>
    public class ShowdownViewModel
    {
        public ShowdownMatchup CurrentMatchup { get; set; }
        public List<ShowdownScore> Leaderboard { get; set; } = new List<ShowdownScore>();
        public int TotalRounds { get; set; }
        public string Message { get; set; }
    }
}
