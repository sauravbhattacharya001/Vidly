using System;
using System.Collections.Generic;

namespace Vidly.Models
{
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

    public enum LostItemStatus
    {
        Found,
        ClaimPending,
        Claimed,
        Disposed,
        Donated
    }

    public class LostItem
    {
        public int Id { get; set; }
        public string Description { get; set; }
        public LostItemCategory Category { get; set; }
        public string LocationFound { get; set; }
        public DateTime FoundAt { get; set; }
        public string FoundByStaffId { get; set; }
        public LostItemStatus Status { get; set; }
        public string Color { get; set; }
        public string Brand { get; set; }
        public string Notes { get; set; }
        public string StorageBin { get; set; }
        public DateTime? DisposalDate { get; set; }
        public int RetentionDays { get; set; } = 30;
        public DateTime? ClaimedAt { get; set; }
        public int? ClaimedByCustomerId { get; set; }
    }

    public class LostItemClaim
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerDescription { get; set; }
        public DateTime ClaimDate { get; set; }
        public bool Verified { get; set; }
        public string VerifiedByStaffId { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public bool Rejected { get; set; }
        public string RejectionReason { get; set; }
    }

    public class LostAndFoundReport
    {
        public int TotalItems { get; set; }
        public int Unclaimed { get; set; }
        public int Claimed { get; set; }
        public int Disposed { get; set; }
        public int Donated { get; set; }
        public int PendingClaims { get; set; }
        public int OverdueForDisposal { get; set; }
        public double AverageDaysToClaimOrDefault { get; set; }
        public double ClaimRate { get; set; }
        public Dictionary<LostItemCategory, int> ByCategory { get; set; } = new Dictionary<LostItemCategory, int>();
        public Dictionary<string, int> TopLocations { get; set; } = new Dictionary<string, int>();
        public List<LostItem> OverdueItems { get; set; } = new List<LostItem>();
    }
}
