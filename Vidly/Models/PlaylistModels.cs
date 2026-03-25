using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A curated, ordered playlist of movies that customers can create and share.
    /// Unlike watchlists (personal to-watch lists) or collections (store-curated),
    /// playlists are ordered sequences with per-entry notes, designed for sharing.
    /// </summary>
    public class Playlist
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Playlist name is required.")]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
        public string Name { get; set; }

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string Description { get; set; }

        public int CreatedByCustomerId { get; set; }
        public string CreatedByCustomerName { get; set; }

        public DateTime CreatedDate { get; set; }
        public DateTime LastModifiedDate { get; set; }

        public bool IsPublic { get; set; }

        /// <summary>
        /// Number of times this playlist has been viewed by others.
        /// </summary>
        public int ViewCount { get; set; }

        /// <summary>
        /// Number of times other customers have copied/forked this playlist.
        /// </summary>
        public int ForkCount { get; set; }

        public List<PlaylistEntry> Entries { get; set; } = new List<PlaylistEntry>();

        public int MovieCount => Entries?.Count ?? 0;

        public TimeSpan? EstimatedDuration
        {
            get
            {
                if (Entries == null || Entries.Count == 0) return null;
                // Assume ~2 hours per movie as a rough estimate
                return TimeSpan.FromHours(Entries.Count * 2);
            }
        }
    }

    public class PlaylistEntry
    {
        public int Id { get; set; }
        public int PlaylistId { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public Genre? MovieGenre { get; set; }
        public int? MovieRating { get; set; }

        /// <summary>
        /// Position in the playlist (1-based).
        /// </summary>
        public int Position { get; set; }

        /// <summary>
        /// Optional note about why this movie is in the playlist,
        /// e.g. "The one that started it all" or "Best watched after #3".
        /// </summary>
        [StringLength(300, ErrorMessage = "Note cannot exceed 300 characters.")]
        public string Note { get; set; }

        public DateTime AddedDate { get; set; }
    }
}
