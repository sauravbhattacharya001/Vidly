using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryPlaylistRepository : IPlaylistRepository
    {
        private static readonly List<Playlist> _playlists = new List<Playlist>();
        private static int _nextId = 1;
        private static int _nextEntryId = 1;
        private static readonly object _lock = new object();

        static InMemoryPlaylistRepository()
        {
            // Seed some sample playlists
            var sciFiMarathon = new Playlist
            {
                Id = _nextId++,
                Name = "Sci-Fi Essentials",
                Description = "Must-watch science fiction films for any fan of the genre.",
                CreatedByCustomerId = 1,
                CreatedByCustomerName = "John Smith",
                CreatedDate = DateTime.Today.AddDays(-30),
                LastModifiedDate = DateTime.Today.AddDays(-2),
                IsPublic = true,
                ViewCount = 42,
                ForkCount = 3
            };
            _playlists.Add(sciFiMarathon);

            var dateNight = new Playlist
            {
                Id = _nextId++,
                Name = "Date Night Picks",
                Description = "Romantic comedies and dramas perfect for a cozy evening.",
                CreatedByCustomerId = 2,
                CreatedByCustomerName = "Mary Williams",
                CreatedDate = DateTime.Today.AddDays(-14),
                LastModifiedDate = DateTime.Today.AddDays(-5),
                IsPublic = true,
                ViewCount = 18,
                ForkCount = 1
            };
            _playlists.Add(dateNight);
        }

        public IReadOnlyList<Playlist> GetByCustomer(int customerId)
        {
            lock (_lock)
            {
                return _playlists
                    .Where(p => p.CreatedByCustomerId == customerId)
                    .OrderByDescending(p => p.LastModifiedDate)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<Playlist> GetPublicPlaylists(int limit = 20)
        {
            lock (_lock)
            {
                return _playlists
                    .Where(p => p.IsPublic)
                    .OrderByDescending(p => p.ViewCount)
                    .Take(limit)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public Playlist GetById(int id)
        {
            lock (_lock)
            {
                return _playlists.FirstOrDefault(p => p.Id == id);
            }
        }

        public void Add(Playlist playlist)
        {
            lock (_lock)
            {
                playlist.Id = _nextId++;
                playlist.CreatedDate = DateTime.Now;
                playlist.LastModifiedDate = DateTime.Now;
                _playlists.Add(playlist);
            }
        }

        public void Update(Playlist playlist)
        {
            lock (_lock)
            {
                var existing = _playlists.FirstOrDefault(p => p.Id == playlist.Id);
                if (existing == null)
                    throw new KeyNotFoundException($"Playlist {playlist.Id} not found.");

                existing.Name = playlist.Name;
                existing.Description = playlist.Description;
                existing.IsPublic = playlist.IsPublic;
                existing.LastModifiedDate = DateTime.Now;
            }
        }

        public void Delete(int id)
        {
            lock (_lock)
            {
                var playlist = _playlists.FirstOrDefault(p => p.Id == id);
                if (playlist == null)
                    throw new KeyNotFoundException($"Playlist {id} not found.");
                _playlists.Remove(playlist);
            }
        }

        public void AddEntry(int playlistId, PlaylistEntry entry)
        {
            lock (_lock)
            {
                var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
                if (playlist == null)
                    throw new KeyNotFoundException($"Playlist {playlistId} not found.");

                if (playlist.Entries.Any(e => e.MovieId == entry.MovieId))
                    throw new InvalidOperationException("This movie is already in the playlist.");

                entry.Id = _nextEntryId++;
                entry.PlaylistId = playlistId;
                entry.Position = playlist.Entries.Count + 1;
                entry.AddedDate = DateTime.Now;
                playlist.Entries.Add(entry);
                playlist.LastModifiedDate = DateTime.Now;
            }
        }

        public void RemoveEntry(int playlistId, int entryId)
        {
            lock (_lock)
            {
                var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
                if (playlist == null)
                    throw new KeyNotFoundException($"Playlist {playlistId} not found.");

                var entry = playlist.Entries.FirstOrDefault(e => e.Id == entryId);
                if (entry == null)
                    throw new KeyNotFoundException($"Entry {entryId} not found.");

                playlist.Entries.Remove(entry);
                // Re-number positions
                var sorted = playlist.Entries.OrderBy(e => e.Position).ToList();
                for (int i = 0; i < sorted.Count; i++)
                    sorted[i].Position = i + 1;

                playlist.LastModifiedDate = DateTime.Now;
            }
        }

        public void MoveEntry(int playlistId, int entryId, int newPosition)
        {
            lock (_lock)
            {
                var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
                if (playlist == null)
                    throw new KeyNotFoundException($"Playlist {playlistId} not found.");

                var entry = playlist.Entries.FirstOrDefault(e => e.Id == entryId);
                if (entry == null)
                    throw new KeyNotFoundException($"Entry {entryId} not found.");

                if (newPosition < 1) newPosition = 1;
                if (newPosition > playlist.Entries.Count) newPosition = playlist.Entries.Count;

                var sorted = playlist.Entries.OrderBy(e => e.Position).ToList();
                sorted.Remove(entry);
                sorted.Insert(newPosition - 1, entry);
                for (int i = 0; i < sorted.Count; i++)
                    sorted[i].Position = i + 1;

                playlist.LastModifiedDate = DateTime.Now;
            }
        }
    }
}
