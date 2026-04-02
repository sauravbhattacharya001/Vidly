using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Manages emoji movie puzzles — built-in library of 30 puzzles,
    /// random selection, answer checking, and scoring.
    /// </summary>
    public class EmojiStoryService
    {
        private readonly List<EmojiPuzzle> _puzzles;
        private readonly Random _random = new Random();

        public EmojiStoryService()
        {
            _puzzles = BuildPuzzleLibrary();
        }

        /// <summary>Gets all puzzles.</summary>
        public List<EmojiPuzzle> GetAll() => _puzzles.ToList();

        /// <summary>Gets a puzzle by ID.</summary>
        public EmojiPuzzle GetById(int id) =>
            _puzzles.FirstOrDefault(p => p.Id == id);

        /// <summary>Gets a random set of puzzles for a game session.</summary>
        public List<EmojiPuzzle> GetGameSet(int count = 10, string difficulty = null)
        {
            var pool = _puzzles.AsEnumerable();
            if (!string.IsNullOrEmpty(difficulty))
                pool = pool.Where(p => p.Difficulty.Equals(difficulty, StringComparison.OrdinalIgnoreCase));

            return pool.OrderBy(_ => _random.Next()).Take(count).ToList();
        }

        /// <summary>Gets 4 answer choices for a puzzle (1 correct + 3 random).</summary>
        public List<string> GetChoices(EmojiPuzzle puzzle)
        {
            var wrong = _puzzles
                .Where(p => p.Id != puzzle.Id)
                .OrderBy(_ => _random.Next())
                .Take(3)
                .Select(p => p.MovieName)
                .ToList();

            wrong.Add(puzzle.MovieName);
            return wrong.OrderBy(_ => _random.Next()).ToList();
        }

        /// <summary>Checks if the guess matches the movie (fuzzy match).</summary>
        public bool CheckAnswer(int puzzleId, string guess)
        {
            var puzzle = GetById(puzzleId);
            if (puzzle == null || string.IsNullOrWhiteSpace(guess))
                return false;

            puzzle.TimesAttempted++;

            var answer = puzzle.MovieName.Trim().ToLowerInvariant();
            var playerGuess = guess.Trim().ToLowerInvariant();

            // Exact or close-enough match
            bool correct = answer == playerGuess
                || answer.Replace("the ", "").Replace("  ", " ") == playerGuess.Replace("the ", "").Replace("  ", " ");

            if (correct)
                puzzle.TimesSolved++;

            return correct;
        }

        /// <summary>Calculates points for a round.</summary>
        public int CalculatePoints(string difficulty, bool hintUsed, int streak)
        {
            int basePoints;
            switch ((difficulty ?? "").ToLowerInvariant())
            {
                case "hard": basePoints = 30; break;
                case "medium": basePoints = 20; break;
                default: basePoints = 10; break;
            }

            if (hintUsed)
                basePoints = (int)(basePoints * 0.5);

            // Streak bonus: +5 per consecutive correct answer
            int streakBonus = streak > 1 ? (streak - 1) * 5 : 0;

            return basePoints + streakBonus;
        }

        /// <summary>Gets difficulty stats.</summary>
        public Dictionary<string, int> GetDifficultyCounts() =>
            _puzzles.GroupBy(p => p.Difficulty)
                    .ToDictionary(g => g.Key, g => g.Count());

        private List<EmojiPuzzle> BuildPuzzleLibrary()
        {
            int id = 1;
            return new List<EmojiPuzzle>
            {
                // Easy
                P(id++, "The Lion King", Genre.Animation, "🦁👑🌅🐗🐒", "Easy", "Disney classic about a lion cub"),
                P(id++, "Titanic", Genre.Romance, "🚢❄️💑🎻💔", "Easy", "An unsinkable ship story"),
                P(id++, "Jaws", Genre.Thriller, "🦈🏊🏖️😱🚤", "Easy", "You're gonna need a bigger boat"),
                P(id++, "E.T.", Genre.SciFi, "👽🚲🌕👦☎️", "Easy", "Phone home"),
                P(id++, "Finding Nemo", Genre.Animation, "🐠🔍🌊🐢🦷", "Easy", "Just keep swimming"),
                P(id++, "Frozen", Genre.Animation, "❄️👸⛄🎵🏔️", "Easy", "Let it go"),
                P(id++, "Star Wars", Genre.SciFi, "⭐⚔️🌌👨‍👦🤖", "Easy", "A galaxy far far away"),
                P(id++, "Jurassic Park", Genre.SciFi, "🦕🧬🏝️🔬😱", "Easy", "Life finds a way"),
                P(id++, "The Wizard of Oz", Genre.Adventure, "🌪️👠🧙‍♂️🦁🏠", "Easy", "There's no place like home"),
                P(id++, "Toy Story", Genre.Animation, "🤠🚀🧸👦🏠", "Easy", "You've got a friend in me"),

                // Medium
                P(id++, "The Matrix", Genre.SciFi, "💊🕶️🤖💻🥋", "Medium", "Take the red pill"),
                P(id++, "Forrest Gump", Genre.Drama, "🏃🍫🦐🏓🪶", "Medium", "Life is like a box of chocolates"),
                P(id++, "Ghostbusters", Genre.Comedy, "👻🔫🚫🏢🧪", "Medium", "Who you gonna call?"),
                P(id++, "The Godfather", Genre.Drama, "🤵🐴🍝🔫🌹", "Medium", "An offer you can't refuse"),
                P(id++, "Back to the Future", Genre.SciFi, "⏰🚗⚡1️⃣9️⃣", "Medium", "1.21 gigawatts!"),
                P(id++, "Gladiator", Genre.Action, "⚔️🏛️👑🐅💀", "Medium", "Are you not entertained?"),
                P(id++, "The Shining", Genre.Horror, "🪓🏨❄️👯📝", "Medium", "Here's Johnny!"),
                P(id++, "Up", Genre.Animation, "🎈🏠👴🐕🌎", "Medium", "Adventure is out there"),
                P(id++, "Cast Away", Genre.Drama, "✈️💥🏝️🏐🔥", "Medium", "Wilson!"),
                P(id++, "Inception", Genre.SciFi, "💤🌀🏙️🎯⏱️", "Medium", "A dream within a dream"),

                // Hard
                P(id++, "Blade Runner", Genre.SciFi, "🤖🌧️🏙️👁️🕊️", "Hard", "Tears in rain"),
                P(id++, "2001: A Space Odyssey", Genre.SciFi, "🐒🦴🛸🔴👁️", "Hard", "I'm sorry Dave"),
                P(id++, "Spirited Away", Genre.Animation, "👧🏚️🐉🛁👻", "Hard", "Studio Ghibli bathhouse"),
                P(id++, "Memento", Genre.Thriller, "📷🔄🤕📝🔍", "Hard", "Remember Sammy Jankis"),
                P(id++, "The Truman Show", Genre.Drama, "📺🏠🌊🚪😲", "Hard", "Good morning! And in case I don't see ya..."),
                P(id++, "Eternal Sunshine of the Spotless Mind", Genre.Romance, "🧠💊💑❄️📼", "Hard", "Meet me in Montauk"),
                P(id++, "Pan's Labyrinth", Genre.Horror, "🧚🌿👁️🏰🗝️", "Hard", "A fairy tale for grown-ups"),
                P(id++, "Mulholland Drive", Genre.Thriller, "🔑💙🎭💤🌃", "Hard", "A love story in the city of dreams"),
                P(id++, "Arrival", Genre.SciFi, "🛸✋⭕📝👶", "Hard", "If you could see your whole life, would you change things?"),
                P(id++, "Parasite", Genre.Thriller, "🪨🏠📶⬆️⬇️", "Hard", "A tale of two families"),
            };
        }

        private EmojiPuzzle P(int id, string movie, Genre genre, string emojis, string diff, string hint)
        {
            return new EmojiPuzzle
            {
                Id = id,
                MovieId = id,
                MovieName = movie,
                Genre = genre,
                Emojis = emojis,
                Difficulty = diff,
                Hint = hint,
                TimesSolved = 0,
                TimesAttempted = 0
            };
        }
    }
}
