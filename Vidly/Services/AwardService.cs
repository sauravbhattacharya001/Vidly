using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Manages movie award nominations and wins — add, remove, query, and
    /// generate award summaries and leaderboards.
    /// </summary>
    public class AwardService
    {
        private readonly IMovieRepository _movieRepository;
        private static readonly List<AwardNomination> _nominations = new List<AwardNomination>();
        private static readonly object _lock = new object();
        private static int _nextId = 1;

        public AwardService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        public AwardNomination AddNomination(int movieId, AwardBody body, AwardCategory category,
            int year, string nominee = null, bool won = false)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie with ID {movieId} not found.", nameof(movieId));

            if (year < 1927 || year > 2100)
                throw new ArgumentOutOfRangeException(nameof(year), "Year must be between 1927 and 2100.");

            lock (_lock)
            {
                var exists = _nominations.Any(n =>
                    n.MovieId == movieId &&
                    n.AwardBody == body &&
                    n.Category == category &&
                    n.Year == year);

                if (exists)
                    throw new InvalidOperationException(
                        "A nomination already exists for this movie, award body, category, and year.");

                var nomination = new AwardNomination
                {
                    Id = _nextId++,
                    MovieId = movieId,
                    MovieName = movie.Name,
                    AwardBody = body,
                    Category = category,
                    Year = year,
                    Nominee = nominee?.Trim(),
                    Won = won,
                    CreatedAt = DateTime.UtcNow
                };

                _nominations.Add(nomination);
                return nomination;
            }
        }

        public AwardNomination SetWon(int nominationId, bool won)
        {
            lock (_lock)
            {
                var nomination = _nominations.FirstOrDefault(n => n.Id == nominationId);
                if (nomination == null)
                    throw new ArgumentException($"Nomination {nominationId} not found.", nameof(nominationId));

                nomination.Won = won;
                return nomination;
            }
        }

        public bool RemoveNomination(int nominationId)
        {
            lock (_lock)
            {
                return _nominations.RemoveAll(n => n.Id == nominationId) > 0;
            }
        }

        public List<AwardNomination> GetNominations(
            int? movieId = null,
            AwardBody? body = null,
            AwardCategory? category = null,
            int? year = null,
            bool? wonOnly = null)
        {
            lock (_lock)
            {
                IEnumerable<AwardNomination> query = _nominations;

                if (movieId.HasValue)
                    query = query.Where(n => n.MovieId == movieId.Value);
                if (body.HasValue)
                    query = query.Where(n => n.AwardBody == body.Value);
                if (category.HasValue)
                    query = query.Where(n => n.Category == category.Value);
                if (year.HasValue)
                    query = query.Where(n => n.Year == year.Value);
                if (wonOnly == true)
                    query = query.Where(n => n.Won);

                return query.OrderByDescending(n => n.Year)
                    .ThenBy(n => n.AwardBody)
                    .ThenBy(n => n.Category)
                    .ToList();
            }
        }

        public AwardNomination GetById(int nominationId)
        {
            lock (_lock)
            {
                return _nominations.FirstOrDefault(n => n.Id == nominationId);
            }
        }

        public List<AwardSummary> GetLeaderboard()
        {
            lock (_lock)
            {
                return _nominations
                    .GroupBy(n => new { n.MovieId, n.MovieName })
                    .Select(g => new AwardSummary
                    {
                        MovieId = g.Key.MovieId,
                        MovieName = g.Key.MovieName,
                        TotalNominations = g.Count(),
                        TotalWins = g.Count(n => n.Won)
                    })
                    .OrderByDescending(s => s.TotalWins)
                    .ThenByDescending(s => s.TotalNominations)
                    .ThenBy(s => s.MovieName)
                    .ToList();
            }
        }

        public AwardSummary GetMovieSummary(int movieId)
        {
            var movie = _movieRepository.GetById(movieId);
            if (movie == null)
                throw new ArgumentException($"Movie with ID {movieId} not found.", nameof(movieId));

            lock (_lock)
            {
                var noms = _nominations.Where(n => n.MovieId == movieId).ToList();
                return new AwardSummary
                {
                    MovieId = movieId,
                    MovieName = movie.Name,
                    TotalNominations = noms.Count,
                    TotalWins = noms.Count(n => n.Won)
                };
            }
        }

        public List<int> GetAwardYears()
        {
            lock (_lock)
            {
                return _nominations.Select(n => n.Year).Distinct().OrderByDescending(y => y).ToList();
            }
        }

        public Dictionary<AwardBody, int> GetCountsByBody()
        {
            lock (_lock)
            {
                return _nominations
                    .GroupBy(n => n.AwardBody)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
        }

        internal void ClearAll()
        {
            lock (_lock)
            {
                _nominations.Clear();
                _nextId = 1;
            }
        }
    }
}
