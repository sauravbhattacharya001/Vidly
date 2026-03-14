using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Matches movies to customer moods using genre affinity mappings.
    /// Tracks mood history, provides mood-based recommendations, and
    /// analyzes mood patterns over time.
    /// </summary>
    public class MoodMatcherService
    {
        private readonly IMovieRepository _movieRepository;
        private readonly List<MoodEntry> _moodLog = new List<MoodEntry>();

        /// <summary>Weight given to mood affinity (vs rating) in combined scoring.</summary>
        public const double MoodWeight = 0.65;

        /// <summary>Weight given to movie rating in combined scoring.</summary>
        public const double RatingWeight = 0.35;

        /// <summary>Default number of recommendations to return.</summary>
        public const int DefaultLimit = 10;

        private static readonly Dictionary<Mood, Dictionary<Genre, double>> MoodAffinities =
            new Dictionary<Mood, Dictionary<Genre, double>>
            {
                [Mood.Happy] = new Dictionary<Genre, double>
                {
                    [Genre.Comedy] = 1.0, [Genre.Animation] = 0.8, [Genre.Adventure] = 0.6, [Genre.Romance] = 0.5
                },
                [Mood.Sad] = new Dictionary<Genre, double>
                {
                    [Genre.Drama] = 1.0, [Genre.Romance] = 0.7, [Genre.Documentary] = 0.4
                },
                [Mood.Excited] = new Dictionary<Genre, double>
                {
                    [Genre.Action] = 1.0, [Genre.Adventure] = 0.9, [Genre.SciFi] = 0.7, [Genre.Thriller] = 0.6
                },
                [Mood.Relaxed] = new Dictionary<Genre, double>
                {
                    [Genre.Comedy] = 0.7, [Genre.Animation] = 0.9, [Genre.Documentary] = 0.8, [Genre.Romance] = 0.5
                },
                [Mood.Scared] = new Dictionary<Genre, double>
                {
                    [Genre.Horror] = 1.0, [Genre.Thriller] = 0.8, [Genre.SciFi] = 0.4
                },
                [Mood.Romantic] = new Dictionary<Genre, double>
                {
                    [Genre.Romance] = 1.0, [Genre.Drama] = 0.7, [Genre.Comedy] = 0.5
                },
                [Mood.Curious] = new Dictionary<Genre, double>
                {
                    [Genre.Documentary] = 1.0, [Genre.SciFi] = 0.8, [Genre.Thriller] = 0.5, [Genre.Drama] = 0.4
                },
                [Mood.Nostalgic] = new Dictionary<Genre, double>
                {
                    [Genre.Drama] = 0.8, [Genre.Comedy] = 0.7, [Genre.Animation] = 0.9, [Genre.Romance] = 0.6
                },
                [Mood.Stressed] = new Dictionary<Genre, double>
                {
                    [Genre.Comedy] = 1.0, [Genre.Animation] = 0.8, [Genre.Adventure] = 0.5
                },
                [Mood.Bored] = new Dictionary<Genre, double>
                {
                    [Genre.Action] = 0.9, [Genre.Thriller] = 0.8, [Genre.Horror] = 0.7, [Genre.SciFi] = 0.6, [Genre.Adventure] = 0.8
                },
                [Mood.Adventurous] = new Dictionary<Genre, double>
                {
                    [Genre.Adventure] = 1.0, [Genre.Action] = 0.8, [Genre.SciFi] = 0.7
                },
                [Mood.Thoughtful] = new Dictionary<Genre, double>
                {
                    [Genre.Documentary] = 0.9, [Genre.Drama] = 1.0, [Genre.SciFi] = 0.5
                }
            };

        public MoodMatcherService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        // -------------------------------------------------------------------
        // Mood → Genre mappings
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns all genre affinities for a given mood, ordered by affinity descending.
        /// </summary>
        public IReadOnlyList<MoodGenreMapping> GetMappings(Mood mood)
        {
            if (!MoodAffinities.TryGetValue(mood, out var genres))
                return new List<MoodGenreMapping>();

            return genres
                .OrderByDescending(g => g.Value)
                .Select(g => new MoodGenreMapping { Mood = mood, Genre = g.Key, Affinity = g.Value })
                .ToList();
        }

        /// <summary>
        /// Returns all moods that have affinity for a given genre, ordered by affinity descending.
        /// </summary>
        public IReadOnlyList<MoodGenreMapping> GetMoodsForGenre(Genre genre)
        {
            return MoodAffinities
                .Where(m => m.Value.ContainsKey(genre))
                .Select(m => new MoodGenreMapping { Mood = m.Key, Genre = genre, Affinity = m.Value[genre] })
                .OrderByDescending(m => m.Affinity)
                .ToList();
        }

        // -------------------------------------------------------------------
        // Recommendations
        // -------------------------------------------------------------------

        /// <summary>
        /// Recommends movies based on the given mood. Scores each movie by
        /// genre affinity × mood weight + normalised rating × rating weight.
        /// </summary>
        public IReadOnlyList<MoodRecommendation> Recommend(Mood mood, int limit = DefaultLimit)
        {
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

            var affinities = MoodAffinities.ContainsKey(mood) ? MoodAffinities[mood] : new Dictionary<Genre, double>();
            if (!affinities.Any())
                return new List<MoodRecommendation>();

            var allMovies = _movieRepository.GetAll();
            var results = new List<MoodRecommendation>();

            foreach (var movie in allMovies)
            {
                if (!movie.Genre.HasValue) continue;
                if (!affinities.TryGetValue(movie.Genre.Value, out var affinity)) continue;

                double normRating = movie.Rating.HasValue ? movie.Rating.Value / 5.0 : 0.5;
                double combined = affinity * MoodWeight + normRating * RatingWeight;

                results.Add(new MoodRecommendation
                {
                    Movie = movie,
                    MoodScore = affinity,
                    CombinedScore = Math.Round(combined, 4),
                    MatchedGenre = movie.Genre.Value
                });
            }

            return results
                .OrderByDescending(r => r.CombinedScore)
                .ThenByDescending(r => r.MoodScore)
                .ThenBy(r => r.Movie.Name)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// Recommends movies based on a blend of multiple moods with weights.
        /// Each mood contributes proportionally to the final score.
        /// </summary>
        public IReadOnlyList<MoodRecommendation> RecommendBlend(
            Dictionary<Mood, double> moodWeights, int limit = DefaultLimit)
        {
            if (moodWeights == null || !moodWeights.Any())
                throw new ArgumentException("At least one mood is required.", nameof(moodWeights));
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

            // Normalise weights to sum to 1.0
            double totalWeight = moodWeights.Values.Sum();
            if (totalWeight <= 0)
                throw new ArgumentException("Mood weights must be positive.", nameof(moodWeights));

            var normalised = moodWeights.ToDictionary(kv => kv.Key, kv => kv.Value / totalWeight);

            // Aggregate genre affinities across moods
            var genreScores = new Dictionary<Genre, double>();
            foreach (var kv in normalised)
            {
                if (!MoodAffinities.ContainsKey(kv.Key)) continue;
                foreach (var ga in MoodAffinities[kv.Key])
                {
                    if (!genreScores.ContainsKey(ga.Key))
                        genreScores[ga.Key] = 0;
                    genreScores[ga.Key] += ga.Value * kv.Value;
                }
            }

            var allMovies = _movieRepository.GetAll();
            var results = new List<MoodRecommendation>();

            foreach (var movie in allMovies)
            {
                if (!movie.Genre.HasValue) continue;
                if (!genreScores.TryGetValue(movie.Genre.Value, out var affinity)) continue;

                double normRating = movie.Rating.HasValue ? movie.Rating.Value / 5.0 : 0.5;
                double combined = Math.Round(affinity * MoodWeight + normRating * RatingWeight, 4);

                results.Add(new MoodRecommendation
                {
                    Movie = movie,
                    MoodScore = Math.Round(affinity, 4),
                    CombinedScore = combined,
                    MatchedGenre = movie.Genre.Value
                });
            }

            return results
                .OrderByDescending(r => r.CombinedScore)
                .ThenBy(r => r.Movie.Name)
                .Take(limit)
                .ToList();
        }

        // -------------------------------------------------------------------
        // Mood logging
        // -------------------------------------------------------------------

        /// <summary>
        /// Logs a mood entry for a customer.
        /// </summary>
        public MoodEntry LogMood(int customerId, Mood mood, string note = null)
        {
            if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));

            var entry = new MoodEntry
            {
                CustomerId = customerId,
                Mood = mood,
                Timestamp = DateTime.UtcNow,
                Note = note
            };
            _moodLog.Add(entry);
            return entry;
        }

        /// <summary>
        /// Returns mood history for a customer, most recent first.
        /// </summary>
        public IReadOnlyList<MoodEntry> GetMoodHistory(int customerId, int? limit = null)
        {
            var query = _moodLog
                .Where(e => e.CustomerId == customerId)
                .OrderByDescending(e => e.Timestamp);

            return limit.HasValue ? query.Take(limit.Value).ToList() : query.ToList();
        }

        /// <summary>
        /// Returns the most recent mood for a customer, or null if none logged.
        /// </summary>
        public MoodEntry GetCurrentMood(int customerId)
        {
            return _moodLog
                .Where(e => e.CustomerId == customerId)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefault();
        }

        /// <summary>
        /// Recommends movies based on the customer's most recently logged mood.
        /// Returns empty list if no mood has been logged.
        /// </summary>
        public IReadOnlyList<MoodRecommendation> RecommendForCustomer(int customerId, int limit = DefaultLimit)
        {
            var current = GetCurrentMood(customerId);
            if (current == null) return new List<MoodRecommendation>();
            return Recommend(current.Mood, limit);
        }

        // -------------------------------------------------------------------
        // Analytics
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns mood frequency statistics for a customer.
        /// </summary>
        public MoodStats GetMoodStats(int customerId)
        {
            var entries = _moodLog.Where(e => e.CustomerId == customerId).ToList();
            var stats = new MoodStats { CustomerId = customerId, TotalEntries = entries.Count };

            if (!entries.Any()) return stats;

            var groups = entries.GroupBy(e => e.Mood).ToDictionary(g => g.Key, g => g.Count());
            stats.MoodCounts = groups;
            stats.MoodPercentages = groups.ToDictionary(
                kv => kv.Key,
                kv => Math.Round(100.0 * kv.Value / entries.Count, 1));
            stats.MostFrequentMood = groups.OrderByDescending(kv => kv.Value).First().Key;

            var span = entries.Max(e => e.Timestamp) - entries.Min(e => e.Timestamp);
            stats.EntriesPerWeek = span.TotalDays >= 7
                ? Math.Round(entries.Count / (span.TotalDays / 7), 2)
                : entries.Count;

            return stats;
        }

        /// <summary>
        /// Returns daily mood trends for a customer over a date range.
        /// </summary>
        public IReadOnlyList<MoodTrend> GetMoodTrends(int customerId, DateTime from, DateTime to)
        {
            if (from > to) throw new ArgumentException("'from' must be before 'to'.");

            var entries = _moodLog
                .Where(e => e.CustomerId == customerId && e.Timestamp >= from && e.Timestamp <= to)
                .ToList();

            return entries
                .GroupBy(e => e.Timestamp.Date)
                .Select(g => new MoodTrend
                {
                    Date = g.Key,
                    DominantMood = g.GroupBy(e => e.Mood)
                        .OrderByDescending(mg => mg.Count())
                        .First().Key,
                    EntryCount = g.Count()
                })
                .OrderBy(t => t.Date)
                .ToList();
        }

        /// <summary>
        /// Returns all supported moods with their genre mappings.
        /// </summary>
        public IReadOnlyList<MoodGenreMapping> GetAllMappings()
        {
            return MoodAffinities
                .SelectMany(m => m.Value.Select(g => new MoodGenreMapping
                {
                    Mood = m.Key,
                    Genre = g.Key,
                    Affinity = g.Value
                }))
                .OrderBy(m => m.Mood)
                .ThenByDescending(m => m.Affinity)
                .ToList();
        }

        /// <summary>
        /// Suggests the best mood for a customer who wants to watch a specific genre.
        /// </summary>
        public Mood? SuggestMoodForGenre(Genre genre)
        {
            var moods = GetMoodsForGenre(genre);
            return moods.Any() ? moods.First().Mood : (Mood?)null;
        }
    }
}
