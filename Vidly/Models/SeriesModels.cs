using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A movie series/franchise (e.g. "The Lord of the Rings", "Star Wars").
    /// </summary>
    public class MovieSeries
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Series name is required.")]
        [StringLength(200)]
        public string Name { get; set; }

        /// <summary>Optional description of the series.</summary>
        [StringLength(1000)]
        public string Description { get; set; }

        /// <summary>Primary genre of the series.</summary>
        public Genre? Genre { get; set; }

        /// <summary>Whether the series is still ongoing (new installments expected).</summary>
        public bool IsOngoing { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Links a movie to a series with an explicit ordering.
    /// </summary>
    public class SeriesEntry
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }
        public int MovieId { get; set; }

        /// <summary>1-based position within the series (viewing order).</summary>
        [Range(1, 999)]
        public int OrderIndex { get; set; }

        /// <summary>Optional label like "Episode IV" or "Part 2".</summary>
        [StringLength(100)]
        public string Label { get; set; }
    }

    /// <summary>
    /// Tracks which series entries a customer has watched.
    /// </summary>
    public class SeriesProgress
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int SeriesEntryId { get; set; }
        public DateTime WatchedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Summary of a customer's progress through a series.
    /// </summary>
    public class SeriesProgressSummary
    {
        public int SeriesId { get; set; }
        public string SeriesName { get; set; }
        public int TotalMovies { get; set; }
        public int WatchedCount { get; set; }
        public double CompletionPercent { get; set; }
        public bool IsComplete { get; set; }

        /// <summary>Next unwatched movie in series order, or null if complete.</summary>
        public SeriesEntryDetail NextUp { get; set; }
    }

    /// <summary>
    /// A series entry enriched with movie details.
    /// </summary>
    public class SeriesEntryDetail
    {
        public int EntryId { get; set; }
        public int MovieId { get; set; }
        public string MovieName { get; set; }
        public int OrderIndex { get; set; }
        public string Label { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public bool Watched { get; set; }
    }
}
