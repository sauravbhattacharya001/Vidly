using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Vidly.Models
{
    /// <summary>
    /// A customer's gift registry — a public wishlist of movies they'd like
    /// to receive as gift rentals from friends and family.
    /// </summary>
    public class GiftRegistry
    {
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "Registry name cannot exceed 100 characters.")]
        public string Name { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        public GiftRegistryOccasion Occasion { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? EventDate { get; set; }

        public bool IsPublic { get; set; } = true;

        /// <summary>Unique shareable code for this registry.</summary>
        public string ShareCode { get; set; }

        public List<GiftRegistryItem> Items { get; set; } = new List<GiftRegistryItem>();
    }

    public class GiftRegistryItem
    {
        public int Id { get; set; }

        public int RegistryId { get; set; }

        public int MovieId { get; set; }

        public string MovieName { get; set; }

        [StringLength(200)]
        public string Note { get; set; }

        public GiftRegistryItemStatus Status { get; set; } = GiftRegistryItemStatus.Wanted;

        /// <summary>Name of the person who fulfilled (gifted) this item.</summary>
        public string FulfilledBy { get; set; }

        public DateTime? FulfilledAt { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.Now;
    }

    public enum GiftRegistryOccasion
    {
        Birthday,
        Holiday,
        Anniversary,
        Graduation,
        JustBecause,
        MovieMarathon,
        Other
    }

    public enum GiftRegistryItemStatus
    {
        Wanted,
        Fulfilled,
        Removed
    }
}
