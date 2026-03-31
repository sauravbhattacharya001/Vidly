using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Builds a chronological timeline of movies grouped by release year
    /// with genre breakdowns and optional filtering.
    /// </summary>
    public class TimelineService
    {
        private readonly IMovieRepository _movieRepository;

        public TimelineService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Build the full timeline, optionally filtered by genre.
        /// </summary>
        public TimelineViewModel BuildTimeline(Genre? filterGenre = null)
        {
            var allMovies = _movieRepository.GetAll()
                .Where(m => m.ReleaseDate.HasValue)
                .ToList();

            // Apply genre filter if specified
            var filtered = filterGenre.HasValue
                ? allMovies.Where(m => m.Genre == filterGenre.Value).ToList()
                : allMovies;

            if (!filtered.Any())
            {
                return new TimelineViewModel
                {
                    FilterGenre = filterGenre,
                    GenreCounts = BuildGenreCounts(allMovies)
                };
            }

            var yearGroups = filtered
                .GroupBy(m => m.ReleaseDate.Value.Year)
                .OrderBy(g => g.Key)
                .Select(g => new TimelineYearGroup
                {
                    Year = g.Key,
                    Movies = g.OrderBy(m => m.ReleaseDate.Value)
                        .Select(m => new TimelineEntry
                        {
                            MovieId = m.Id,
                            MovieName = m.Name,
                            Year = m.ReleaseDate.Value.Year,
                            Genre = m.Genre,
                            Rating = m.Rating,
                            IsNewRelease = m.IsNewRelease
                        })
                        .ToList()
                })
                .ToList();

            var years = yearGroups.Select(g => g.Year).ToList();

            return new TimelineViewModel
            {
                YearGroups = yearGroups,
                TotalMovies = filtered.Count,
                EarliestYear = years.Min(),
                LatestYear = years.Max(),
                TotalYearsSpanned = years.Max() - years.Min() + 1,
                GenreCounts = BuildGenreCounts(allMovies),
                FilterGenre = filterGenre
            };
        }

        private Dictionary<string, int> BuildGenreCounts(List<Movie> movies)
        {
            return movies
                .Where(m => m.Genre.HasValue)
                .GroupBy(m => m.Genre.Value.ToString())
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count());
        }
    }
}
