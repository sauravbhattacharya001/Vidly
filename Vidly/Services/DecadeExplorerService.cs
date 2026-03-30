using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    public class DecadeExplorerService
    {
        private readonly IMovieRepository _movieRepository;

        public DecadeExplorerService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Returns all decades that have at least one movie, sorted descending.
        /// </summary>
        public IReadOnlyList<DecadeSummary> GetAllDecades()
        {
            var movies = _movieRepository.GetAll()
                .Where(m => m.ReleaseDate.HasValue)
                .ToList();

            return movies
                .GroupBy(m => (m.ReleaseDate.Value.Year / 10) * 10)
                .Select(g => BuildSummary(g.Key, g.ToList()))
                .OrderByDescending(d => d.Decade)
                .ToList();
        }

        /// <summary>
        /// Returns detailed info for a single decade.
        /// </summary>
        public DecadeDetail GetDecade(int decade)
        {
            var movies = _movieRepository.GetAll()
                .Where(m => m.ReleaseDate.HasValue
                    && m.ReleaseDate.Value.Year >= decade
                    && m.ReleaseDate.Value.Year < decade + 10)
                .OrderByDescending(m => m.Rating ?? 0)
                .ThenBy(m => m.Name)
                .ToList();

            if (!movies.Any()) return null;

            var genreBreakdown = movies
                .Where(m => m.Genre.HasValue)
                .GroupBy(m => m.Genre.Value)
                .Select(g => new GenreCount { Genre = g.Key, Count = g.Count() })
                .OrderByDescending(gc => gc.Count)
                .ToList();

            var yearBreakdown = movies
                .GroupBy(m => m.ReleaseDate.Value.Year)
                .Select(g => new YearCount { Year = g.Key, Count = g.Count() })
                .OrderBy(yc => yc.Year)
                .ToList();

            return new DecadeDetail
            {
                Decade = decade,
                Label = decade + "s",
                Movies = movies,
                TotalCount = movies.Count,
                AverageRating = movies.Where(m => m.Rating.HasValue).Select(m => m.Rating.Value).DefaultIfEmpty(0).Average(),
                TopRated = movies.Where(m => m.Rating.HasValue).OrderByDescending(m => m.Rating).FirstOrDefault(),
                GenreBreakdown = genreBreakdown,
                YearBreakdown = yearBreakdown,
                DominantGenre = genreBreakdown.FirstOrDefault()?.Genre
            };
        }

        private static DecadeSummary BuildSummary(int decade, List<Movie> movies)
        {
            var rated = movies.Where(m => m.Rating.HasValue).ToList();
            var topGenre = movies
                .Where(m => m.Genre.HasValue)
                .GroupBy(m => m.Genre.Value)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            return new DecadeSummary
            {
                Decade = decade,
                Label = decade + "s",
                MovieCount = movies.Count,
                AverageRating = rated.Any() ? Math.Round(rated.Average(m => m.Rating.Value), 1) : 0,
                TopRatedMovie = rated.OrderByDescending(m => m.Rating).FirstOrDefault()?.Name,
                DominantGenre = topGenre?.Key,
                GenreCount = movies.Where(m => m.Genre.HasValue).Select(m => m.Genre.Value).Distinct().Count()
            };
        }
    }

    public class DecadeSummary
    {
        public int Decade { get; set; }
        public string Label { get; set; }
        public int MovieCount { get; set; }
        public double AverageRating { get; set; }
        public string TopRatedMovie { get; set; }
        public Genre? DominantGenre { get; set; }
        public int GenreCount { get; set; }
    }

    public class DecadeDetail
    {
        public int Decade { get; set; }
        public string Label { get; set; }
        public IReadOnlyList<Movie> Movies { get; set; }
        public int TotalCount { get; set; }
        public double AverageRating { get; set; }
        public Movie TopRated { get; set; }
        public IReadOnlyList<GenreCount> GenreBreakdown { get; set; }
        public IReadOnlyList<YearCount> YearBreakdown { get; set; }
        public Genre? DominantGenre { get; set; }
    }

    public class GenreCount
    {
        public Genre Genre { get; set; }
        public int Count { get; set; }
    }

    public class YearCount
    {
        public int Year { get; set; }
        public int Count { get; set; }
    }
}
