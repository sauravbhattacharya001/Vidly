using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Builds movie marathon plans with scheduling, ordering, and stats.
    /// </summary>
    public class MarathonPlannerService
    {
        private readonly IMovieRepository _movieRepository;
        private static readonly Random _rng = new Random();

        public MarathonPlannerService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Generates a marathon plan from a request.
        /// </summary>
        public MarathonPlan BuildPlan(MarathonRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.MovieIds == null || request.MovieIds.Count == 0)
                return EmptyPlan(request.StartTime);

            // Resolve movies
            var movies = request.MovieIds
                .Select(id => _movieRepository.GetById(id))
                .Where(m => m != null)
                .ToList();

            if (movies.Count == 0)
                return EmptyPlan(request.StartTime);

            // Order movies
            movies = OrderMovies(movies, request.Order);

            // Build schedule
            var entries = new List<MarathonEntry>();
            var current = request.StartTime;

            for (int i = 0; i < movies.Count; i++)
            {
                var movie = movies[i];
                var end = current.AddMinutes(request.AvgRuntimeMinutes);
                bool hasBreak = i < movies.Count - 1 && request.BreakMinutes > 0;

                var entry = new MarathonEntry
                {
                    Position = i + 1,
                    Movie = movie,
                    StartTime = current,
                    EndTime = end,
                    HasBreakAfter = hasBreak,
                    BreakEndTime = hasBreak ? end.AddMinutes(request.BreakMinutes) : (DateTime?)null
                };

                entries.Add(entry);
                current = hasBreak ? end.AddMinutes(request.BreakMinutes) : end;
            }

            // Genre breakdown
            var genreBreakdown = movies
                .Where(m => m.Genre.HasValue)
                .GroupBy(m => m.Genre.Value.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            // Average rating
            var rated = movies.Where(m => m.Rating.HasValue).ToList();
            double? avgRating = rated.Any()
                ? Math.Round(rated.Average(m => m.Rating.Value), 1)
                : (double?)null;

            // Estimated cost (DailyRate or default $3.99)
            decimal cost = movies.Sum(m => m.DailyRate ?? 3.99m);

            var totalWatch = TimeSpan.FromMinutes(movies.Count * request.AvgRuntimeMinutes);
            var totalBreak = TimeSpan.FromMinutes(
                Math.Max(0, movies.Count - 1) * request.BreakMinutes);

            return new MarathonPlan
            {
                Entries = entries,
                OverallStart = request.StartTime,
                OverallEnd = entries.Last().EndTime,
                TotalWatchTime = totalWatch,
                TotalBreakTime = totalBreak,
                GenreBreakdown = genreBreakdown,
                AverageRating = avgRating,
                EstimatedCost = cost
            };
        }

        /// <summary>
        /// Suggests marathon-worthy movies by picking top-rated ones.
        /// </summary>
        public List<Movie> SuggestMovies(int count, Genre? genreFilter = null)
        {
            var all = _movieRepository.GetAll();
            IEnumerable<Movie> query = all;

            if (genreFilter.HasValue)
                query = query.Where(m => m.Genre == genreFilter.Value);

            return query
                .OrderByDescending(m => m.Rating ?? 0)
                .ThenBy(m => m.Name)
                .Take(count)
                .ToList();
        }

        private static List<Movie> OrderMovies(List<Movie> movies, MarathonOrder order)
        {
            switch (order)
            {
                case MarathonOrder.Chronological:
                    return movies.OrderBy(m => m.ReleaseDate ?? DateTime.MaxValue).ToList();
                case MarathonOrder.ReverseChronological:
                    return movies.OrderByDescending(m => m.ReleaseDate ?? DateTime.MinValue).ToList();
                case MarathonOrder.RatingDescending:
                    return movies.OrderByDescending(m => m.Rating ?? 0).ThenBy(m => m.Name).ToList();
                case MarathonOrder.GenreGrouped:
                    return movies.OrderBy(m => m.Genre?.ToString() ?? "ZZZ")
                        .ThenBy(m => m.Name).ToList();
                case MarathonOrder.Random:
                    return movies.OrderBy(_ => _rng.Next()).ToList();
                default:
                    return movies;
            }
        }

        private static MarathonPlan EmptyPlan(DateTime start)
        {
            return new MarathonPlan
            {
                OverallStart = start,
                OverallEnd = start,
                TotalWatchTime = TimeSpan.Zero,
                TotalBreakTime = TimeSpan.Zero
            };
        }
    }
}
