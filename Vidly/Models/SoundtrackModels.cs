using System;

namespace Vidly.Models
{
    /// <summary>
    /// Represents a song or track from a movie's soundtrack.
    /// </summary>
    public class SoundtrackTrack
    {
        public int Id { get; set; }

        /// <summary>The movie this track belongs to.</summary>
        public int MovieId { get; set; }

        /// <summary>Movie name (denormalized for display).</summary>
        public string MovieName { get; set; }

        /// <summary>Song/track title.</summary>
        public string Title { get; set; }

        /// <summary>Artist or composer name.</summary>
        public string Artist { get; set; }

        /// <summary>Duration in seconds.</summary>
        public int DurationSeconds { get; set; }

        /// <summary>Track number on the album.</summary>
        public int TrackNumber { get; set; }

        /// <summary>Genre of the track (e.g., Pop, Classical, Rock).</summary>
        public string Genre { get; set; }

        /// <summary>Average rating (1-5 stars).</summary>
        public double AverageRating { get; set; }

        /// <summary>Total number of ratings.</summary>
        public int RatingCount { get; set; }

        /// <summary>When this track was added to the system.</summary>
        public DateTime AddedAt { get; set; }

        /// <summary>Formats duration as mm:ss.</summary>
        public string FormattedDuration
        {
            get
            {
                var minutes = DurationSeconds / 60;
                var seconds = DurationSeconds % 60;
                return $"{minutes}:{seconds:D2}";
            }
        }
    }
}
