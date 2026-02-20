using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    /// <summary>
    /// Thread-safe in-memory watchlist repository.
    /// Uses Dictionary for O(1) lookups by ID, a composite key HashSet
    /// for O(1) duplicate checking, and counter-based ID generation.
    /// </summary>
    public class InMemoryWatchlistRepository : IWatchlistRepository
    {
        private static readonly Dictionary<int, WatchlistItem> _items;

        /// <summary>
        /// Tracks (customerId, movieId) pairs for O(1) duplicate checking.
        /// </summary>
        private static readonly HashSet<string> _customerMoviePairs;

        private static readonly object _lock = new object();
        private static int _nextId;

        static InMemoryWatchlistRepository()
        {
            _items = new Dictionary<int, WatchlistItem>();
            _customerMoviePairs = new HashSet<string>();

            // Seed some sample watchlist data
            var seedData = new[]
            {
                new WatchlistItem
                {
                    Id = 1,
                    CustomerId = 1,
                    CustomerName = "John Smith",
                    MovieId = 3,
                    MovieName = "Toy Story",
                    MovieGenre = Genre.Animation,
                    MovieRating = 5,
                    AddedDate = DateTime.Today.AddDays(-5),
                    Note = "Kids want to see this",
                    Priority = WatchlistPriority.High
                },
                new WatchlistItem
                {
                    Id = 2,
                    CustomerId = 2,
                    CustomerName = "Jane Doe",
                    MovieId = 1,
                    MovieName = "Shrek!",
                    MovieGenre = Genre.Animation,
                    MovieRating = 4,
                    AddedDate = DateTime.Today.AddDays(-2),
                    Priority = WatchlistPriority.Normal
                },
                new WatchlistItem
                {
                    Id = 3,
                    CustomerId = 1,
                    CustomerName = "John Smith",
                    MovieId = 2,
                    MovieName = "The Godfather",
                    MovieGenre = Genre.Drama,
                    MovieRating = 5,
                    AddedDate = DateTime.Today.AddDays(-1),
                    Note = "Classic must-see",
                    Priority = WatchlistPriority.MustWatch
                }
            };

            foreach (var item in seedData)
            {
                _items[item.Id] = item;
                _customerMoviePairs.Add(MakeKey(item.CustomerId, item.MovieId));
            }

            _nextId = 4;
        }

        public WatchlistItem GetById(int id)
        {
            lock (_lock)
            {
                return _items.TryGetValue(id, out var item) ? Clone(item) : null;
            }
        }

        public IReadOnlyList<WatchlistItem> GetAll()
        {
            lock (_lock)
            {
                return _items.Values
                    .Select(Clone)
                    .OrderByDescending(i => (int)i.Priority)
                    .ThenByDescending(i => i.AddedDate)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<WatchlistItem> GetByCustomer(int customerId)
        {
            lock (_lock)
            {
                return _items.Values
                    .Where(i => i.CustomerId == customerId)
                    .Select(Clone)
                    .OrderByDescending(i => (int)i.Priority)
                    .ThenByDescending(i => i.AddedDate)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public bool IsOnWatchlist(int customerId, int movieId)
        {
            lock (_lock)
            {
                return _customerMoviePairs.Contains(MakeKey(customerId, movieId));
            }
        }

        public WatchlistItem Add(WatchlistItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            lock (_lock)
            {
                var key = MakeKey(item.CustomerId, item.MovieId);
                if (_customerMoviePairs.Contains(key))
                    throw new InvalidOperationException(
                        "This movie is already on the customer's watchlist.");

                item.Id = _nextId++;

                if (item.AddedDate == default)
                    item.AddedDate = DateTime.Today;

                if (item.Priority == 0)
                    item.Priority = WatchlistPriority.Normal;

                _items[item.Id] = item;
                _customerMoviePairs.Add(key);

                return Clone(item);
            }
        }

        public void Remove(int id)
        {
            lock (_lock)
            {
                if (!_items.TryGetValue(id, out var item))
                    throw new KeyNotFoundException(
                        $"Watchlist item with Id {id} not found.");

                _customerMoviePairs.Remove(MakeKey(item.CustomerId, item.MovieId));
                _items.Remove(id);
            }
        }

        public bool RemoveByCustomerAndMovie(int customerId, int movieId)
        {
            lock (_lock)
            {
                var key = MakeKey(customerId, movieId);
                if (!_customerMoviePairs.Contains(key))
                    return false;

                var itemToRemove = _items.Values
                    .FirstOrDefault(i => i.CustomerId == customerId && i.MovieId == movieId);

                if (itemToRemove == null)
                    return false;

                _items.Remove(itemToRemove.Id);
                _customerMoviePairs.Remove(key);
                return true;
            }
        }

        public void Update(WatchlistItem item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            lock (_lock)
            {
                if (!_items.TryGetValue(item.Id, out var existing))
                    throw new KeyNotFoundException(
                        $"Watchlist item with Id {item.Id} not found.");

                existing.Note = item.Note;
                existing.Priority = item.Priority;
            }
        }

        public int ClearCustomerWatchlist(int customerId)
        {
            lock (_lock)
            {
                var toRemove = _items.Values
                    .Where(i => i.CustomerId == customerId)
                    .ToList();

                foreach (var item in toRemove)
                {
                    _customerMoviePairs.Remove(MakeKey(item.CustomerId, item.MovieId));
                    _items.Remove(item.Id);
                }

                return toRemove.Count;
            }
        }

        public WatchlistStats GetStats(int customerId)
        {
            lock (_lock)
            {
                int normal = 0, high = 0, mustWatch = 0;

                foreach (var item in _items.Values)
                {
                    if (item.CustomerId != customerId)
                        continue;

                    switch (item.Priority)
                    {
                        case WatchlistPriority.Normal:    normal++;    break;
                        case WatchlistPriority.High:      high++;      break;
                        case WatchlistPriority.MustWatch: mustWatch++; break;
                    }
                }

                return new WatchlistStats
                {
                    TotalItems = normal + high + mustWatch,
                    NormalCount = normal,
                    HighCount = high,
                    MustWatchCount = mustWatch
                };
            }
        }

        public IReadOnlyList<PopularWatchlistMovie> GetMostWatchlisted(int limit = 10)
        {
            if (limit < 1)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be at least 1.");

            lock (_lock)
            {
                return _items.Values
                    .GroupBy(i => new { i.MovieId, i.MovieName })
                    .Select(g => new PopularWatchlistMovie
                    {
                        MovieId = g.Key.MovieId,
                        MovieName = g.Key.MovieName,
                        WatchlistCount = g.Count()
                    })
                    .OrderByDescending(p => p.WatchlistCount)
                    .ThenBy(p => p.MovieName)
                    .Take(limit)
                    .ToList()
                    .AsReadOnly();
            }
        }

        /// <summary>
        /// Creates a composite key for the (customerId, movieId) pair.
        /// </summary>
        private static string MakeKey(int customerId, int movieId)
        {
            return $"{customerId}:{movieId}";
        }

        /// <summary>
        /// Creates a defensive copy.
        /// </summary>
        private static WatchlistItem Clone(WatchlistItem source)
        {
            return new WatchlistItem
            {
                Id = source.Id,
                CustomerId = source.CustomerId,
                CustomerName = source.CustomerName,
                MovieId = source.MovieId,
                MovieName = source.MovieName,
                MovieGenre = source.MovieGenre,
                MovieRating = source.MovieRating,
                AddedDate = source.AddedDate,
                Note = source.Note,
                Priority = source.Priority
            };
        }
    }
}
