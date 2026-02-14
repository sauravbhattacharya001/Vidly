using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;

namespace Vidly.Tests
{
    [TestClass]
    public class InMemoryMovieRepositoryTests
    {
        private InMemoryMovieRepository _repo;

        [TestInitialize]
        public void SetUp()
        {
            _repo = new InMemoryMovieRepository();
        }

        // ---------- GetAll ----------

        [TestMethod]
        public void GetAll_ReturnsPreSeededMovies()
        {
            var movies = _repo.GetAll();

            Assert.IsTrue(movies.Count >= 3,
                "Repository should be pre-seeded with at least 3 movies.");
        }

        [TestMethod]
        public void GetAll_ReturnsReadOnlyList()
        {
            var movies = _repo.GetAll();

            Assert.IsInstanceOfType(movies, typeof(IReadOnlyList<Movie>),
                "GetAll should return IReadOnlyList to prevent external mutation.");
        }

        [TestMethod]
        public void GetAll_ReturnsDefensiveCopies()
        {
            var movies = _repo.GetAll();
            var originalName = movies[0].Name;

            // Mutate the returned movie
            movies[0].Name = "MUTATED";

            // Re-fetch and verify the store is unchanged
            var freshMovies = _repo.GetAll();
            Assert.AreEqual(originalName, freshMovies[0].Name,
                "Mutating a returned movie should not affect the internal store.");
        }

        // ---------- GetById ----------

        [TestMethod]
        public void GetById_ValidId_ReturnsMovie()
        {
            var movie = _repo.GetById(1);

            Assert.IsNotNull(movie);
            Assert.AreEqual(1, movie.Id);
        }

        [TestMethod]
        public void GetById_InvalidId_ReturnsNull()
        {
            var movie = _repo.GetById(99999);

            Assert.IsNull(movie,
                "GetById should return null for non-existent IDs.");
        }

        [TestMethod]
        public void GetById_ReturnsDefensiveCopy()
        {
            var movie = _repo.GetById(1);
            var originalName = movie.Name;

            movie.Name = "MUTATED";

            var freshMovie = _repo.GetById(1);
            Assert.AreEqual(originalName, freshMovie.Name,
                "Mutating a returned movie should not affect the internal store.");
        }

        // ---------- Add ----------

