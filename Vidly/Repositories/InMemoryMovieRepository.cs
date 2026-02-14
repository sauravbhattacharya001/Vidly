using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory movie repository.
    /// Encapsulates the static movie store and all locking logic
    /// previously embedded in MoviesController.
    /// </summary>
    public class InMemoryMovieRepository : IMovieRepository
    {
        private static readonly List<Movie> _movies = new List<Movie>
        {
            new Movie { Id = 1, Name = "Shrek!", ReleaseDate = new DateTime(2001, 5, 18), Genre = Genre.Animation, Rating = 4 },
            new Movie { Id = 2, Name = "The Godfather", ReleaseDate = new DateTime(1972, 3, 24), Genre = Genre.Drama, Rating = 5 },
            new Movie { Id = 3, Name = "Toy Story", ReleaseDate = new DateTime(1995, 11, 22), Genre = Genre.Animation, Rating = 5 }
        };

        private static readonly object _lock = new object();
        private static readonly Random _random = new Random();

        public Movie GetById(int id)
        {
            lock (_lock)
            {
                var movie = _movies.SingleOrDefault(m => m.Id == id);
                return movie == null ? null : Clone(movie);
            }
        }

        public IReadOnlyList<Movie> GetAll()
        {
            lock (_lock)
            {
                return _movies.Select(Clone).ToList().AsReadOnly();
            }
        }

        public void Add(Movie movie)
        {
            if (movie == null)
                throw new ArgumentNullException(nameof(movie));

            lock (_lock)
            {
                movie.Id = _movies.Any() ? _movies.Max(m => m.Id) + 1 : 1;
                _movies.Add(movie);
            }
        }

        public void Update(Movie movie)
        {
            if (movie == null)
                throw new ArgumentNullException(nameof(movie));

            lock (_lock)
            {
                var existing = _movies.SingleOrDefault(m => m.Id == movie.Id);
                if (existing == null)
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
                var movie = _movies.SingleOrDefault(m => m.Id == id);
                if (movie == null)
                    throw new KeyNotFoundException(
                        $"Movie with Id {id} not found.");

                _movies.Remove(movie);
            }
        }

        public IReadOnlyList<Movie> GetByReleaseDate(int year, int month)
        {
            lock (_lock)
            {
                return _movies
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
                IEnumerable<Movie> results = _movies;

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
                if (!_movies.Any())
                    return null;

                return Clone(_movies[_random.Next(_movies.Count)]);
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
