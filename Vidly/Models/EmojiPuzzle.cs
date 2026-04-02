using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// An emoji puzzle that represents a movie's plot using emoji sequences.
    /// Players guess which movie the emojis describe.
    /// </summary>
    public class EmojiPuzzle
    {
        public int Id { get; set; }

        /// <summary>Movie ID this puzzle represents.</summary>
        public int MovieId { get; set; }

        /// <summary>Movie name (the answer).</summary>
        public string MovieName { get; set; }

        /// <summary>Genre of the movie.</summary>
        public Genre? Genre { get; set; }

        /// <summary>Emoji sequence representing the movie plot.</summary>
        public string Emojis { get; set; }

        /// <summary>Difficulty: Easy, Medium, Hard.</summary>
        public string Difficulty { get; set; }

        /// <summary>Optional hint text.</summary>
        public string Hint { get; set; }

        /// <summary>Number of times solved correctly.</summary>
        public int TimesSolved { get; set; }

        /// <summary>Number of times attempted.</summary>
        public int TimesAttempted { get; set; }

        /// <summary>Success rate as percentage.</summary>
        public double SuccessRate =>
            TimesAttempted > 0 ? (double)TimesSolved / TimesAttempted * 100 : 0;
    }

    /// <summary>
    /// Tracks a player's emoji game session.
    /// </summary>
    public class EmojiGameSession
    {
        public int Score { get; set; }
        public int Round { get; set; }
        public int TotalRounds { get; set; }
        public int Streak { get; set; }
        public int BestStreak { get; set; }
        public bool HintUsed { get; set; }
        public List<EmojiRoundResult> History { get; set; } = new List<EmojiRoundResult>();
    }

    /// <summary>
    /// Result of a single emoji guessing round.
    /// </summary>
    public class EmojiRoundResult
    {
        public string Emojis { get; set; }
        public string MovieName { get; set; }
        public string PlayerGuess { get; set; }
        public bool Correct { get; set; }
        public bool HintUsed { get; set; }
        public int PointsEarned { get; set; }
    }
}
