using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Generates movie drinking game rule sets based on genre tropes and
    /// selected difficulty.
    /// </summary>
    public class DrinkingGameService
    {
        private static readonly Random _random = new Random();

        /// <summary>
        /// Genre-specific rule templates. Each tuple is (Trigger, Action, Category, Frequency).
        /// </summary>
        private static readonly Dictionary<string, List<(string Trigger, string Action, RuleCategory Cat, int Freq)>> _genreRules
            = new Dictionary<string, List<(string, string, RuleCategory, int)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Action"] = new List<(string, string, RuleCategory, int)>
            {
                ("An explosion fills the screen", "Take a sip", RuleCategory.Cliche, 4),
                ("The hero walks away from an explosion without looking", "Take two sips", RuleCategory.Character, 2),
                ("Someone says \"We don't have much time\"", "Take a sip", RuleCategory.Plot, 3),
                ("A vehicle flips or crashes dramatically", "Take a sip", RuleCategory.Cliche, 3),
                ("The villain monologues instead of finishing the hero", "Finish your drink", RuleCategory.Challenge, 1),
                ("A gun never runs out of ammo", "Take a sip", RuleCategory.Cliche, 3),
                ("Someone dives in slow motion", "Take a sip", RuleCategory.Genre, 2),
                ("The hero gets punched but barely reacts", "Take a sip", RuleCategory.Character, 3),
            },
            ["Comedy"] = new List<(string, string, RuleCategory, int)>
            {
                ("A character breaks the fourth wall", "Take a sip", RuleCategory.Character, 2),
                ("An awkward silence plays for laughs", "Take a sip", RuleCategory.Cliche, 3),
                ("Someone falls or trips", "Take a sip", RuleCategory.Cliche, 3),
                ("A misunderstanding drives the plot forward", "Take two sips", RuleCategory.Plot, 2),
                ("You actually laugh out loud", "Take a sip", RuleCategory.Challenge, 4),
                ("A character does a double-take", "Take a sip", RuleCategory.Cliche, 3),
                ("Someone gets food or drink in their face", "Finish your drink", RuleCategory.Challenge, 1),
                ("A running gag recurs", "Take a sip", RuleCategory.Genre, 3),
            },
            ["Drama"] = new List<(string, string, RuleCategory, int)>
            {
                ("Someone stares out a rainy window", "Take a sip", RuleCategory.Cliche, 2),
                ("A dramatic music swell plays", "Take a sip", RuleCategory.Genre, 4),
                ("Someone cries", "Take a sip", RuleCategory.Character, 3),
                ("A character delivers a monologue about life", "Take two sips", RuleCategory.Character, 2),
                ("There's a flashback sequence", "Take a sip", RuleCategory.Plot, 2),
                ("Someone slams a door", "Take a sip", RuleCategory.Cliche, 2),
                ("A secret is revealed", "Finish your drink", RuleCategory.Challenge, 1),
                ("Two characters have a heated argument", "Take a sip", RuleCategory.Plot, 3),
            },
            ["Horror"] = new List<(string, string, RuleCategory, int)>
            {
                ("Someone investigates a strange noise alone", "Take a sip", RuleCategory.Cliche, 4),
                ("A jump scare happens", "Take two sips", RuleCategory.Genre, 3),
                ("Someone says \"I'll be right back\"", "Finish your drink", RuleCategory.Challenge, 1),
                ("A character trips while running away", "Take a sip", RuleCategory.Cliche, 2),
                ("The phone/car/flashlight doesn't work", "Take a sip", RuleCategory.Cliche, 3),
                ("You spot the killer/monster before the character does", "Take a sip", RuleCategory.Challenge, 3),
                ("A door creaks open by itself", "Take a sip", RuleCategory.Genre, 2),
                ("Someone says \"It's just the wind\"", "Take a sip", RuleCategory.Cliche, 1),
            },
            ["Sci-Fi"] = new List<(string, string, RuleCategory, int)>
            {
                ("Someone explains the science (real or fake)", "Take a sip", RuleCategory.Genre, 3),
                ("A hologram appears", "Take a sip", RuleCategory.Genre, 2),
                ("Something beeps or boops on a control panel", "Take a sip", RuleCategory.Cliche, 5),
                ("An AI says something ominous", "Take two sips", RuleCategory.Character, 2),
                ("Someone says \"That's impossible\"", "Take a sip", RuleCategory.Cliche, 3),
                ("A countdown timer appears", "Finish your drink before it hits zero", RuleCategory.Challenge, 1),
                ("Zero-gravity physics are ignored", "Take a sip", RuleCategory.Genre, 2),
                ("There's a wormhole or portal", "Take a sip", RuleCategory.Plot, 1),
            },
        };

        /// <summary>
        /// Universal rules that apply to any genre.
        /// </summary>
        private static readonly List<(string Trigger, string Action, RuleCategory Cat, int Freq)> _universalRules
            = new List<(string, string, RuleCategory, int)>
        {
            ("The title of the movie is said in dialogue", "Take two sips", RuleCategory.Plot, 1),
            ("A character's phone rings at a dramatic moment", "Take a sip", RuleCategory.Cliche, 2),
            ("Someone orders or drinks alcohol on screen", "Take a sip (solidarity!)", RuleCategory.Cliche, 2),
            ("A sunset or sunrise is shown", "Take a sip", RuleCategory.Genre, 1),
            ("A character looks directly into the camera", "Take a sip", RuleCategory.Character, 1),
            ("The soundtrack plays a recognizable hit song", "Take a sip", RuleCategory.Genre, 2),
        };

        /// <summary>
        /// Generates a drinking game for the given movie and difficulty.
        /// </summary>
        public DrinkingGame Generate(Movie movie, Difficulty difficulty)
        {
            if (movie == null) throw new ArgumentNullException(nameof(movie));

            var genre = movie.Genre ?? "Drama";
            var pool = new List<(string Trigger, string Action, RuleCategory Cat, int Freq)>();

            // Add genre-specific rules
            if (_genreRules.TryGetValue(genre, out var genreList))
                pool.AddRange(genreList);
            else
                pool.AddRange(_genreRules["Drama"]); // fallback

            // Add universal rules
            pool.AddRange(_universalRules);

            // Shuffle
            pool = pool.OrderBy(_ => _random.Next()).ToList();

            // Pick rules based on difficulty
            int count;
            switch (difficulty)
            {
                case Difficulty.Casual: count = 4; break;
                case Difficulty.Expert: count = 10; break;
                default: count = 7; break;
            }
            count = Math.Min(count, pool.Count);

            var rules = pool.Take(count).Select((r, i) => new DrinkingGameRule
            {
                Id = i + 1,
                Trigger = r.Trigger,
                Action = r.Action,
                Category = r.Cat,
                Frequency = r.Freq,
            }).ToList();

            var estimatedSips = rules.Sum(r =>
            {
                int sipsPer = r.Action.Contains("Finish") ? 5 : r.Action.Contains("two") ? 2 : 1;
                return sipsPer * r.Frequency * 3; // ~3 occurrences per frequency point in a movie
            });

            return new DrinkingGame
            {
                MovieId = movie.Id,
                MovieName = movie.Name,
                Genre = genre,
                Difficulty = difficulty,
                Rules = rules,
                EstimatedSips = estimatedSips,
                Disclaimer = "Please drink responsibly. This is a fun game — substitute any beverage you like! 🥤"
            };
        }
    }
}
