using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Service for the Movie Roulette feature — spins a wheel to pick
    /// a random movie, optionally filtered by genre and minimum rating.
    /// </summary>
    public class RouletteService
    {
        private readonly IMovieRepository _movieRepository;
        private static readonly Random _random = new Random();

        public RouletteService(IMovieRepository movieRepository)
        {
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Picks a random movie matching the optional filters.
        /// Returns null if no movies match.
        /// </summary>
        public RouletteResult Spin(Genre? genre = null, int? minRating = null)
        {
            var candidates = _movieRepository.Search(null, genre, minRating);

            if (candidates.Count == 0)
            {
                return new RouletteResult
                {
                    PickedMovie = null,
                    TotalCandidates = 0,
                    FilterGenre = genre,
                    FilterMinRating = minRating
                };
            }

            var picked = candidates[_random.Next(candidates.Count)];

            return new RouletteResult
            {
                PickedMovie = picked,
                TotalCandidates = candidates.Count,
                FilterGenre = genre,
                FilterMinRating = minRating
            };
        }

        /// <summary>
        /// Gets all movies for the wheel display (up to 12 for visual variety).
        /// </summary>
        public IReadOnlyList<Movie> GetWheelMovies(Genre? genre = null, int? minRating = null)
        {
            var candidates = _movieRepository.Search(null, genre, minRating);
            return candidates.Take(12).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Result of a roulette spin.
    /// </summary>
    public class RouletteResult
    {
        public Movie PickedMovie { get; set; }
        public int TotalCandidates { get; set; }
        public Genre? FilterGenre { get; set; }
        public int? FilterMinRating { get; set; }
    }
}
