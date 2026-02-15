using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory movie repository.
    /// Uses Dictionary for O(1) lookups by ID and an atomic counter
    /// for ID generation instead of O(n) Max() scans.
    /// </summary>
    public class InMemoryMovieRepository : IMovieRepository
    {
        private static readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>
        {
            [1] = new Movie { Id = 1, Name = "Shrek!", ReleaseDate = new DateTime(2001, 5, 18), Genre = Genre.Animation, Rating = 4 },
            [2] = new Movie { Id = 2, Name = "The Godfather", ReleaseDate = new DateTime(1972, 3, 24), Genre = Genre.Drama, Rating = 5 },
            [3] = new Movie { Id = 3, Name = "Toy Story", ReleaseDate = new DateTime(1995, 11, 22), Genre = Genre.Animation, Rating = 5 }
        };

        private static readonly object _lock = new object();
        private static readonly Random _random = new Random();
        private static int _nextId = 4;

        public Movie GetById(int id)
        {
            lock (_lock)
            {
                return _movies.TryGetValue(id, out var movie) ? Clone(movie) : null;
            }
        }

        public IReadOnlyList<Movie> GetAll()
        {
            lock (_lock)
            {
                return _movies.Values.Select(Clone).ToList().AsReadOnly();
            }
        }

        public void Add(Movie movie)
        {
            if (movie == null)
                throw new ArgumentNullException(nameof(movie));

            lock (_lock)
            {
                movie.Id = _nextId++;
                _movies[movie.Id] = movie;
            }
        }

        public void Update(Movie movie)
        {
            if (movie == null)
                throw new ArgumentNullException(nameof(movie));

            lock (_lock)
            {
                if (!_movies.TryGetValue(movie.Id, out var existing))
                    throw new KeyNotFoundException(
                        $"Movie with Id {movie.Id} not found.");

                existing.Name = movie.Name;
                existing.ReleaseDate = movie.ReleaseDate;
                existing.Genre = movie.Genre;
                existing.Rating = movie.Rating;
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                if (!_movies.Remove(id))
                    throw new KeyNotFoundException(
                        $"Movie with Id {id} not found.");
            }
        }

        public IReadOnlyList<Movie> GetByReleaseDate(int year, int month)
        {
            lock (_lock)
            {
                return _movies.Values
                    .Where(m => m.ReleaseDate.HasValue
                             && m.ReleaseDate.Value.Year == year
                             && m.ReleaseDate.Value.Month == month)
                    .OrderBy(m => m.ReleaseDate)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating)
        {
            lock (_lock)
            {
                IEnumerable<Movie> results = _movies.Values;

                if (!string.IsNullOrWhiteSpace(query))
                {
                    results = results.Where(m =>
                        m.Name != null &&
                        m.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (genre.HasValue)
                {
                    results = results.Where(m => m.Genre == genre.Value);
                }

                if (minRating.HasValue)
                {
                    results = results.Where(m =>
                        m.Rating.HasValue && m.Rating.Value >= minRating.Value);
                }

                return results
                    .OrderBy(m => m.Name)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public Movie GetRandom()
        {
            lock (_lock)
            {
                if (_movies.Count == 0)
                    return null;

                // Convert to list once for indexed access
                var values = _movies.Values.ToList();
                return Clone(values[_random.Next(values.Count)]);
            }
        }

        /// <summary>
        /// Creates a defensive copy of a Movie to prevent callers from
        /// mutating the internal store outside the lock.
        /// </summary>
        private static Movie Clone(Movie source)
        {
            return new Movie
            {
                Id = source.Id,
                Name = source.Name,
                ReleaseDate = source.ReleaseDate,
                Genre = source.Genre,
                Rating = source.Rating
            };
        }
    }
}
