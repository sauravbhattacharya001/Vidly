using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A curated themed list of movies (e.g., "Summer Action", "Classic Comedies").
    /// Think "playlists for movies."
    /// </summary>
    public class MovieCollection
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Collection name is required.")]
        [StringLength(100, ErrorMessage = "Collection name cannot exceed 100 characters.")]
        public string Name { get; set; }

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string Description { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Whether the collection is visible to customers (true) or still a draft (false).
        /// </summary>
        public bool IsPublished { get; set; }

        /// <summary>
        /// Movies in this collection, maintained in sorted order.
        /// </summary>
        public List<CollectionItem> Items { get; set; } = new List<CollectionItem>();

        /// <summary>
        /// Number of movies currently in the collection.
        /// </summary>
        public int MovieCount => Items?.Count ?? 0;
    }

    /// <summary>
    /// An entry in a MovieCollection, linking to a movie with ordering info.
    /// </summary>
    public class CollectionItem
    {
        /// <summary>Maximum allowed length for collection item notes.</summary>
        public const int MaxNoteLength = 500;

        public int MovieId { get; set; }
        public int SortOrder { get; set; }

        /// <summary>
        /// Optional note about why this movie is in the collection.
        /// </summary>
        [StringLength(MaxNoteLength, ErrorMessage = "Note cannot exceed 500 characters.")]
        public string Note { get; set; }
    }
}
