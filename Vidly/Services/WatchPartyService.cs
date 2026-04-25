using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Watch Party planner — create themed movie night plans with
    /// auto-generated snack pairings, runtime estimates, and shareable summaries.
    /// </summary>
    public class WatchPartyService
    {
        private readonly IMovieRepository _movieRepository;
        private static readonly Random _random = new Random();
        private static readonly List<WatchPartyPlan> _savedParties = new List<WatchPartyPlan>();

        public static readonly Dictionary<string, PartyTheme> Themes = new Dictionary<string, PartyTheme>
        {
            ["movie-marathon"] = new PartyTheme(
                "Movie Marathon", "🎬",
                "Back-to-back movies for the dedicated cinephile",
                movieCount: 3,
                new[] { "Popcorn (extra butter)", "Nachos & cheese", "Energy drinks", "Pizza rolls" }),
            ["date-night"] = new PartyTheme(
                "Date Night", "💑",
                "Cozy evening for two with the perfect film",
                movieCount: 1,
                new[] { "Wine & cheese board", "Chocolate-covered strawberries", "Fancy popcorn" }),
            ["family-fun"] = new PartyTheme(
                "Family Fun", "👨‍👩‍👧‍👦",
                "Wholesome entertainment for all ages",
                movieCount: 2,
                new[] { "Popcorn", "Juice boxes", "Cookie platter", "Candy mix" }),
            ["horror-night"] = new PartyTheme(
                "Horror Night", "🎃",
                "Lights off, volume up, pillows ready",
                movieCount: 2,
                new[] { "Gummy worms", "Blood-red punch", "Skull candy", "Chips & salsa" }),
            ["throwback"] = new PartyTheme(
                "Throwback Thursday", "📼",
                "Classics from the golden era of cinema",
                movieCount: 2,
                new[] { "Retro candy", "Root beer floats", "Buttered popcorn", "TV dinners" }),
            ["binge-watch"] = new PartyTheme(
                "Ultimate Binge", "🛋️",
                "Clear your schedule — we're going all day",
                movieCount: 5,
                new[] { "Everything bagel bites", "Family-size chips", "Soda variety pack", "Leftover pizza", "Ice cream" })
        };

        public WatchPartyService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Get all available party themes.
        /// </summary>
        public IReadOnlyList<PartyTheme> GetThemes()
        {
            return Themes.Values.ToList().AsReadOnly();
        }

        /// <summary>
        /// Generate a watch party plan for the given theme.
        /// Picks movies matching the theme's vibe from the catalog.
        /// </summary>
        public WatchPartyPlan GeneratePlan(string themeKey, int? guestCount = null)
        {
            if (string.IsNullOrWhiteSpace(themeKey) || !Themes.ContainsKey(themeKey.ToLowerInvariant()))
                return null;

            var theme = Themes[themeKey.ToLowerInvariant()];
            var allMovies = _movieRepository.GetAll().Where(m => m.Genre.HasValue).ToList();
            var selected = SelectMoviesForTheme(allMovies, theme);
            var guests = guestCount ?? _random.Next(2, 8);

            var estimatedRuntime = selected.Count * 120; // ~2 hours per movie
            var plan = new WatchPartyPlan
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Theme = theme,
                Movies = selected,
                GuestCount = guests,
                SnackSuggestions = theme.Snacks.ToList(),
                EstimatedRuntimeMinutes = estimatedRuntime,
                CreatedAt = DateTime.Now,
                ShareCode = GenerateShareCode()
            };

            _savedParties.Add(plan);
            return plan;
        }

        /// <summary>
        /// Retrieve a saved party plan by share code.
        /// </summary>
        public WatchPartyPlan GetByShareCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            return _savedParties.FirstOrDefault(
                p => p.ShareCode.Equals(code, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get all saved party plans.
        /// </summary>
        public IReadOnlyList<WatchPartyPlan> GetSavedParties()
        {
            return _savedParties
                .OrderByDescending(p => p.CreatedAt)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Get the count of saved parties.
        /// </summary>
        public int GetPartyCount()
        {
            return _savedParties.Count;
        }

        private List<Movie> SelectMoviesForTheme(List<Movie> movies, PartyTheme theme)
        {
            // Genre preferences by theme
            var preferredGenres = GetPreferredGenres(theme.Name);
            var scored = movies
                .Select(m => new
                {
                    Movie = m,
                    Score = (preferredGenres.Contains(m.Genre.Value) ? 2.0 : 0.5)
                           + (m.Rating.HasValue ? m.Rating.Value * 0.2 : 0)
                           + _random.NextDouble() * 0.5
                })
                .OrderByDescending(x => x.Score)
                .Take(theme.MovieCount)
                .Select(x => x.Movie)
                .ToList();

            return scored;
        }

        private static HashSet<Genre> GetPreferredGenres(string themeName)
        {
            switch (themeName)
            {
                case "Date Night":
                    return new HashSet<Genre> { Genre.Romance, Genre.Comedy, Genre.Drama };
                case "Family Fun":
                    return new HashSet<Genre> { Genre.Animation, Genre.Comedy, Genre.Adventure };
                case "Horror Night":
                    return new HashSet<Genre> { Genre.Horror, Genre.Thriller };
                case "Throwback Thursday":
                    return new HashSet<Genre> { Genre.Drama, Genre.Comedy, Genre.Adventure };
                case "Ultimate Binge":
                    return new HashSet<Genre> { Genre.Action, Genre.SciFi, Genre.Adventure, Genre.Thriller };
                default: // Movie Marathon
                    return new HashSet<Genre> { Genre.Action, Genre.SciFi, Genre.Adventure };
            }
        }

        private static string GenerateShareCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[10];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var code = new char[10];
            for (int i = 0; i < code.Length; i++)
                code[i] = chars[bytes[i] % chars.Length];
            return new string(code);
        }
    }

    // ── Supporting types ──

    public class PartyTheme
    {
        public string Name { get; }
        public string Emoji { get; }
        public string Description { get; }
        public int MovieCount { get; }
        public IReadOnlyList<string> Snacks { get; }

        public PartyTheme(string name, string emoji, string description,
            int movieCount, string[] snacks)
        {
            Name = name;
            Emoji = emoji;
            Description = description;
            MovieCount = movieCount;
            Snacks = snacks.ToList().AsReadOnly();
        }
    }

    public class WatchPartyPlan
    {
        public string Id { get; set; }
        public PartyTheme Theme { get; set; }
        public List<Movie> Movies { get; set; }
        public int GuestCount { get; set; }
        public List<string> SnackSuggestions { get; set; }
        public int EstimatedRuntimeMinutes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ShareCode { get; set; }

        /// <summary>
        /// Format runtime as hours and minutes.
        /// </summary>
        public string FormattedRuntime
        {
            get
            {
                var h = EstimatedRuntimeMinutes / 60;
                var m = EstimatedRuntimeMinutes % 60;
                return h > 0
                    ? (m > 0 ? $"{h}h {m}m" : $"{h}h")
                    : $"{m}m";
            }
        }

        /// <summary>
        /// Generate a shareable text summary of the party plan.
        /// </summary>
        public string ToShareText()
        {
            var lines = new List<string>
            {
                $"{Theme.Emoji} Watch Party: {Theme.Name}",
                $"Movies ({Movies.Count}):"
            };
            foreach (var m in Movies)
            {
                var stars = m.Rating.HasValue ? new string('★', m.Rating.Value) : "";
                lines.Add($"  🎬 {m.Name} {stars}");
            }
            lines.Add($"🍿 Snacks: {string.Join(", ", SnackSuggestions)}");
            lines.Add($"⏱️ ~{FormattedRuntime} total");
            lines.Add($"👥 {GuestCount} guests");
            lines.Add($"Share code: {ShareCode}");
            return string.Join("\n", lines);
        }
    }
}