        [TestMethod]
        public void Add_AssignsIncrementingId()
        {
            var initialCount = _repo.GetAll().Count;
            var newMovie = new Movie { Name = "Test Movie", ReleaseDate = DateTime.Now };

            _repo.Add(newMovie);

            var allMovies = _repo.GetAll();
            Assert.AreEqual(initialCount + 1, allMovies.Count);

            var added = allMovies.Last();
            Assert.IsTrue(added.Id > 0, "Added movie should have a positive Id.");
            Assert.AreEqual("Test Movie", added.Name);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Add_NullMovie_ThrowsArgumentNullException()
        {
            _repo.Add(null);
        }

        [TestMethod]
        public void Add_MultipleMovies_GetsUniqueIds()
        {
            var m1 = new Movie { Name = "Movie A" };
            var m2 = new Movie { Name = "Movie B" };

            _repo.Add(m1);
            _repo.Add(m2);

            Assert.AreNotEqual(m1.Id, m2.Id,
                "Each added movie should get a unique ID.");
        }

        // ---------- Update ----------

        [TestMethod]
        public void Update_ExistingMovie_ChangesStoredValues()
        {
            var movie = _repo.GetById(1);
            var updatedName = "Updated: " + movie.Name;
            movie.Name = updatedName;

            _repo.Update(movie);

            var fetched = _repo.GetById(1);
            Assert.AreEqual(updatedName, fetched.Name);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Update_NonExistentMovie_ThrowsKeyNotFoundException()
        {
            var ghost = new Movie { Id = 99999, Name = "Ghost" };
            _repo.Update(ghost);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Update_NullMovie_ThrowsArgumentNullException()
        {
            _repo.Update(null);
        }

        // ---------- Remove ----------

        [TestMethod]
        public void Remove_ExistingId_DecreasesCount()
        {
            var initialCount = _repo.GetAll().Count;

            _repo.Remove(1);

            Assert.AreEqual(initialCount - 1, _repo.GetAll().Count);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Remove_NonExistentId_ThrowsKeyNotFoundException()
        {
            _repo.Remove(99999);
        }

        [TestMethod]
        public void Remove_ThenGetById_ReturnsNull()
        {
            // Add a fresh movie to avoid side-effects on pre-seeded data
            var movie = new Movie { Name = "Ephemeral" };
            _repo.Add(movie);
            var id = movie.Id;

            _repo.Remove(id);

            Assert.IsNull(_repo.GetById(id),
                "Removed movie should no longer be retrievable.");
        }

        // ---------- GetByReleaseDate ----------

        [TestMethod]
        public void GetByReleaseDate_MatchingDate_ReturnsMovies()
        {
            // Pre-seeded: Shrek! = 2001-05-18
            var results = _repo.GetByReleaseDate(2001, 5);

            Assert.IsTrue(results.Count >= 1,
                "Should find at least Shrek! for May 2001.");
            Assert.IsTrue(results.All(m =>
                m.ReleaseDate.HasValue &&
                m.ReleaseDate.Value.Year == 2001 &&
                m.ReleaseDate.Value.Month == 5));
        }

        [TestMethod]
        public void GetByReleaseDate_NoMatch_ReturnsEmptyList()
        {
            var results = _repo.GetByReleaseDate(1800, 1);

            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void GetByReleaseDate_ResultsOrderedByDate()
        {
            // Add two movies in the same month with different days
            _repo.Add(new Movie { Name = "Late", ReleaseDate = new DateTime(2020, 6, 25) });
            _repo.Add(new Movie { Name = "Early", ReleaseDate = new DateTime(2020, 6, 5) });

            var results = _repo.GetByReleaseDate(2020, 6);

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results[0].ReleaseDate <= results[1].ReleaseDate,
                "Results should be ordered by release date ascending.");
        }

        [TestMethod]
        public void GetByReleaseDate_IgnoresMoviesWithoutReleaseDate()
        {
            _repo.Add(new Movie { Name = "No Date" });  // ReleaseDate = null

            // Should not crash and should not include the null-dated movie
            var results = _repo.GetByReleaseDate(2001, 5);
            Assert.IsTrue(results.All(m => m.ReleaseDate.HasValue));
        }

        // ---------- GetRandom ----------

        [TestMethod]
        public void GetRandom_ReturnsAMovie()
        {
            var movie = _repo.GetRandom();

            Assert.IsNotNull(movie,
                "GetRandom should return a movie from the pre-seeded store.");
        }

        [TestMethod]
        public void GetRandom_ReturnsDefensiveCopy()
        {
            var movie = _repo.GetRandom();
            var originalId = movie.Id;
            var originalName = movie.Name;

            movie.Name = "MUTATED";

            var stored = _repo.GetById(originalId);
            Assert.AreEqual(originalName, stored.Name,
                "Mutating a random movie should not affect the store.");
        }

        // ---------- Concurrent access ----------

        [TestMethod]
        public void ConcurrentAdds_DoNotLoseMovies()
        {
            const int threadCount = 10;
            const int moviesPerThread = 20;
            var initialCount = _repo.GetAll().Count;
            var tasks = new Task[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < moviesPerThread; i++)
                    {
                        _repo.Add(new Movie
                        {
                            Name = $"Concurrent-{Thread.CurrentThread.ManagedThreadId}-{i}"
                        });
                    }
                });
            }

            Task.WaitAll(tasks);

            var finalCount = _repo.GetAll().Count;
            Assert.AreEqual(initialCount + (threadCount * moviesPerThread), finalCount,
                "All concurrently added movies should be present in the store.");
        }

        // ---------- Search ----------

        [TestMethod]
        public void Search_ByName_ReturnsCaseInsensitiveMatches()
        {
            var results = _repo.Search("shrek", null, null);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Shrek!", results[0].Name);
        }

        [TestMethod]
        public void Search_ByName_SubstringMatch()
        {
            var results = _repo.Search("the", null, null);

            Assert.IsTrue(results.Count >= 1,
                "Should match 'The Godfather' via substring.");
            Assert.IsTrue(results.Any(m => m.Name == "The Godfather"));
        }

        [TestMethod]
        public void Search_ByGenre_ReturnsMatchingMovies()
        {
            var results = _repo.Search(null, Genre.Animation, null);

            Assert.IsTrue(results.Count >= 2,
                "Should find at least Shrek! and Toy Story as Animation.");
            Assert.IsTrue(results.All(m => m.Genre == Genre.Animation));
        }

        [TestMethod]
        public void Search_ByMinRating_ReturnsHighRated()
        {
            var results = _repo.Search(null, null, 5);

            Assert.IsTrue(results.Count >= 2,
                "Should find at least The Godfather and Toy Story with 5 stars.");
            Assert.IsTrue(results.All(m => m.Rating.HasValue && m.Rating.Value >= 5));
        }

        [TestMethod]
        public void Search_CombinedFilters_NarrowsResults()
        {
            // Animation movies with rating >= 5
            var results = _repo.Search(null, Genre.Animation, 5);

            Assert.IsTrue(results.Count >= 1,
                "Should find Toy Story (Animation, 5 stars).");
            Assert.IsTrue(results.All(m =>
                m.Genre == Genre.Animation && m.Rating >= 5));
        }

        [TestMethod]
        public void Search_NoFilters_ReturnsAllSortedByName()
        {
            var results = _repo.Search(null, null, null);

            Assert.IsTrue(results.Count >= 3);
            for (int i = 1; i < results.Count; i++)
            {
                Assert.IsTrue(
                    string.Compare(results[i - 1].Name, results[i].Name, StringComparison.Ordinal) <= 0,
                    "Search with no filters should return all movies sorted by name.");
            }
        }

        [TestMethod]
        public void Search_NoMatches_ReturnsEmptyList()
        {
            var results = _repo.Search("zzz_nonexistent", null, null);

            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public void Search_EmptyQuery_TreatedAsNoFilter()
        {
            var all = _repo.Search(null, null, null);
            var withEmpty = _repo.Search("", null, null);
            var withWhitespace = _repo.Search("   ", null, null);

            Assert.AreEqual(all.Count, withEmpty.Count);
            Assert.AreEqual(all.Count, withWhitespace.Count);
        }

        // ---------- Genre and Rating cloning ----------

        [TestMethod]
        public void GetById_ClonesGenreAndRating()
        {
            var movie = _repo.GetById(1);

            Assert.IsNotNull(movie.Genre, "Pre-seeded Shrek! should have a genre.");
            Assert.IsNotNull(movie.Rating, "Pre-seeded Shrek! should have a rating.");
            Assert.AreEqual(Genre.Animation, movie.Genre);
            Assert.AreEqual(4, movie.Rating);
        }

        [TestMethod]
        public void Update_UpdatesGenreAndRating()
        {
            var movie = _repo.GetById(1);
            movie.Genre = Genre.Comedy;
            movie.Rating = 3;

            _repo.Update(movie);

            var updated = _repo.GetById(1);
            Assert.AreEqual(Genre.Comedy, updated.Genre);
            Assert.AreEqual(3, updated.Rating);

            // Restore original values
            updated.Genre = Genre.Animation;
            updated.Rating = 4;
            _repo.Update(updated);
        }

        // ---------- Concurrent access ----------

        [TestMethod]
        public void ConcurrentAdds_GenerateUniqueIds()
        {
            const int threadCount = 5;
            const int moviesPerThread = 10;
            var tasks = new Task[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < moviesPerThread; i++)
                    {
                        _repo.Add(new Movie { Name = $"Unique-{i}" });
                    }
                });
            }

            Task.WaitAll(tasks);

            var allMovies = _repo.GetAll();
            var ids = allMovies.Select(m => m.Id).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(),
                "All movie IDs should be unique even under concurrent access.");
        }
    }
}
