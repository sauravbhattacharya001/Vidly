using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Controllers;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class CollectionTests
    {
        #region Test Helpers

        /// <summary>
        /// Isolated movie repository for testing (no static state).
        /// </summary>
        private class TestMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();
            private int _nextId = 1;

            public void Add(Movie movie)
            {
                if (movie.Id == 0) movie.Id = _nextId++;
                _movies[movie.Id] = movie;
            }

            public Movie GetById(int id) =>
                _movies.TryGetValue(id, out var m) ? m : null;

            public IReadOnlyList<Movie> GetAll() =>
                _movies.Values.ToList().AsReadOnly();

            public void Update(Movie movie) { _movies[movie.Id] = movie; }
            public void Remove(int id) { _movies.Remove(id); }

            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                _movies.Values
                    .Where(m => m.ReleaseDate?.Year == year && m.ReleaseDate?.Month == month)
                    .ToList().AsReadOnly();

            public Movie GetRandom() => _movies.Values.FirstOrDefault();

            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                _movies.Values.ToList().AsReadOnly();
        }

        /// <summary>
        /// Isolated collection repository for testing (no static state).
        /// </summary>
        private class TestCollectionRepository : ICollectionRepository
        {
            private readonly Dictionary<int, MovieCollection> _collections = new Dictionary<int, MovieCollection>();
            private readonly object _lock = new object();
            private int _nextId = 1;

            public MovieCollection GetById(int id)
            {
                lock (_lock)
                {
                    return _collections.TryGetValue(id, out var c) ? Clone(c) : null;
                }
            }

            public IReadOnlyList<MovieCollection> GetAll()
            {
                lock (_lock)
                {
                    return _collections.Values.Select(Clone).OrderBy(c => c.Name).ToList().AsReadOnly();
                }
            }

            public void Add(MovieCollection entity)
            {
                if (entity == null) throw new ArgumentNullException(nameof(entity));
                if (string.IsNullOrWhiteSpace(entity.Name))
                    throw new ArgumentException("Collection name is required.", nameof(entity));

                lock (_lock)
                {
                    if (_collections.Values.Any(c =>
                        c.Name.Equals(entity.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(
                            $"A collection named \"{entity.Name}\" already exists.");

                    entity.Id = _nextId++;
                    if (entity.CreatedAt == default) entity.CreatedAt = DateTime.Now;
                    if (entity.UpdatedAt == default) entity.UpdatedAt = entity.CreatedAt;
                    if (entity.Items == null) entity.Items = new List<CollectionItem>();
                    _collections[entity.Id] = Clone(entity);
                }
            }

            public void Update(MovieCollection entity)
            {
                if (entity == null) throw new ArgumentNullException(nameof(entity));
                lock (_lock)
                {
                    if (!_collections.ContainsKey(entity.Id))
                        throw new KeyNotFoundException($"Collection with Id {entity.Id} not found.");
                    if (_collections.Values.Any(c =>
                        c.Id != entity.Id &&
                        c.Name.Equals(entity.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(
                            $"A collection named \"{entity.Name}\" already exists.");
                    entity.UpdatedAt = DateTime.Now;
                    _collections[entity.Id] = Clone(entity);
                }
            }

            public void Remove(int id)
            {
                lock (_lock)
                {
                    if (!_collections.ContainsKey(id))
                        throw new KeyNotFoundException($"Collection with Id {id} not found.");
                    _collections.Remove(id);
                }
            }

            public IReadOnlyList<MovieCollection> GetPublished()
            {
                lock (_lock)
                {
                    return _collections.Values.Where(c => c.IsPublished)
                        .Select(Clone).OrderBy(c => c.Name).ToList().AsReadOnly();
                }
            }

            public IReadOnlyList<MovieCollection> Search(string query)
            {
                lock (_lock)
                {
                    if (string.IsNullOrWhiteSpace(query)) return GetAll();
                    return _collections.Values
                        .Where(c => c.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    (c.Description != null && c.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                        .Select(Clone).OrderBy(c => c.Name).ToList().AsReadOnly();
                }
            }

            public MovieCollection GetByName(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;
                lock (_lock)
                {
                    var match = _collections.Values.FirstOrDefault(c =>
                        c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    return match != null ? Clone(match) : null;
                }
            }

            public bool AddMovie(int collectionId, int movieId, string note = null)
            {
                lock (_lock)
                {
                    if (!_collections.TryGetValue(collectionId, out var collection)) return false;
                    if (collection.Items.Any(i => i.MovieId == movieId)) return false;
                    var nextOrder = collection.Items.Count > 0
                        ? collection.Items.Max(i => i.SortOrder) + 1 : 1;
                    collection.Items.Add(new CollectionItem
                    {
                        MovieId = movieId,
                        SortOrder = nextOrder,
                        Note = note
                    });
                    collection.UpdatedAt = DateTime.Now;
                    return true;
                }
            }

            public bool RemoveMovie(int collectionId, int movieId)
            {
                lock (_lock)
                {
                    if (!_collections.TryGetValue(collectionId, out var collection)) return false;
                    var item = collection.Items.FirstOrDefault(i => i.MovieId == movieId);
                    if (item == null) return false;
                    collection.Items.Remove(item);
                    var sorted = collection.Items.OrderBy(i => i.SortOrder).ToList();
                    for (int i = 0; i < sorted.Count; i++) sorted[i].SortOrder = i + 1;
                    collection.UpdatedAt = DateTime.Now;
                    return true;
                }
            }

            public bool ReorderMovie(int collectionId, int movieId, int newPosition)
            {
                lock (_lock)
                {
                    if (!_collections.TryGetValue(collectionId, out var collection)) return false;
                    var item = collection.Items.FirstOrDefault(i => i.MovieId == movieId);
                    if (item == null) return false;
                    if (newPosition < 1) newPosition = 1;
                    if (newPosition > collection.Items.Count) newPosition = collection.Items.Count;
                    collection.Items.Remove(item);
                    collection.Items.Insert(newPosition - 1, item);
                    for (int i = 0; i < collection.Items.Count; i++)
                        collection.Items[i].SortOrder = i + 1;
                    collection.UpdatedAt = DateTime.Now;
                    return true;
                }
            }

            public IReadOnlyList<MovieCollection> GetCollectionsContaining(int movieId)
            {
                lock (_lock)
                {
                    return _collections.Values
                        .Where(c => c.Items.Any(i => i.MovieId == movieId))
                        .Select(Clone).OrderBy(c => c.Name).ToList().AsReadOnly();
                }
            }

            private static MovieCollection Clone(MovieCollection source)
            {
                return new MovieCollection
                {
                    Id = source.Id,
                    Name = source.Name,
                    Description = source.Description,
                    CreatedAt = source.CreatedAt,
                    UpdatedAt = source.UpdatedAt,
                    IsPublished = source.IsPublished,
                    Items = source.Items?.Select(i => new CollectionItem
                    {
                        MovieId = i.MovieId,
                        SortOrder = i.SortOrder,
                        Note = i.Note
                    }).ToList() ?? new List<CollectionItem>()
                };
            }
        }

        private TestCollectionRepository _collectionRepo;
        private TestMovieRepository _movieRepo;

        private void SeedMovies()
        {
            _movieRepo.Add(new Movie { Id = 1, Name = "Shrek!", Genre = Genre.Animation, Rating = 4 });
            _movieRepo.Add(new Movie { Id = 2, Name = "The Godfather", Genre = Genre.Drama, Rating = 5 });
            _movieRepo.Add(new Movie { Id = 3, Name = "Toy Story", Genre = Genre.Animation, Rating = 5 });
            _movieRepo.Add(new Movie { Id = 4, Name = "Die Hard", Genre = Genre.Action, Rating = 4 });
            _movieRepo.Add(new Movie { Id = 5, Name = "The Dark Knight", Genre = Genre.Action, Rating = 5 });
            _movieRepo.Add(new Movie { Id = 6, Name = "Forrest Gump", Genre = Genre.Drama, Rating = 5 });
        }

        private MovieCollection CreateCollection(string name, bool published = true, string description = null)
        {
            var c = new MovieCollection
            {
                Name = name,
                Description = description ?? $"Description for {name}",
                IsPublished = published
            };
            _collectionRepo.Add(c);
            return _collectionRepo.GetByName(name);
        }

        [TestInitialize]
        public void SetUp()
        {
            _collectionRepo = new TestCollectionRepository();
            _movieRepo = new TestMovieRepository();
            SeedMovies();
        }

        #endregion

        #region Model Tests

        [TestMethod]
        public void MovieCollection_DefaultItems_IsEmptyList()
        {
            var collection = new MovieCollection();
            Assert.IsNotNull(collection.Items);
            Assert.AreEqual(0, collection.Items.Count);
        }

        [TestMethod]
        public void MovieCollection_MovieCount_ReturnsItemsCount()
        {
            var collection = new MovieCollection();
            collection.Items.Add(new CollectionItem { MovieId = 1, SortOrder = 1 });
            collection.Items.Add(new CollectionItem { MovieId = 2, SortOrder = 2 });
            Assert.AreEqual(2, collection.MovieCount);
        }

        [TestMethod]
        public void MovieCollection_MovieCount_NullItems_ReturnsZero()
        {
            var collection = new MovieCollection { Items = null };
            Assert.AreEqual(0, collection.MovieCount);
        }

        [TestMethod]
        public void MovieCollection_NameRequired_Validation()
        {
            var collection = new MovieCollection { Name = null };
            var results = new List<ValidationResult>();
            var context = new ValidationContext(collection);
            var isValid = Validator.TryValidateObject(collection, context, results, true);
            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains("Name")));
        }

        [TestMethod]
        public void MovieCollection_NameTooLong_Validation()
        {
            var collection = new MovieCollection { Name = new string('x', 101) };
            var results = new List<ValidationResult>();
            var context = new ValidationContext(collection);
            var isValid = Validator.TryValidateObject(collection, context, results, true);
            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains("Name")));
        }

        [TestMethod]
        public void MovieCollection_NameMaxLength_Valid()
        {
            var collection = new MovieCollection { Name = new string('x', 100) };
            var results = new List<ValidationResult>();
            var context = new ValidationContext(collection);
            var isValid = Validator.TryValidateObject(collection, context, results, true);
            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public void MovieCollection_DescriptionTooLong_Validation()
        {
            var collection = new MovieCollection { Name = "Test", Description = new string('y', 501) };
            var results = new List<ValidationResult>();
            var context = new ValidationContext(collection);
            var isValid = Validator.TryValidateObject(collection, context, results, true);
            Assert.IsFalse(isValid);
            Assert.IsTrue(results.Any(r => r.MemberNames.Contains("Description")));
        }

        [TestMethod]
        public void MovieCollection_DescriptionMaxLength_Valid()
        {
            var collection = new MovieCollection { Name = "Test", Description = new string('y', 500) };
            var results = new List<ValidationResult>();
            var context = new ValidationContext(collection);
            var isValid = Validator.TryValidateObject(collection, context, results, true);
            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public void CollectionItem_Creation_DefaultValues()
        {
            var item = new CollectionItem();
            Assert.AreEqual(0, item.MovieId);
            Assert.AreEqual(0, item.SortOrder);
            Assert.IsNull(item.Note);
        }

        [TestMethod]
        public void CollectionItem_SetProperties()
        {
            var item = new CollectionItem { MovieId = 5, SortOrder = 3, Note = "Great pick" };
            Assert.AreEqual(5, item.MovieId);
            Assert.AreEqual(3, item.SortOrder);
            Assert.AreEqual("Great pick", item.Note);
        }

        #endregion

        #region Repository CRUD Tests

        [TestMethod]
        public void Add_ValidCollection_AssignsId()
        {
            var c = CreateCollection("Action Hits");
            Assert.IsTrue(c.Id > 0);
        }

        [TestMethod]
        public void Add_ValidCollection_CanBeRetrieved()
        {
            var c = CreateCollection("Action Hits");
            var retrieved = _collectionRepo.GetById(c.Id);
            Assert.IsNotNull(retrieved);
            Assert.AreEqual("Action Hits", retrieved.Name);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Add_NullName_Throws()
        {
            _collectionRepo.Add(new MovieCollection { Name = null });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Add_EmptyName_Throws()
        {
            _collectionRepo.Add(new MovieCollection { Name = "   " });
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Add_DuplicateName_Throws()
        {
            CreateCollection("My List");
            _collectionRepo.Add(new MovieCollection { Name = "My List" });
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Add_DuplicateNameCaseInsensitive_Throws()
        {
            CreateCollection("My List");
            _collectionRepo.Add(new MovieCollection { Name = "MY LIST" });
        }

        [TestMethod]
        public void GetById_NonExistent_ReturnsNull()
        {
            Assert.IsNull(_collectionRepo.GetById(999));
        }

        [TestMethod]
        public void GetAll_ReturnsAllCollections()
        {
            CreateCollection("A List");
            CreateCollection("B List");
            CreateCollection("C List");
            var all = _collectionRepo.GetAll();
            Assert.AreEqual(3, all.Count);
        }

        [TestMethod]
        public void GetAll_OrderedByName()
        {
            CreateCollection("Zebra");
            CreateCollection("Alpha");
            CreateCollection("Middle");
            var all = _collectionRepo.GetAll();
            Assert.AreEqual("Alpha", all[0].Name);
            Assert.AreEqual("Middle", all[1].Name);
            Assert.AreEqual("Zebra", all[2].Name);
        }

        [TestMethod]
        public void Update_ChangesName()
        {
            var c = CreateCollection("Old Name");
            c.Name = "New Name";
            _collectionRepo.Update(c);
            var updated = _collectionRepo.GetById(c.Id);
            Assert.AreEqual("New Name", updated.Name);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Update_NonExistent_Throws()
        {
            _collectionRepo.Update(new MovieCollection { Id = 999, Name = "Test" });
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Update_DuplicateName_Throws()
        {
            CreateCollection("First");
            var second = CreateCollection("Second");
            second.Name = "First";
            _collectionRepo.Update(second);
        }

        [TestMethod]
        public void Remove_ExistingCollection_Removes()
        {
            var c = CreateCollection("To Delete");
            _collectionRepo.Remove(c.Id);
            Assert.IsNull(_collectionRepo.GetById(c.Id));
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void Remove_NonExistent_Throws()
        {
            _collectionRepo.Remove(999);
        }

        #endregion

        #region GetPublished Tests

        [TestMethod]
        public void GetPublished_OnlyReturnsPublished()
        {
            CreateCollection("Published One", published: true);
            CreateCollection("Draft One", published: false);
            CreateCollection("Published Two", published: true);

            var published = _collectionRepo.GetPublished();
            Assert.AreEqual(2, published.Count);
            Assert.IsTrue(published.All(c => c.IsPublished));
        }

        [TestMethod]
        public void GetPublished_NonePublished_ReturnsEmpty()
        {
            CreateCollection("Draft Only", published: false);
            var published = _collectionRepo.GetPublished();
            Assert.AreEqual(0, published.Count);
        }

        #endregion

        #region Search Tests

        [TestMethod]
        public void Search_ByName_CaseInsensitive()
        {
            CreateCollection("Action Movies");
            CreateCollection("Comedy Gold");

            var results = _collectionRepo.Search("action");
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Action Movies", results[0].Name);
        }

        [TestMethod]
        public void Search_ByDescription()
        {
            CreateCollection("My List", description: "Best action films ever");
            CreateCollection("Other List", description: "Romantic comedies");

            var results = _collectionRepo.Search("action");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Search_EmptyQuery_ReturnsAll()
        {
            CreateCollection("A");
            CreateCollection("B");
            var results = _collectionRepo.Search("");
            Assert.AreEqual(2, results.Count);
        }

        [TestMethod]
        public void Search_NoMatch_ReturnsEmpty()
        {
            CreateCollection("Action Movies");
            var results = _collectionRepo.Search("zzzzz");
            Assert.AreEqual(0, results.Count);
        }

        #endregion

        #region GetByName Tests

        [TestMethod]
        public void GetByName_ExactMatch_CaseInsensitive()
        {
            CreateCollection("Staff Picks");
            var result = _collectionRepo.GetByName("staff picks");
            Assert.IsNotNull(result);
            Assert.AreEqual("Staff Picks", result.Name);
        }

        [TestMethod]
        public void GetByName_NotFound_ReturnsNull()
        {
            Assert.IsNull(_collectionRepo.GetByName("Nonexistent"));
        }

        [TestMethod]
        public void GetByName_NullInput_ReturnsNull()
        {
            Assert.IsNull(_collectionRepo.GetByName(null));
        }

        #endregion

        #region AddMovie Tests

        [TestMethod]
        public void AddMovie_Success_ReturnsTrue()
        {
            var c = CreateCollection("My List");
            var result = _collectionRepo.AddMovie(c.Id, 1, "Great movie");
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void AddMovie_Success_MovieAppearsInCollection()
        {
            var c = CreateCollection("My List");
            _collectionRepo.AddMovie(c.Id, 1, "Note A");
            var updated = _collectionRepo.GetById(c.Id);
            Assert.AreEqual(1, updated.Items.Count);
            Assert.AreEqual(1, updated.Items[0].MovieId);
            Assert.AreEqual("Note A", updated.Items[0].Note);
        }

        [TestMethod]
        public void AddMovie_DuplicateMovie_ReturnsFalse()
        {
            var c = CreateCollection("My List");
            _collectionRepo.AddMovie(c.Id, 1);
            var result = _collectionRepo.AddMovie(c.Id, 1);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void AddMovie_InvalidCollection_ReturnsFalse()
        {
            var result = _collectionRepo.AddMovie(999, 1);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void AddMovie_AutoIncrementsSortOrder()
        {
            var c = CreateCollection("My List");
            _collectionRepo.AddMovie(c.Id, 1);
            _collectionRepo.AddMovie(c.Id, 2);
            _collectionRepo.AddMovie(c.Id, 3);
            var updated = _collectionRepo.GetById(c.Id);
            Assert.AreEqual(1, updated.Items.First(i => i.MovieId == 1).SortOrder);
            Assert.AreEqual(2, updated.Items.First(i => i.MovieId == 2).SortOrder);
            Assert.AreEqual(3, updated.Items.First(i => i.MovieId == 3).SortOrder);
        }

        #endregion

        #region RemoveMovie Tests

        [TestMethod]
        public void RemoveMovie_Success_ReturnsTrue()
        {
            var c = CreateCollection("My List");
            _collectionRepo.AddMovie(c.Id, 1);
            var result = _collectionRepo.RemoveMovie(c.Id, 1);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void RemoveMovie_NotInCollection_ReturnsFalse()
        {
            var c = CreateCollection("My List");
            var result = _collectionRepo.RemoveMovie(c.Id, 999);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void RemoveMovie_InvalidCollection_ReturnsFalse()
        {
            var result = _collectionRepo.RemoveMovie(999, 1);
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void RemoveMovie_RenumbersSortOrders()
        {
            var c = CreateCollection("My List");
            _collectionRepo.AddMovie(c.Id, 1);
            _collectionRepo.AddMovie(c.Id, 2);
            _collectionRepo.AddMovie(c.Id, 3);
            _collectionRepo.RemoveMovie(c.Id, 2);
            var updated = _collectionRepo.GetById(c.Id);
            Assert.AreEqual(2, updated.Items.Count);
            // Sort orders should be 1 and 2 after re-numbering
            var orders = updated.Items.Select(i => i.SortOrder).OrderBy(o => o).ToList();
            Assert.AreEqual(1, orders[0]);
            Assert.AreEqual(2, orders[1]);
        }

        #endregion

        #region ReorderMovie Tests

        [TestMethod]
        public void ReorderMovie_Success_ReturnsTrue()
        {
            var c = CreateCollection("My List");
            _collectionRepo.AddMovie(c.Id, 1);
            _collectionRepo.AddMovie(c.Id, 2);
            _collectionRepo.AddMovie(c.Id, 3);
            var result = _collectionRepo.ReorderMovie(c.Id, 3, 1);
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ReorderMovie_MovesToCorrectPosition()
        {
            var c = CreateCollection("My List");
            _collectionRepo.AddMovie(c.Id, 1);
            _collectionRepo.AddMovie(c.Id, 2);
            _collectionRepo.AddMovie(c.Id, 3);
            _collectionRepo.ReorderMovie(c.Id, 3, 1); // Move movie 3 to position 1
            var updated = _collectionRepo.GetById(c.Id);
            var ordered = updated.Items.OrderBy(i => i.SortOrder).ToList();
            Assert.AreEqual(3, ordered[0].MovieId); // Movie 3 is now first
        }

        [TestMethod]
        public void ReorderMovie_InvalidCollection_ReturnsFalse()
        {
            Assert.IsFalse(_collectionRepo.ReorderMovie(999, 1, 1));
        }

        [TestMethod]
        public void ReorderMovie_MovieNotInCollection_ReturnsFalse()
        {
            var c = CreateCollection("My List");
            _collectionRepo.AddMovie(c.Id, 1);
            Assert.IsFalse(_collectionRepo.ReorderMovie(c.Id, 999, 1));
        }

        #endregion

        #region GetCollectionsContaining Tests

        [TestMethod]
        public void GetCollectionsContaining_ReturnsMatchingCollections()
        {
            var c1 = CreateCollection("List A");
            var c2 = CreateCollection("List B");
            CreateCollection("List C");
            _collectionRepo.AddMovie(c1.Id, 1);
            _collectionRepo.AddMovie(c2.Id, 1);

            var result = _collectionRepo.GetCollectionsContaining(1);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void GetCollectionsContaining_NoMatch_ReturnsEmpty()
        {
            CreateCollection("Empty List");
            var result = _collectionRepo.GetCollectionsContaining(999);
            Assert.AreEqual(0, result.Count);
        }

        #endregion

        #region CollectionService.GetCollectionSummary Tests

        [TestMethod]
        public void GetCollectionSummary_ReturnsEnrichedData()
        {
            var c = CreateCollection("Action Mix");
            _collectionRepo.AddMovie(c.Id, 1, "Hilarious");
            _collectionRepo.AddMovie(c.Id, 4, "Die Hard rocks");

            var service = new CollectionService(_collectionRepo, _movieRepo);
            var summary = service.GetCollectionSummary(c.Id);

            Assert.IsNotNull(summary);
            Assert.AreEqual("Action Mix", summary.Name);
            Assert.AreEqual(2, summary.MovieCount);
            Assert.AreEqual(2, summary.Movies.Count);
            Assert.AreEqual("Shrek!", summary.Movies[0].MovieName);
            Assert.AreEqual("Die Hard", summary.Movies[1].MovieName);
        }

        [TestMethod]
        public void GetCollectionSummary_NonExistent_ReturnsNull()
        {
            var service = new CollectionService(_collectionRepo, _movieRepo);
            Assert.IsNull(service.GetCollectionSummary(999));
        }

        [TestMethod]
        public void GetCollectionSummary_EmptyCollection_ReturnsEmptyMovies()
        {
            var c = CreateCollection("Empty");
            var service = new CollectionService(_collectionRepo, _movieRepo);
            var summary = service.GetCollectionSummary(c.Id);
            Assert.AreEqual(0, summary.Movies.Count);
        }

        [TestMethod]
        public void GetCollectionSummary_IncludesGenreAndRating()
        {
            var c = CreateCollection("Drama");
            _collectionRepo.AddMovie(c.Id, 2);

            var service = new CollectionService(_collectionRepo, _movieRepo);
            var summary = service.GetCollectionSummary(c.Id);

            Assert.AreEqual(Genre.Drama, summary.Movies[0].Genre);
            Assert.AreEqual(5, summary.Movies[0].Rating);
        }

        #endregion

        #region CollectionService.GetPopularCollections Tests

        [TestMethod]
        public void GetPopularCollections_SortedByMovieCount()
        {
            var c1 = CreateCollection("Small");
            var c2 = CreateCollection("Big");
            _collectionRepo.AddMovie(c1.Id, 1);
            _collectionRepo.AddMovie(c2.Id, 1);
            _collectionRepo.AddMovie(c2.Id, 2);
            _collectionRepo.AddMovie(c2.Id, 3);

            var service = new CollectionService(_collectionRepo, _movieRepo);
            var popular = service.GetPopularCollections(10);

            Assert.AreEqual("Big", popular[0].Name);
            Assert.AreEqual("Small", popular[1].Name);
        }

        [TestMethod]
        public void GetPopularCollections_RespectsCount()
        {
            CreateCollection("A");
            CreateCollection("B");
            CreateCollection("C");

            var service = new CollectionService(_collectionRepo, _movieRepo);
            var popular = service.GetPopularCollections(2);
            Assert.AreEqual(2, popular.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void GetPopularCollections_ZeroCount_Throws()
        {
            var service = new CollectionService(_collectionRepo, _movieRepo);
            service.GetPopularCollections(0);
        }

        #endregion

        #region CollectionService.GetCollectionStats Tests

        [TestMethod]
        public void GetCollectionStats_ReturnsCorrectTotals()
        {
            CreateCollection("Pub1", published: true);
            CreateCollection("Pub2", published: true);
            CreateCollection("Draft", published: false);

            var service = new CollectionService(_collectionRepo, _movieRepo);
            var stats = service.GetCollectionStats();

            Assert.AreEqual(3, stats.TotalCollections);
            Assert.AreEqual(2, stats.PublishedCount);
        }

        [TestMethod]
        public void GetCollectionStats_AverageMovies()
        {
            var c1 = CreateCollection("A");
            var c2 = CreateCollection("B");
            _collectionRepo.AddMovie(c1.Id, 1);
            _collectionRepo.AddMovie(c1.Id, 2);
            // c2 has 0 movies, c1 has 2 → average = 1.0

            var service = new CollectionService(_collectionRepo, _movieRepo);
            var stats = service.GetCollectionStats();
            Assert.AreEqual(1.0, stats.AverageMoviesPerCollection);
        }

        [TestMethod]
        public void GetCollectionStats_NoCollections_ZeroAverage()
        {
            var service = new CollectionService(_collectionRepo, _movieRepo);
            var stats = service.GetCollectionStats();
            Assert.AreEqual(0, stats.TotalCollections);
            Assert.AreEqual(0, stats.AverageMoviesPerCollection);
        }

        [TestMethod]
        public void GetCollectionStats_MostCommonGenre()
        {
            var c1 = CreateCollection("Animations");
            _collectionRepo.AddMovie(c1.Id, 1); // Animation
            _collectionRepo.AddMovie(c1.Id, 3); // Animation
            _collectionRepo.AddMovie(c1.Id, 2); // Drama

            var service = new CollectionService(_collectionRepo, _movieRepo);
            var stats = service.GetCollectionStats();
            Assert.AreEqual(Genre.Animation, stats.MostCommonGenre);
        }

        #endregion

        #region CollectionService.SuggestMovies Tests

        [TestMethod]
        public void SuggestMovies_ReturnsSameGenreNotInCollection()
        {
            var c = CreateCollection("Drama Collection");
            _collectionRepo.AddMovie(c.Id, 2); // The Godfather, Drama

            var service = new CollectionService(_collectionRepo, _movieRepo);
            var suggestions = service.SuggestMovies(c.Id);

            // Forrest Gump is also Drama and not in collection
            Assert.IsTrue(suggestions.Any(m => m.Id == 6));
            // The Godfather should NOT be suggested (already in collection)
            Assert.IsFalse(suggestions.Any(m => m.Id == 2));
        }

        [TestMethod]
        public void SuggestMovies_EmptyCollection_ReturnsEmpty()
        {
            var c = CreateCollection("Empty");
            var service = new CollectionService(_collectionRepo, _movieRepo);
            var suggestions = service.SuggestMovies(c.Id);
            Assert.AreEqual(0, suggestions.Count);
        }

        [TestMethod]
        public void SuggestMovies_NonExistentCollection_ReturnsEmpty()
        {
            var service = new CollectionService(_collectionRepo, _movieRepo);
            var suggestions = service.SuggestMovies(999);
            Assert.AreEqual(0, suggestions.Count);
        }

        [TestMethod]
        public void SuggestMovies_MultipleGenres_SuggestsBoth()
        {
            var c = CreateCollection("Mixed");
            _collectionRepo.AddMovie(c.Id, 1); // Animation
            _collectionRepo.AddMovie(c.Id, 4); // Action

            var service = new CollectionService(_collectionRepo, _movieRepo);
            var suggestions = service.SuggestMovies(c.Id);

            // Should suggest: Toy Story (Animation), The Dark Knight (Action)
            Assert.IsTrue(suggestions.Any(m => m.Id == 3)); // Toy Story
            Assert.IsTrue(suggestions.Any(m => m.Id == 5)); // The Dark Knight
        }

        #endregion

        #region Thread Safety Tests

        [TestMethod]
        public void ConcurrentAdds_AllSucceed()
        {
            var errors = new List<Exception>();
            var threads = new Thread[10];

            for (int i = 0; i < 10; i++)
            {
                int idx = i;
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        _collectionRepo.Add(new MovieCollection
                        {
                            Name = $"Concurrent Collection {idx}",
                            Description = $"Thread {idx}"
                        });
                    }
                    catch (Exception ex)
                    {
                        lock (errors) { errors.Add(ex); }
                    }
                });
            }

            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();

            Assert.AreEqual(0, errors.Count, $"Errors: {string.Join("; ", errors.Select(e => e.Message))}");
            Assert.AreEqual(10, _collectionRepo.GetAll().Count);
        }

        [TestMethod]
        public void ConcurrentAddMovies_NoDuplicates()
        {
            var c = CreateCollection("Concurrent Test");
            var successes = 0;

            var threads = new Thread[5];
            for (int i = 0; i < 5; i++)
            {
                threads[i] = new Thread(() =>
                {
                    // All try to add movie 1
                    if (_collectionRepo.AddMovie(c.Id, 1))
                        Interlocked.Increment(ref successes);
                });
            }

            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();

            Assert.AreEqual(1, successes, "Only one thread should succeed adding the same movie");
            var updated = _collectionRepo.GetById(c.Id);
            Assert.AreEqual(1, updated.Items.Count);
        }

        #endregion

        #region Controller Tests

        [TestMethod]
        public void Controller_Index_ReturnsView()
        {
            CreateCollection("Published", published: true);
            var controller = new CollectionsController(_collectionRepo, _movieRepo);
            var result = controller.Index(null) as ViewResult;
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void Controller_Index_WithSearch_FiltersResults()
        {
            CreateCollection("Action Movies", published: true);
            CreateCollection("Comedy Gold", published: true);

            var controller = new CollectionsController(_collectionRepo, _movieRepo);
            var result = controller.Index("action") as ViewResult;
            Assert.IsNotNull(result);
            var model = result.Model as IReadOnlyList<MovieCollection>;
            Assert.IsNotNull(model);
            Assert.AreEqual(1, model.Count);
        }

        [TestMethod]
        public void Controller_Details_ExistingCollection_ReturnsView()
        {
            var c = CreateCollection("Test Details");
            _collectionRepo.AddMovie(c.Id, 1);

            var controller = new CollectionsController(_collectionRepo, _movieRepo);
            var result = controller.Details(c.Id) as ViewResult;
            Assert.IsNotNull(result);
            var summary = result.Model as CollectionSummary;
            Assert.IsNotNull(summary);
            Assert.AreEqual("Test Details", summary.Name);
        }

        [TestMethod]
        public void Controller_Details_NonExistent_ReturnsHttpNotFound()
        {
            var controller = new CollectionsController(_collectionRepo, _movieRepo);
            var result = controller.Details(999);
            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        [TestMethod]
        public void Controller_CreateGet_ReturnsView()
        {
            var controller = new CollectionsController(_collectionRepo, _movieRepo);
            var result = controller.Create() as ViewResult;
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public void Controller_Delete_ExistingCollection_Redirects()
        {
            var c = CreateCollection("To Delete");
            var controller = new CollectionsController(_collectionRepo, _movieRepo);
            var result = controller.Delete(c.Id) as RedirectToRouteResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("Index", result.RouteValues["action"]);
        }

        [TestMethod]
        public void Controller_Delete_NonExistent_ReturnsNotFound()
        {
            var controller = new CollectionsController(_collectionRepo, _movieRepo);
            var result = controller.Delete(999);
            Assert.IsInstanceOfType(result, typeof(HttpNotFoundResult));
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public void EmptyCollection_MovieCount_IsZero()
        {
            var c = CreateCollection("Empty");
            Assert.AreEqual(0, _collectionRepo.GetById(c.Id).MovieCount);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Add_NullCollection_Throws()
        {
            _collectionRepo.Add(null);
        }

        [TestMethod]
        public void Update_SameNameSameCollection_Succeeds()
        {
            var c = CreateCollection("My Name");
            c.Description = "Updated description";
            _collectionRepo.Update(c); // Same name, same ID — should work
            var updated = _collectionRepo.GetById(c.Id);
            Assert.AreEqual("Updated description", updated.Description);
        }

        [TestMethod]
        public void AddManyMovies_AllPersist()
        {
            var c = CreateCollection("Big List");
            for (int i = 1; i <= 6; i++)
                _collectionRepo.AddMovie(c.Id, i);

            var updated = _collectionRepo.GetById(c.Id);
            Assert.AreEqual(6, updated.Items.Count);
        }

        [TestMethod]
        public void ReorderMovie_ClampsPosition()
        {
            var c = CreateCollection("Test");
            _collectionRepo.AddMovie(c.Id, 1);
            _collectionRepo.AddMovie(c.Id, 2);

            // Position beyond count should clamp
            _collectionRepo.ReorderMovie(c.Id, 1, 100);
            var updated = _collectionRepo.GetById(c.Id);
            var ordered = updated.Items.OrderBy(i => i.SortOrder).ToList();
            Assert.AreEqual(2, ordered[1].MovieId == 1 ? ordered[1].SortOrder : -1);
        }

        [TestMethod]
        public void GetById_ReturnsDefensiveCopy()
        {
            var c = CreateCollection("Original");
            var copy1 = _collectionRepo.GetById(c.Id);
            copy1.Name = "Modified";
            var copy2 = _collectionRepo.GetById(c.Id);
            Assert.AreEqual("Original", copy2.Name);
        }

        #endregion
    }
}
