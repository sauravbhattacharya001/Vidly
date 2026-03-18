using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryWaitlistRepository : IWaitlistRepository
    {
        private static readonly List<WaitlistEntry> _entries = new List<WaitlistEntry>();
        private static int _nextId = 1;
        private static bool _seeded;

        public InMemoryWaitlistRepository()
        {
            if (!_seeded)
            {
                _seeded = true;
                SeedData();
            }
        }

        private void SeedData()
        {
            var entries = new[]
            {
                new WaitlistEntry { CustomerId = 1, CustomerName = "Alice Johnson", MovieId = 3, MovieName = "The Matrix", JoinedAt = DateTime.Now.AddDays(-5), Priority = WaitlistPriority.Normal, Status = WaitlistStatus.Waiting, Position = 1 },
                new WaitlistEntry { CustomerId = 2, CustomerName = "Bob Smith", MovieId = 3, MovieName = "The Matrix", JoinedAt = DateTime.Now.AddDays(-3), Priority = WaitlistPriority.Normal, Status = WaitlistStatus.Waiting, Position = 2 },
                new WaitlistEntry { CustomerId = 3, CustomerName = "Carol Davis", MovieId = 5, MovieName = "Inception", JoinedAt = DateTime.Now.AddDays(-7), Priority = WaitlistPriority.High, Status = WaitlistStatus.Notified, NotifiedAt = DateTime.Now.AddDays(-1), ExpiresAt = DateTime.Now.AddDays(1), Position = 1 },
                new WaitlistEntry { CustomerId = 1, CustomerName = "Alice Johnson", MovieId = 7, MovieName = "Interstellar", JoinedAt = DateTime.Now.AddDays(-10), Priority = WaitlistPriority.Normal, Status = WaitlistStatus.Fulfilled, NotifiedAt = DateTime.Now.AddDays(-8), Position = 1 },
            };

            foreach (var e in entries)
            {
                e.Id = _nextId++;
                _entries.Add(e);
            }
        }

        public IEnumerable<WaitlistEntry> GetAll() =>
            _entries.OrderBy(e => e.MovieId).ThenBy(e => e.Position).ToList();

        public WaitlistEntry GetById(int id) =>
            _entries.FirstOrDefault(e => e.Id == id);

        public IEnumerable<WaitlistEntry> GetByCustomer(int customerId) =>
            _entries.Where(e => e.CustomerId == customerId).OrderBy(e => e.JoinedAt).ToList();

        public IEnumerable<WaitlistEntry> GetByMovie(int movieId) =>
            _entries.Where(e => e.MovieId == movieId).OrderBy(e => e.Position).ToList();

        public IEnumerable<WaitlistEntry> GetActiveByMovie(int movieId) =>
            _entries.Where(e => e.MovieId == movieId && e.Status == WaitlistStatus.Waiting)
                    .OrderBy(e => e.Position).ToList();

        public WaitlistEntry FindExisting(int customerId, int movieId) =>
            _entries.FirstOrDefault(e => e.CustomerId == customerId
                                         && e.MovieId == movieId
                                         && (e.Status == WaitlistStatus.Waiting || e.Status == WaitlistStatus.Notified));

        public void Add(WaitlistEntry entry)
        {
            entry.Id = _nextId++;
            // Set position to end of queue for this movie
            var existing = GetActiveByMovie(entry.MovieId);
            entry.Position = existing.Any() ? existing.Max(e => e.Position) + 1 : 1;
            entry.JoinedAt = DateTime.Now;
            entry.Status = WaitlistStatus.Waiting;
            _entries.Add(entry);
        }

        public void Update(WaitlistEntry entry)
        {
            var idx = _entries.FindIndex(e => e.Id == entry.Id);
            if (idx >= 0) _entries[idx] = entry;
        }

        public void Remove(int id)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id);
            if (entry != null)
            {
                _entries.Remove(entry);
                // Reorder positions for remaining entries on same movie
                var remaining = _entries.Where(e => e.MovieId == entry.MovieId && e.Status == WaitlistStatus.Waiting)
                                        .OrderBy(e => e.Position).ToList();
                for (int i = 0; i < remaining.Count; i++)
                    remaining[i].Position = i + 1;
            }
        }

        public WaitlistStats GetStats()
        {
            var stats = new WaitlistStats
            {
                TotalWaiting = _entries.Count(e => e.Status == WaitlistStatus.Waiting),
                TotalNotified = _entries.Count(e => e.Status == WaitlistStatus.Notified),
                TotalFulfilled = _entries.Count(e => e.Status == WaitlistStatus.Fulfilled),
                TotalExpired = _entries.Count(e => e.Status == WaitlistStatus.Expired),
                TotalCancelled = _entries.Count(e => e.Status == WaitlistStatus.Cancelled),
            };

            var completedEntries = _entries.Where(e => e.Status == WaitlistStatus.Fulfilled).ToList();
            stats.AverageWaitDays = completedEntries.Any()
                ? completedEntries.Average(e => e.WaitDuration.TotalDays)
                : 0;

            var waitingByMovie = _entries.Where(e => e.Status == WaitlistStatus.Waiting)
                                         .GroupBy(e => e.MovieName ?? "Unknown")
                                         .ToDictionary(g => g.Key, g => g.Count());

            stats.WaitlistByMovie = waitingByMovie;

            if (waitingByMovie.Any())
            {
                var top = waitingByMovie.OrderByDescending(kv => kv.Value).First();
                stats.MostWaitlistedMovie = top.Key;
                stats.MostWaitlistedCount = top.Value;
            }

            return stats;
        }
    }
}
