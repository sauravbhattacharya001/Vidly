using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Services
{
    /// <summary>
    /// Business logic for movie collections — summaries, popularity, stats, and suggestions.
    /// </summary>
    public class CollectionService
    {
        private readonly ICollectionRepository _collectionRepository;
        private readonly IMovieRepository _movieRepository;

        public CollectionService(
            ICollectionRepository collectionRepository,
            IMovieRepository movieRepository)
        {
            _collectionRepository = collectionRepository
                ?? throw new ArgumentNullException(nameof(collectionRepository));
            _movieRepository = movieRepository
                ?? throw new ArgumentNullException(nameof(movieRepository));
        }

        /// <summary>
        /// Returns collection details with enriched movie info for each item.
        /// </summary>
        public CollectionSummary GetCollectionSummary(int collectionId)
        {
            var collection = _collectionRepository.GetById(collectionId);
            if (collection == null)
                return null;

            var movieDetails = new List<CollectionMovieDetail>();
            foreach (var item in collection.Items.OrderBy(i => i.SortOrder))
            {
                var movie = _movieRepository.GetById(item.MovieId);
                movieDetails.Add(new CollectionMovieDetail
                {
                    MovieId = item.MovieId,
                    MovieName = movie?.Name ?? "Unknown",
                    Genre = movie?.Genre,
                    Rating = movie?.Rating,
                    SortOrder = item.SortOrder,
                    Note = item.Note
                });
            }

            return new CollectionSummary
            {
                Id = collection.Id,
                Name = collection.Name,
                Description = collection.Description,
                CreatedAt = collection.CreatedAt,
                UpdatedAt = collection.UpdatedAt,
                IsPublished = collection.IsPublished,
                MovieCount = collection.MovieCount,
                Movies = movieDetails
            };
        }

        /// <summary>
        /// Returns the most popular collections sorted by movie count (descending).
        /// </summary>
        public IReadOnlyList<MovieCollection> GetPopularCollections(int count)
        {
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be at least 1.");

            var all = _collectionRepository.GetAll();
            return all
                .OrderByDescending(c => c.MovieCount)
                .ThenBy(c => c.Name)
                .Take(count)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Returns aggregate statistics across all collections.
        /// </summary>
        public CollectionStats GetCollectionStats()
        {
            var all = _collectionRepository.GetAll();

            var genreCounts = new Dictionary<Genre, int>();
            foreach (var collection in all)
            {
                foreach (var item in collection.Items)
                {
                    var movie = _movieRepository.GetById(item.MovieId);
                    if (movie?.Genre != null)
                    {
                        if (genreCounts.ContainsKey(movie.Genre.Value))
                            genreCounts[movie.Genre.Value]++;
                        else
                            genreCounts[movie.Genre.Value] = 1;
                    }
                }
            }

            Genre? mostCommonGenre = genreCounts.Count > 0
                ? genreCounts.OrderByDescending(kv => kv.Value).First().Key
                : (Genre?)null;

            return new CollectionStats
            {
                TotalCollections = all.Count,
                PublishedCount = all.Count(c => c.IsPublished),
                AverageMoviesPerCollection = all.Count > 0
                    ? Math.Round(all.Average(c => (double)c.MovieCount), 1)
                    : 0,
                MostCommonGenre = mostCommonGenre
            };
        }

        /// <summary>
        /// Suggests movies not yet in the collection that share genres
        /// with existing movies in the collection.
        /// </summary>
        public IReadOnlyList<Movie> SuggestMovies(int collectionId)
        {
            var collection = _collectionRepository.GetById(collectionId);
            if (collection == null)
                return new List<Movie>().AsReadOnly();

            var existingMovieIds = new HashSet<int>(collection.Items.Select(i => i.MovieId));

            // Gather genres of existing movies
            var genres = new HashSet<Genre>();
            foreach (var item in collection.Items)
            {
                var movie = _movieRepository.GetById(item.MovieId);
                if (movie?.Genre != null)
                    genres.Add(movie.Genre.Value);
            }

            if (genres.Count == 0)
                return new List<Movie>().AsReadOnly();

            // Find all movies matching those genres that aren't already in the collection
            var allMovies = _movieRepository.GetAll();
            return allMovies
                .Where(m => m.Genre.HasValue &&
                            genres.Contains(m.Genre.Value) &&
                            !existingMovieIds.Contains(m.Id))
                .OrderBy(m => m.Name)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Detailed summary of a collection including enriched movie info.
    /// </summary>
    public class CollectionSummary
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsPublished { get; set; }
        public int MovieCount { get; set; }
        public List<CollectionMovieDetail> Movies { get; set; }
    }

    /// <summary>
    /// Enriched movie info within a collection.
    /// </summary>
    public class CollectionMovieDetail
    {
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? Genre { get; set; }
        public int? Rating { get; set; }
        public int SortOrder { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// Aggregate statistics across all collections.
    /// </summary>
    public class CollectionStats
    {
        public int TotalCollections { get; set; }
        public int PublishedCount { get; set; }
        public double AverageMoviesPerCollection { get; set; }
        public Genre? MostCommonGenre { get; set; }
    }
}
