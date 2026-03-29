using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public interface ISoundtrackRepository
    {
        IEnumerable<SoundtrackTrack> GetAll();
        IEnumerable<SoundtrackTrack> GetByMovieId(int movieId);
        SoundtrackTrack GetById(int id);
        SoundtrackTrack Add(SoundtrackTrack track);
        void Rate(int id, int stars);
        void Delete(int id);
        IEnumerable<SoundtrackTrack> GetTopRated(int count);
        IEnumerable<SoundtrackTrack> Search(string query);
    }

    public class InMemorySoundtrackRepository : ISoundtrackRepository
    {
        private static readonly Dictionary<int, SoundtrackTrack> _tracks = new Dictionary<int, SoundtrackTrack>
        {
            [1] = new SoundtrackTrack
            {
                Id = 1, MovieId = 1, MovieName = "Shrek!",
                Title = "All Star", Artist = "Smash Mouth",
                DurationSeconds = 200, TrackNumber = 1, Genre = "Pop Rock",
                AverageRating = 4.8, RatingCount = 120,
                AddedAt = new DateTime(2026, 1, 1)
            },
            [2] = new SoundtrackTrack
            {
                Id = 2, MovieId = 1, MovieName = "Shrek!",
                Title = "I'm a Believer", Artist = "Smash Mouth",
                DurationSeconds = 198, TrackNumber = 2, Genre = "Pop Rock",
                AverageRating = 4.6, RatingCount = 95,
                AddedAt = new DateTime(2026, 1, 1)
            },
            [3] = new SoundtrackTrack
            {
                Id = 3, MovieId = 2, MovieName = "The Godfather",
                Title = "Main Theme (The Godfather Waltz)", Artist = "Nino Rota",
                DurationSeconds = 182, TrackNumber = 1, Genre = "Classical",
                AverageRating = 4.9, RatingCount = 200,
                AddedAt = new DateTime(2026, 1, 1)
            },
            [4] = new SoundtrackTrack
            {
                Id = 4, MovieId = 2, MovieName = "The Godfather",
                Title = "Love Theme from The Godfather", Artist = "Nino Rota",
                DurationSeconds = 175, TrackNumber = 2, Genre = "Classical",
                AverageRating = 4.7, RatingCount = 180,
                AddedAt = new DateTime(2026, 1, 1)
            },
            [5] = new SoundtrackTrack
            {
                Id = 5, MovieId = 3, MovieName = "Toy Story",
                Title = "You've Got a Friend in Me", Artist = "Randy Newman",
                DurationSeconds = 122, TrackNumber = 1, Genre = "Pop",
                AverageRating = 4.9, RatingCount = 250,
                AddedAt = new DateTime(2026, 1, 1)
            },
            [6] = new SoundtrackTrack
            {
                Id = 6, MovieId = 3, MovieName = "Toy Story",
                Title = "Strange Things", Artist = "Randy Newman",
                DurationSeconds = 217, TrackNumber = 2, Genre = "Pop",
                AverageRating = 4.2, RatingCount = 65,
                AddedAt = new DateTime(2026, 1, 1)
            }
        };

        private static readonly object _lock = new object();
        private static int _nextId = 7;

        public IEnumerable<SoundtrackTrack> GetAll()
        {
            lock (_lock)
            {
                return _tracks.Values
                    .OrderBy(t => t.MovieName).ThenBy(t => t.TrackNumber)
                    .Select(Clone).ToList();
            }
        }

        public IEnumerable<SoundtrackTrack> GetByMovieId(int movieId)
        {
            lock (_lock)
            {
                return _tracks.Values
                    .Where(t => t.MovieId == movieId)
                    .OrderBy(t => t.TrackNumber)
                    .Select(Clone).ToList();
            }
        }

        public SoundtrackTrack GetById(int id)
        {
            lock (_lock)
            {
                return _tracks.TryGetValue(id, out var t) ? Clone(t) : null;
            }
        }

        public SoundtrackTrack Add(SoundtrackTrack track)
        {
            lock (_lock)
            {
                track.Id = _nextId++;
                track.AddedAt = DateTime.UtcNow;
                track.AverageRating = 0;
                track.RatingCount = 0;
                _tracks[track.Id] = Clone(track);
                return Clone(track);
            }
        }

        public void Rate(int id, int stars)
        {
            if (stars < 1 || stars > 5) return;
            lock (_lock)
            {
                if (_tracks.TryGetValue(id, out var t))
                {
                    var total = t.AverageRating * t.RatingCount + stars;
                    t.RatingCount++;
                    t.AverageRating = Math.Round(total / t.RatingCount, 1);
                }
            }
        }

        public void Delete(int id)
        {
            lock (_lock) { _tracks.Remove(id); }
        }

        public IEnumerable<SoundtrackTrack> GetTopRated(int count)
        {
            lock (_lock)
            {
                return _tracks.Values
                    .Where(t => t.RatingCount >= 5)
                    .OrderByDescending(t => t.AverageRating)
                    .ThenByDescending(t => t.RatingCount)
                    .Take(count)
                    .Select(Clone).ToList();
            }
        }

        public IEnumerable<SoundtrackTrack> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return GetAll();

            var q = query.ToLowerInvariant();
            lock (_lock)
            {
                return _tracks.Values
                    .Where(t => t.Title.ToLowerInvariant().Contains(q)
                             || t.Artist.ToLowerInvariant().Contains(q)
                             || t.MovieName.ToLowerInvariant().Contains(q)
                             || (t.Genre != null && t.Genre.ToLowerInvariant().Contains(q)))
                    .OrderByDescending(t => t.AverageRating)
                    .Select(Clone).ToList();
            }
        }

        private static SoundtrackTrack Clone(SoundtrackTrack t)
        {
            return new SoundtrackTrack
            {
                Id = t.Id, MovieId = t.MovieId, MovieName = t.MovieName,
                Title = t.Title, Artist = t.Artist,
                DurationSeconds = t.DurationSeconds, TrackNumber = t.TrackNumber,
                Genre = t.Genre, AverageRating = t.AverageRating,
                RatingCount = t.RatingCount, AddedAt = t.AddedAt
            };
        }
    }
}
