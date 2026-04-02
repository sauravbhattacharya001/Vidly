using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.ViewModels
{
    /// <summary>
    /// ViewModel for the Emoji Story game — carries puzzle data,
    /// answer choices, game state, and results.
    /// </summary>
    public class EmojiStoryViewModel
    {
        /// <summary>Current puzzle to guess.</summary>
        public EmojiPuzzle CurrentPuzzle { get; set; }

        /// <summary>Multiple-choice answers.</summary>
        public List<string> Choices { get; set; } = new List<string>();

        /// <summary>Current game session state.</summary>
        public EmojiGameSession Session { get; set; } = new EmojiGameSession();

        /// <summary>Whether the game is in progress.</summary>
        public bool IsPlaying { get; set; }

        /// <summary>Whether the game just ended (show results).</summary>
        public bool IsFinished { get; set; }

        /// <summary>Last round result (for feedback).</summary>
        public EmojiRoundResult LastResult { get; set; }

        /// <summary>Selected difficulty filter.</summary>
        public string Difficulty { get; set; }

        /// <summary>All puzzles for browsing mode.</summary>
        public List<EmojiPuzzle> AllPuzzles { get; set; } = new List<EmojiPuzzle>();

        /// <summary>Difficulty counts for filter badges.</summary>
        public Dictionary<string, int> DifficultyCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>Serialized game queue (comma-separated puzzle IDs).</summary>
        public string GameQueue { get; set; }

        /// <summary>Serialized session JSON for hidden form field.</summary>
        public string SessionJson { get; set; }
    }
}
