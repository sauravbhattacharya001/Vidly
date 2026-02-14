using System.Collections.Generic;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Movie-specific repository with query methods beyond basic CRUD.
    /// </summary>
    public interface IMovieRepository : IRepository<Movie>
    {
        /// <summary>
        /// Returns movies released in the given year and month, ordered by release date.
        /// </summary>
        IReadOnlyList<Movie> GetByReleaseDate(int year, int month);

        /// <summary>
        /// Returns a random movie from the store, or null if no movies exist.
        /// </summary>
        Movie GetRandom();
    }
}
