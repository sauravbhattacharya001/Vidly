using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory collection repository.
    /// Uses Dictionary for O(1) lookups by ID, locking for thread safety,
    /// and counter-based ID generation.
    /// </summary>
    public class InMemoryCollectionRepository : ICollectionRepository
    {
        private static readonly Dictionary<int, MovieCollection> _collections;
        private static readonly object _lock = new object();
        private static int _nextId;

        static InMemoryCollectionRepository()
        {
            _collections = new Dictionary<int, MovieCollection>();

            // Seed some sample collections
            var seedData = new[]
            {
                new MovieCollection
                {
                    Id = 1,
                    Name = "Classic Must-Watch",
                    Description = "Timeless films everyone should see",
                    CreatedAt = DateTime.Today.AddDays(-30),
                    UpdatedAt = DateTime.Today.AddDays(-5),
                    IsPublished = true,
                    Items = new List<CollectionItem>
                    {
                        new CollectionItem { MovieId = 2, SortOrder = 1, Note = "The ultimate classic" }
                    }
                },
                new MovieCollection
                {
                    Id = 2,
                    Name = "Family Favorites",
                    Description = "Great movies for the whole family",
                    CreatedAt = DateTime.Today.AddDays(-20),
                    UpdatedAt = DateTime.Today.AddDays(-3),
                    IsPublished = true,
                    Items = new List<CollectionItem>
                    {
                        new CollectionItem { MovieId = 1, SortOrder = 1, Note = "Kids love it" },
                        new CollectionItem { MovieId = 3, SortOrder = 2, Note = "Animated classic" }
                    }
                },
                new MovieCollection
                {
                    Id = 3,
                    Name = "Staff Picks Draft",
                    Description = "Our staff recommendations — work in progress",
                    CreatedAt = DateTime.Today.AddDays(-2),
                    UpdatedAt = DateTime.Today.AddDays(-1),
                    IsPublished = false,
                    Items = new List<CollectionItem>()
                }
            };

            foreach (var c in seedData)
                _collections[c.Id] = c;

            _nextId = 4;
        }

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
                return _collections.Values
                    .Select(Clone)
                    .OrderBy(c => c.Name)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public void Add(MovieCollection entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrWhiteSpace(entity.Name))
                throw new ArgumentException("Collection name is required.", nameof(entity));

            lock (_lock)
            {
                // Enforce unique name
                if (_collections.Values.Any(c =>
                    c.Name.Equals(entity.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        $"A collection named \"{entity.Name}\" already exists.");

                entity.Id = _nextId++;

                if (entity.CreatedAt == default)
                    entity.CreatedAt = DateTime.Now;
                if (entity.UpdatedAt == default)
                    entity.UpdatedAt = entity.CreatedAt;
                if (entity.Items == null)
                    entity.Items = new List<CollectionItem>();

                _collections[entity.Id] = Clone(entity);
            }
        }

        public void Update(MovieCollection entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            lock (_lock)
            {
                if (!_collections.ContainsKey(entity.Id))
                    throw new KeyNotFoundException(
                        $"Collection with Id {entity.Id} not found.");

                // Check unique name (excluding self)
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
                    throw new KeyNotFoundException(
                        $"Collection with Id {id} not found.");
                _collections.Remove(id);
            }
        }

        public IReadOnlyList<MovieCollection> GetPublished()
        {
            lock (_lock)
            {
                return _collections.Values
                    .Where(c => c.IsPublished)
                    .Select(Clone)
                    .OrderBy(c => c.Name)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<MovieCollection> Search(string query)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(query))
                    return GetAll();

                return _collections.Values
                    .Where(c => c.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (c.Description != null && c.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(Clone)
                    .OrderBy(c => c.Name)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public MovieCollection GetByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            lock (_lock)
            {
                var match = _collections.Values.FirstOrDefault(c =>
                    c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                return match != null ? Clone(match) : null;
            }
        }

        public bool AddMovie(int collectionId, int movieId, string note = null)
        {
            // Enforce note length at the storage boundary as defense-in-depth
            if (note != null && note.Length > CollectionItem.MaxNoteLength)
                throw new ArgumentException(
                    $"Note cannot exceed {CollectionItem.MaxNoteLength} characters.",
                    nameof(note));

            lock (_lock)
            {
                if (!_collections.TryGetValue(collectionId, out var collection))
                    return false;

                // Check for duplicate
                if (collection.Items.Any(i => i.MovieId == movieId))
                    return false;

                var nextOrder = collection.Items.Count > 0
                    ? collection.Items.Max(i => i.SortOrder) + 1
                    : 1;

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
                if (!_collections.TryGetValue(collectionId, out var collection))
                    return false;

                var item = collection.Items.FirstOrDefault(i => i.MovieId == movieId);
                if (item == null)
                    return false;

                collection.Items.Remove(item);

                // Re-number sort orders
                var sorted = collection.Items.OrderBy(i => i.SortOrder).ToList();
                for (int i = 0; i < sorted.Count; i++)
                    sorted[i].SortOrder = i + 1;

                collection.UpdatedAt = DateTime.Now;
                return true;
            }
        }

        public bool ReorderMovie(int collectionId, int movieId, int newPosition)
        {
            lock (_lock)
            {
                if (!_collections.TryGetValue(collectionId, out var collection))
                    return false;

                var item = collection.Items.FirstOrDefault(i => i.MovieId == movieId);
                if (item == null)
                    return false;

                // Clamp position
                if (newPosition < 1) newPosition = 1;
                if (newPosition > collection.Items.Count) newPosition = collection.Items.Count;

                // Remove and re-insert at new position
                collection.Items.Remove(item);
                collection.Items.Insert(newPosition - 1, item);

                // Re-number
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
                    .Select(Clone)
                    .OrderBy(c => c.Name)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Creates a defensive deep copy.
        /// </summary>
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
}
