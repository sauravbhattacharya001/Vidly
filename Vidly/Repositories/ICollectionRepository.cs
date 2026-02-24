using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Repository for managing movie collections (curated themed lists).
    /// </summary>
    public interface ICollectionRepository : IRepository<MovieCollection>
    {
        /// <summary>
        /// Returns all published (visible) collections.
        /// </summary>
        IReadOnlyList<MovieCollection> GetPublished();

        /// <summary>
        /// Searches collections by name (case-insensitive substring match).
        /// </summary>
        IReadOnlyList<MovieCollection> Search(string query);

        /// <summary>
        /// Finds a collection by its exact name (case-insensitive).
        /// Returns null if not found.
        /// </summary>
        MovieCollection GetByName(string name);

        /// <summary>
        /// Adds a movie to a collection. Returns false if the collection
        /// doesn't exist or the movie is already in the collection.
        /// </summary>
        bool AddMovie(int collectionId, int movieId, string note = null);

        /// <summary>
        /// Removes a movie from a collection. Returns false if the collection
        /// doesn't exist or the movie is not in the collection.
        /// </summary>
        bool RemoveMovie(int collectionId, int movieId);

        /// <summary>
        /// Reorders a movie within a collection to the given position.
        /// Returns false if the collection doesn't exist or the movie is not in the collection.
        /// </summary>
        bool ReorderMovie(int collectionId, int movieId, int newPosition);

        /// <summary>
        /// Returns all collections that contain the specified movie.
        /// </summary>
        IReadOnlyList<MovieCollection> GetCollectionsContaining(int movieId);
    }
}
