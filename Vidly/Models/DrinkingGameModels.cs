using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Categories of drinking game rules based on what triggers them.
    /// </summary>
    public enum RuleCategory
    {
        /// <summary>Triggered by specific character actions or catchphrases.</summary>
        Character,

        /// <summary>Triggered by recurring visual or audio cues.</summary>
        Cliche,

        /// <summary>Triggered by plot events or twists.</summary>
        Plot,

        /// <summary>Triggered by genre-specific tropes.</summary>
        Genre,

        /// <summary>Bonus/challenge rules for brave participants.</summary>
        Challenge
    }

    /// <summary>
    /// Difficulty tiers that control how many and how intense rules are.
    /// </summary>
    public enum Difficulty
    {
        /// <summary>Casual viewing — few, easy-to-spot rules.</summary>
        Casual,

        /// <summary>Standard game — balanced mix of rules.</summary>
        Standard,

        /// <summary>Expert mode — many rules, requires close attention.</summary>
        Expert
    }

    /// <summary>
    /// A single rule in a movie drinking game.
    /// </summary>
    public class DrinkingGameRule
    {
        public int Id { get; set; }

        /// <summary>The trigger description, e.g. "Every time the hero says a one-liner".</summary>
        public string Trigger { get; set; }

        /// <summary>What to do when triggered, e.g. "Take a sip" or "Finish your drink".</summary>
        public string Action { get; set; }

        /// <summary>Category this rule belongs to.</summary>
        public RuleCategory Category { get; set; }

        /// <summary>Estimated frequency: 1 (rare) to 5 (constant).</summary>
        public int Frequency { get; set; }
    }

    /// <summary>
    /// A complete drinking game rule set for a movie.
    /// </summary>
    public class DrinkingGame
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public string Genre { get; set; }
        public Difficulty Difficulty { get; set; }
        public List<DrinkingGameRule> Rules { get; set; } = new List<DrinkingGameRule>();

        /// <summary>Estimated total sips for the full movie.</summary>
        public int EstimatedSips { get; set; }

        /// <summary>Fun disclaimer text.</summary>
        public string Disclaimer { get; set; }
    }
}
