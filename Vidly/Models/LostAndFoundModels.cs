using System;
using System.Collections.Generic;

namespace Vidly.Models
{
    /// <summary>
    /// Category classification for items found at the rental store.
    /// Used for organizing storage bins and generating lost-and-found reports.
    /// </summary>
    public enum LostItemCategory
    {
        PersonalAccessory,
        Electronics,
        Clothing,
        Bag,
        Keys,
        Wallet,
        Glasses,
        Umbrella,
        Book,
        Toy,
        Jewelry,
        Other
    }

    /// <summary>
    /// Lifecycle status of a lost item, from discovery through final disposition.
    /// </summary>
    public enum LostItemStatus
    {
        /// <summary>Item has been found and logged but no claim has been made.</summary>
        Found,
        /// <summary>A customer has submitted a claim that is awaiting staff verification.</summary>
        ClaimPending,
        /// <summary>Item has been verified and returned to its owner.</summary>
        Claimed,
        /// <summary>Item exceeded its retention period and was disposed of.</summary>
        Disposed,
        /// <summary>Item exceeded its retention period and was donated to charity.</summary>
        Donated
    }

    /// <summary>
    /// Represents a physical item found at the store and entered into the lost-and-found system.
    /// Tracks the item's description, discovery context, storage location, and disposition.
    /// </summary>
    public class LostItem
    {
        public int Id { get; set; }

        /// <summary>Free-text description of the item (e.g. "Red scarf, wool blend").</summary>
        public string Description { get; set; }

        public LostItemCategory Category { get; set; }

        /// <summary>Where in the store the item was found (e.g. "Aisle 3", "Restroom").</summary>
        public string LocationFound { get; set; }

        /// <summary>Timestamp when the item was discovered by staff.</summary>
        public DateTime FoundAt { get; set; }

        /// <summary>Staff member ID who logged the found item.</summary>
        public string FoundByStaffId { get; set; }

        public LostItemStatus Status { get; set; }

        /// <summary>Primary color of the item, used for matching against claims.</summary>
        public string Color { get; set; }

        /// <summary>Brand name if identifiable (e.g. "Ray-Ban", "Samsung").</summary>
        public string Brand { get; set; }

        /// <summary>Additional notes (e.g. "Has initials 'JD' engraved").</summary>
        public string Notes { get; set; }

        /// <summary>Physical storage bin identifier where the item is kept.</summary>
        public string StorageBin { get; set; }

        /// <summary>Date when the item was disposed of or donated, if applicable.</summary>
        public DateTime? DisposalDate { get; set; }

        /// <summary>Number of days to retain the item before disposal/donation. Default 30.</summary>
        public int RetentionDays { get; set; } = 30;

        /// <summary>Timestamp when the item was successfully claimed and returned.</summary>
        public DateTime? ClaimedAt { get; set; }

        /// <summary>Customer ID of the person who claimed the item.</summary>
        public int? ClaimedByCustomerId { get; set; }
    }

    /// <summary>
    /// A customer's claim on a lost item. Claims require staff verification
    /// before the item can be released. Multiple claims can exist for the same item.
    /// </summary>
    public class LostItemClaim
    {
        public int Id { get; set; }

        /// <summary>ID of the lost item being claimed.</summary>
        public int ItemId { get; set; }

        /// <summary>ID of the customer making the claim.</summary>
        public int CustomerId { get; set; }

        /// <summary>Customer's description of the item, used by staff to verify ownership.</summary>
        public string CustomerDescription { get; set; }

        /// <summary>When the claim was submitted.</summary>
        public DateTime ClaimDate { get; set; }

        /// <summary>Whether staff has verified the claim matches the item.</summary>
        public bool Verified { get; set; }

        /// <summary>Staff member who performed the verification.</summary>
        public string VerifiedByStaffId { get; set; }

        /// <summary>Timestamp of verification, if completed.</summary>
        public DateTime? VerifiedAt { get; set; }

        /// <summary>Whether the claim was rejected (description didn't match).</summary>
        public bool Rejected { get; set; }

        /// <summary>Reason for rejection, provided by verifying staff.</summary>
        public string RejectionReason { get; set; }
    }

    /// <summary>
    /// Aggregate statistics for the lost-and-found system, including item counts
    /// by status, category breakdowns, and operational metrics like claim rate
    /// and average days to claim.
    /// </summary>
    public class LostAndFoundReport
    {
        /// <summary>Total number of items ever entered into the system.</summary>
        public int TotalItems { get; set; }

        /// <summary>Items currently in Found status with no pending claim.</summary>
        public int Unclaimed { get; set; }

        /// <summary>Items that have been claimed and returned.</summary>
        public int Claimed { get; set; }

        /// <summary>Items that were disposed of after exceeding retention.</summary>
        public int Disposed { get; set; }

        /// <summary>Items that were donated after exceeding retention.</summary>
        public int Donated { get; set; }

        /// <summary>Number of unresolved claims awaiting staff verification.</summary>
        public int PendingClaims { get; set; }

        /// <summary>Unclaimed items past their retention deadline.</summary>
        public int OverdueForDisposal { get; set; }

        /// <summary>Average days from item discovery to claim or disposal.</summary>
        public double AverageDaysToClaimOrDefault { get; set; }

        /// <summary>Fraction of found items that are eventually claimed (0.0–1.0).</summary>
        public double ClaimRate { get; set; }

        /// <summary>Item count broken down by category.</summary>
        public Dictionary<LostItemCategory, int> ByCategory { get; set; } = new Dictionary<LostItemCategory, int>();

        /// <summary>Most common locations where items are found, with counts.</summary>
        public Dictionary<string, int> TopLocations { get; set; } = new Dictionary<string, int>();

        /// <summary>List of items that have exceeded their retention period.</summary>
        public List<LostItem> OverdueItems { get; set; } = new List<LostItem>();
    }
}
