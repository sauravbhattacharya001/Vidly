using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Matches movies to viewer moods based on genre affinity, rating, and recency.
    /// </summary>
    public class MoodMatcherService
    {
        private readonly IMovieRepository _movieRepository;
        private static readonly Dictionary<Mood, MoodProfile> _profiles;

        static MoodMatcherService()
        {
            _profiles = new Dictionary<Mood, MoodProfile>
            {
                [Mood.Happy] = new MoodProfile
                {
                    Mood = Mood.Happy, DisplayName = "Happy & Upbeat", Emoji = "😄",
                    Description = "You're feeling great! Here are some fun, feel-good movies to keep the vibe going.",
                    ColorHex = "#FFD93D",
                    PreferredGenres = new List<Genre> { Genre.Comedy, Genre.Animation, Genre.Adventure }
                },
                [Mood.Sad] = new MoodProfile
                {
                    Mood = Mood.Sad, DisplayName = "Sad & Reflective", Emoji = "😢",
                    Description = "Sometimes a good drama or heartfelt story is exactly what you need.",
                    ColorHex = "#6C9BCF",
                    PreferredGenres = new List<Genre> { Genre.Drama, Genre.Romance }
                },
                [Mood.Stressed] = new MoodProfile
                {
                    Mood = Mood.Stressed, DisplayName = "Stressed & Anxious", Emoji = "😰",
                    Description = "Unwind with something light, funny, or visually calming.",
                    ColorHex = "#A8D5BA",
                    PreferredGenres = new List<Genre> { Genre.Comedy, Genre.Animation, Genre.Documentary }
                },
                [Mood.Adventurous] = new MoodProfile
                {
                    Mood = Mood.Adventurous, DisplayName = "Adventurous & Bold", Emoji = "🤩",
                    Description = "Ready for thrills! Action-packed adventures await.",
                    ColorHex = "#FF6B35",
                    PreferredGenres = new List<Genre> { Genre.Action, Genre.Adventure, Genre.SciFi }
                },
                [Mood.Tired] = new MoodProfile
                {
                    Mood = Mood.Tired, DisplayName = "Tired & Chill", Emoji = "😴",
                    Description = "Easy-watching comfort movies — nothing too intense.",
                    ColorHex = "#C3B1E1",
                    PreferredGenres = new List<Genre> { Genre.Animation, Genre.Comedy, Genre.Romance }
                },
                [Mood.Curious] = new MoodProfile
                {
                    Mood = Mood.Curious, DisplayName = "Curious & Intellectual", Emoji = "🤔",
                    Description = "Feed your mind with thought-provoking films.",
                    ColorHex = "#4ECDC4",
                    PreferredGenres = new List<Genre> { Genre.Documentary, Genre.SciFi, Genre.Drama },
                    MinRating = 4
                },
                [Mood.Romantic] = new MoodProfile
                {
                    Mood = Mood.Romantic, DisplayName = "Romantic", Emoji = "💕",
                    Description = "Love is in the air — cozy up with a romance.",
                    ColorHex = "#FF69B4",
                    PreferredGenres = new List<Genre> { Genre.Romance, Genre.Drama, Genre.Comedy }
                },
                [Mood.ThrillSeeking] = new MoodProfile
                {
                    Mood = Mood.ThrillSeeking, DisplayName = "Thrill-Seeking", Emoji = "😱",
                    Description = "Heart-pounding suspense and scares — if you dare!",
                    ColorHex = "#2D2D2D",
                    PreferredGenres = new List<Genre> { Genre.Horror, Genre.Thriller, Genre.Action }
                },
                [Mood.FamilyTime] = new MoodProfile
                {
                    Mood = Mood.FamilyTime, DisplayName = "Family Time", Emoji = "👨‍👩‍👧‍👦",
                    Description = "Movies everyone can enjoy together.",
                    ColorHex = "#87CEEB",
                    PreferredGenres = new List<Genre> { Genre.Animation, Genre.Comedy, Genre.Adventure }
                },
                [Mood.NeedALaugh] = new MoodProfile
                {
                    Mood = Mood.NeedALaugh, DisplayName = "Need a Laugh", Emoji = "😂",
                    Description = "Guaranteed laughs — comedy gold incoming!",
                    ColorHex = "#FFA62F",
                    PreferredGenres = new List<Genre> { Genre.Comedy, Genre.Animation }
                }
            };
        }

        public MoodMatcherService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Returns all available mood profiles.
        /// </summary>
        public IReadOnlyList<MoodProfile> GetAllMoods()
        {
            return _profiles.Values.ToList().AsReadOnly();
        }

        /// <summary>
        /// Gets the profile for a specific mood.
        /// </summary>
        public MoodProfile GetMoodProfile(Mood mood)
        {
            return _profiles.TryGetValue(mood, out var profile) ? profile : null;
        }

        /// <summary>
        /// Finds movies matching the given mood, scored by relevance.
        /// </summary>
        /// <param name="mood">The viewer's current mood.</param>
        /// <param name="maxResults">Maximum recommendations to return (default 10).</param>
        public MoodMatchResult GetRecommendations(Mood mood, int maxResults = 10)
        {
            if (maxResults < 1) maxResults = 1;
            if (maxResults > 50) maxResults = 50;

            var profile = GetMoodProfile(mood);
            if (profile == null)
                return new MoodMatchResult();

            var allMovies = _movieRepository.GetAll();
            var scored = new List<MoodRecommendation>();

            foreach (var movie in allMovies)
            {
                double score = 0;
                string reason = "";

                // Genre match (primary signal)
                if (movie.Genre.HasValue && profile.PreferredGenres.Contains(movie.Genre.Value))
                {
                    int genreRank = profile.PreferredGenres.IndexOf(movie.Genre.Value);
                    double genreScore = 50 - (genreRank * 10); // first genre = 50, second = 40, etc.
                    score += genreScore;
                    reason = $"Genre match: {movie.Genre.Value}";
                }

                // Rating bonus
                if (movie.Rating.HasValue)
                {
                    score += movie.Rating.Value * 5; // up to 25 points
                }

                // Min rating filter
                if (profile.MinRating.HasValue && movie.Rating.HasValue && movie.Rating.Value < profile.MinRating.Value)
                {
                    score *= 0.3; // penalize but don't exclude
                }

                // New release bonus
                if (movie.IsNewRelease)
                {
                    score += 10;
                    if (!string.IsNullOrEmpty(reason)) reason += " • ";
                    reason += "New release";
                }

                if (score > 0)
                {
                    scored.Add(new MoodRecommendation
                    {
                        Movie = movie,
                        RelevanceScore = Math.Round(score, 1),
                        MatchReason = reason
                    });
                }
            }

            return new MoodMatchResult
            {
                SelectedMood = profile,
                Recommendations = scored
                    .OrderByDescending(r => r.RelevanceScore)
                    .ThenBy(r => r.Movie.Name)
                    .Take(maxResults)
                    .ToList(),
                TotalMoviesScanned = allMovies.Count
            };
        }
    }
}
