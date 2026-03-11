using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Manages movie series/franchises — create series, add movies in order,
    /// track per-customer watch progress, and recommend the next installment.
    /// </summary>
    public class MovieSeriesService
    {
        private readonly List<MovieSeries> _series = new List<MovieSeries>();
        private readonly List<SeriesEntry> _entries = new List<SeriesEntry>();
        private readonly List<SeriesProgress> _progress = new List<SeriesProgress>();

        private int _nextSeriesId = 1;
        private int _nextEntryId = 1;
        private int _nextProgressId = 1;

        // ── Series CRUD ─────────────────────────────────────────────

        /// <summary>Creates a new movie series.</summary>
        public MovieSeries CreateSeries(string name, string description = null, Genre? genre = null, bool isOngoing = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Series name is required.", nameof(name));

            if (_series.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A series named '{name}' already exists.");

            var series = new MovieSeries
            {
                Id = _nextSeriesId++,
                Name = name.Trim(),
                Description = description?.Trim(),
                Genre = genre,
                IsOngoing = isOngoing,
                CreatedAt = DateTime.UtcNow
            };

            _series.Add(series);
            return series;
        }

        /// <summary>Gets a series by ID.</summary>
        public MovieSeries GetSeries(int seriesId)
        {
            return _series.FirstOrDefault(s => s.Id == seriesId);
        }

        /// <summary>Lists all series, optionally filtered by genre.</summary>
        public List<MovieSeries> ListSeries(Genre? genre = null)
        {
            var query = _series.AsEnumerable();
            if (genre.HasValue)
                query = query.Where(s => s.Genre == genre.Value);
            return query.OrderBy(s => s.Name).ToList();
        }

        /// <summary>Searches series by name substring (case-insensitive).</summary>
        public List<MovieSeries> SearchSeries(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<MovieSeries>();

            return _series
                .Where(s => s.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => s.Name)
                .ToList();
        }

        /// <summary>Deletes a series and all its entries and progress records.</summary>
        public bool DeleteSeries(int seriesId)
        {
            var series = _series.FirstOrDefault(s => s.Id == seriesId);
            if (series == null) return false;

            var entryIds = _entries.Where(e => e.SeriesId == seriesId).Select(e => e.Id).ToHashSet();
            _progress.RemoveAll(p => entryIds.Contains(p.SeriesEntryId));
            _entries.RemoveAll(e => e.SeriesId == seriesId);
            _series.Remove(series);
            return true;
        }

        // ── Series Entries ──────────────────────────────────────────

        /// <summary>Adds a movie to a series at the given position.</summary>
        public SeriesEntry AddMovie(int seriesId, int movieId, int orderIndex, string label = null)
        {
            if (GetSeries(seriesId) == null)
                throw new ArgumentException($"Series {seriesId} not found.");

            if (orderIndex < 1)
                throw new ArgumentException("Order index must be >= 1.");

            if (_entries.Any(e => e.SeriesId == seriesId && e.MovieId == movieId))
                throw new InvalidOperationException("Movie is already in this series.");

            if (_entries.Any(e => e.SeriesId == seriesId && e.OrderIndex == orderIndex))
                throw new InvalidOperationException($"Order index {orderIndex} is already taken in this series.");

            var entry = new SeriesEntry
            {
                Id = _nextEntryId++,
                SeriesId = seriesId,
                MovieId = movieId,
                OrderIndex = orderIndex,
                Label = label?.Trim()
            };

            _entries.Add(entry);
            return entry;
        }

        /// <summary>Removes a movie from a series.</summary>
        public bool RemoveMovie(int seriesId, int movieId)
        {
            var entry = _entries.FirstOrDefault(e => e.SeriesId == seriesId && e.MovieId == movieId);
            if (entry == null) return false;

            _progress.RemoveAll(p => p.SeriesEntryId == entry.Id);
            _entries.Remove(entry);
            return true;
        }

        /// <summary>Gets all entries for a series in viewing order.</summary>
        public List<SeriesEntry> GetSeriesEntries(int seriesId)
        {
            return _entries
                .Where(e => e.SeriesId == seriesId)
                .OrderBy(e => e.OrderIndex)
                .ToList();
        }

        /// <summary>Reorders an entry within its series.</summary>
        public bool ReorderEntry(int entryId, int newOrderIndex)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry == null) return false;

            if (newOrderIndex < 1)
                throw new ArgumentException("Order index must be >= 1.");

            if (_entries.Any(e => e.SeriesId == entry.SeriesId && e.Id != entryId && e.OrderIndex == newOrderIndex))
                throw new InvalidOperationException($"Order index {newOrderIndex} is already taken.");

            entry.OrderIndex = newOrderIndex;
            return true;
        }

        // ── Progress Tracking ───────────────────────────────────────

        /// <summary>Marks a series entry as watched by a customer.</summary>
        public SeriesProgress MarkWatched(int customerId, int seriesEntryId)
        {
            if (customerId <= 0)
                throw new ArgumentException("Valid customer ID required.");

            var entry = _entries.FirstOrDefault(e => e.Id == seriesEntryId);
            if (entry == null)
                throw new ArgumentException($"Series entry {seriesEntryId} not found.");

            if (_progress.Any(p => p.CustomerId == customerId && p.SeriesEntryId == seriesEntryId))
                throw new InvalidOperationException("Already marked as watched.");

            var prog = new SeriesProgress
            {
                Id = _nextProgressId++,
                CustomerId = customerId,
                SeriesEntryId = seriesEntryId,
                WatchedAt = DateTime.UtcNow
            };

            _progress.Add(prog);
            return prog;
        }

        /// <summary>Unmarks a series entry as watched.</summary>
        public bool UnmarkWatched(int customerId, int seriesEntryId)
        {
            var prog = _progress.FirstOrDefault(p => p.CustomerId == customerId && p.SeriesEntryId == seriesEntryId);
            if (prog == null) return false;

            _progress.Remove(prog);
            return true;
        }

        /// <summary>Gets detailed progress summary for a customer across a specific series.</summary>
        public SeriesProgressSummary GetProgress(int customerId, int seriesId, List<Movie> movieLookup)
        {
            var series = GetSeries(seriesId);
            if (series == null) return null;

            var entries = GetSeriesEntries(seriesId);
            if (!entries.Any())
                return new SeriesProgressSummary
                {
                    SeriesId = seriesId,
                    SeriesName = series.Name,
                    TotalMovies = 0,
                    WatchedCount = 0,
                    CompletionPercent = 0,
                    IsComplete = false
                };

            var watchedEntryIds = _progress
                .Where(p => p.CustomerId == customerId)
                .Select(p => p.SeriesEntryId)
                .ToHashSet();

            var details = entries.Select(e =>
            {
                var movie = movieLookup?.FirstOrDefault(m => m.Id == e.MovieId);
                return new SeriesEntryDetail
                {
                    EntryId = e.Id,
                    MovieId = e.MovieId,
                    MovieName = movie?.Name ?? $"Movie #{e.MovieId}",
                    OrderIndex = e.OrderIndex,
                    Label = e.Label,
                    ReleaseDate = movie?.ReleaseDate,
                    Watched = watchedEntryIds.Contains(e.Id)
                };
            }).ToList();

            var watched = details.Count(d => d.Watched);
            var nextUp = details.FirstOrDefault(d => !d.Watched);

            return new SeriesProgressSummary
            {
                SeriesId = seriesId,
                SeriesName = series.Name,
                TotalMovies = details.Count,
                WatchedCount = watched,
                CompletionPercent = Math.Round(100.0 * watched / details.Count, 1),
                IsComplete = watched == details.Count,
                NextUp = nextUp
            };
        }

        /// <summary>Gets progress across all series for a customer.</summary>
        public List<SeriesProgressSummary> GetAllProgress(int customerId, List<Movie> movieLookup)
        {
            return _series
                .Select(s => GetProgress(customerId, s.Id, movieLookup))
                .Where(p => p != null)
                .OrderBy(p => p.IsComplete)
                .ThenBy(p => p.SeriesName)
                .ToList();
        }

        /// <summary>Gets "next up" recommendations — one per in-progress series.</summary>
        public List<SeriesEntryDetail> GetNextUpRecommendations(int customerId, List<Movie> movieLookup)
        {
            return GetAllProgress(customerId, movieLookup)
                .Where(p => !p.IsComplete && p.NextUp != null)
                .Select(p => p.NextUp)
                .ToList();
        }

        /// <summary>Finds all series a given movie belongs to.</summary>
        public List<MovieSeries> GetSeriesForMovie(int movieId)
        {
            var seriesIds = _entries
                .Where(e => e.MovieId == movieId)
                .Select(e => e.SeriesId)
                .Distinct();

            return _series.Where(s => seriesIds.Contains(s.Id)).ToList();
        }

        /// <summary>Gets stats: total series, total entries, most popular series by progress records.</summary>
        public SeriesStats GetStats()
        {
            var popularSeriesId = _progress
                .Join(_entries, p => p.SeriesEntryId, e => e.Id, (p, e) => e.SeriesId)
                .GroupBy(id => id)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            return new SeriesStats
            {
                TotalSeries = _series.Count,
                TotalEntries = _entries.Count,
                TotalProgressRecords = _progress.Count,
                MostPopularSeries = GetSeries(popularSeriesId)?.Name
            };
        }
    }

    /// <summary>Aggregate statistics about the series system.</summary>
    public class SeriesStats
    {
        public int TotalSeries { get; set; }
        public int TotalEntries { get; set; }
        public int TotalProgressRecords { get; set; }
        public string MostPopularSeries { get; set; }
    }
}
