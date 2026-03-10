using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Services
{
    /// <summary>
    /// Lost &amp; Found service for tracking items left behind by customers:
    /// item registration, claiming workflow, verification, auto-disposal,
    /// keyword matching, and store-wide reporting.
    /// </summary>
    public class LostAndFoundService
    {
        private readonly List<LostItem> _items = new List<LostItem>();
        private readonly List<LostItemClaim> _claims = new List<LostItemClaim>();
        private int _nextItemId = 1;
        private int _nextClaimId = 1;

        // ── Item Registration ──────────────────────────────────────

        /// <summary>Register a found item.</summary>
        public LostItem RegisterItem(LostItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrWhiteSpace(item.Description))
                throw new ArgumentException("Description is required.");
            if (string.IsNullOrWhiteSpace(item.LocationFound))
                throw new ArgumentException("Location found is required.");
            if (string.IsNullOrWhiteSpace(item.FoundByStaffId))
                throw new ArgumentException("Staff ID is required.");

            item.Id = _nextItemId++;
            item.Status = LostItemStatus.Found;
            if (item.FoundAt == default) item.FoundAt = DateTime.Now;
            if (item.RetentionDays <= 0) item.RetentionDays = 30;
            _items.Add(item);
            return item;
        }

        /// <summary>Get item by ID.</summary>
        public LostItem GetById(int id)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item == null)
                throw new KeyNotFoundException($"Lost item {id} not found.");
            return item;
        }

        /// <summary>List items with optional filters.</summary>
        public List<LostItem> ListItems(
            LostItemStatus? status = null,
            LostItemCategory? category = null,
            string locationContains = null,
            DateTime? foundAfter = null,
            DateTime? foundBefore = null)
        {
            var q = _items.AsEnumerable();
            if (status.HasValue) q = q.Where(x => x.Status == status.Value);
            if (category.HasValue) q = q.Where(x => x.Category == category.Value);
            if (!string.IsNullOrWhiteSpace(locationContains))
                q = q.Where(x => x.LocationFound != null &&
                    x.LocationFound.IndexOf(locationContains, StringComparison.OrdinalIgnoreCase) >= 0);
            if (foundAfter.HasValue) q = q.Where(x => x.FoundAt >= foundAfter.Value);
            if (foundBefore.HasValue) q = q.Where(x => x.FoundAt <= foundBefore.Value);
            return q.OrderByDescending(x => x.FoundAt).ToList();
        }

        /// <summary>Update an item's details (only if still Found or ClaimPending).</summary>
        public LostItem UpdateItem(int id, Action<LostItem> modifier)
        {
            var item = GetById(id);
            if (item.Status == LostItemStatus.Claimed || item.Status == LostItemStatus.Disposed || item.Status == LostItemStatus.Donated)
                throw new InvalidOperationException($"Cannot update item in {item.Status} status.");
            modifier(item);
            if (string.IsNullOrWhiteSpace(item.Description))
                throw new ArgumentException("Description cannot be empty.");
            return item;
        }

        // ── Claiming ──────────────────────────────────────────────

        /// <summary>Submit a claim for a lost item.</summary>
        public LostItemClaim SubmitClaim(int itemId, int customerId, string description)
        {
            var item = GetById(itemId);
            if (item.Status == LostItemStatus.Claimed)
                throw new InvalidOperationException("Item has already been claimed.");
            if (item.Status == LostItemStatus.Disposed || item.Status == LostItemStatus.Donated)
                throw new InvalidOperationException("Item is no longer available.");
            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("Customer must describe the item to claim it.");

            // Check for duplicate claim by same customer
            if (_claims.Any(c => c.ItemId == itemId && c.CustomerId == customerId && !c.Rejected))
                throw new InvalidOperationException("Customer already has a pending claim for this item.");

            var claim = new LostItemClaim
            {
                Id = _nextClaimId++,
                ItemId = itemId,
                CustomerId = customerId,
                CustomerDescription = description,
                ClaimDate = DateTime.Now,
                Verified = false,
                Rejected = false
            };
            _claims.Add(claim);
            item.Status = LostItemStatus.ClaimPending;
            return claim;
        }

        /// <summary>Verify and approve a claim (staff action).</summary>
        public LostItemClaim ApproveClaim(int claimId, string staffId)
        {
            var claim = _claims.FirstOrDefault(c => c.Id == claimId);
            if (claim == null) throw new KeyNotFoundException($"Claim {claimId} not found.");
            if (claim.Rejected) throw new InvalidOperationException("Claim was already rejected.");
            if (claim.Verified) throw new InvalidOperationException("Claim was already approved.");

            var item = GetById(claim.ItemId);
            if (item.Status == LostItemStatus.Claimed)
                throw new InvalidOperationException("Item was already claimed via another claim.");

            claim.Verified = true;
            claim.VerifiedByStaffId = staffId;
            claim.VerifiedAt = DateTime.Now;
            item.Status = LostItemStatus.Claimed;
            item.ClaimedAt = DateTime.Now;
            item.ClaimedByCustomerId = claim.CustomerId;

            // Reject all other pending claims for this item
            foreach (var other in _claims.Where(c => c.ItemId == claim.ItemId && c.Id != claimId && !c.Rejected && !c.Verified))
            {
                other.Rejected = true;
                other.RejectionReason = "Item claimed by another customer.";
            }

            return claim;
        }

        /// <summary>Reject a claim.</summary>
        public LostItemClaim RejectClaim(int claimId, string reason)
        {
            var claim = _claims.FirstOrDefault(c => c.Id == claimId);
            if (claim == null) throw new KeyNotFoundException($"Claim {claimId} not found.");
            if (claim.Verified) throw new InvalidOperationException("Cannot reject an approved claim.");
            if (claim.Rejected) throw new InvalidOperationException("Claim was already rejected.");

            claim.Rejected = true;
            claim.RejectionReason = reason ?? "Claim rejected.";

            // If no more pending claims, revert item to Found
            var item = GetById(claim.ItemId);
            if (!_claims.Any(c => c.ItemId == claim.ItemId && !c.Rejected && !c.Verified))
                item.Status = LostItemStatus.Found;

            return claim;
        }

        /// <summary>Get all claims for an item.</summary>
        public List<LostItemClaim> GetClaimsForItem(int itemId)
        {
            return _claims.Where(c => c.ItemId == itemId)
                .OrderByDescending(c => c.ClaimDate).ToList();
        }

        /// <summary>Get all claims by a customer.</summary>
        public List<LostItemClaim> GetClaimsByCustomer(int customerId)
        {
            return _claims.Where(c => c.CustomerId == customerId)
                .OrderByDescending(c => c.ClaimDate).ToList();
        }

        // ── Search & Matching ─────────────────────────────────────

        /// <summary>Search items by keyword (description, color, brand, notes).</summary>
        public List<LostItem> Search(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new List<LostItem>();
            var kw = keyword.ToLowerInvariant();
            return _items.Where(item =>
                (item.Description != null && item.Description.ToLowerInvariant().Contains(kw)) ||
                (item.Color != null && item.Color.ToLowerInvariant().Contains(kw)) ||
                (item.Brand != null && item.Brand.ToLowerInvariant().Contains(kw)) ||
                (item.Notes != null && item.Notes.ToLowerInvariant().Contains(kw))
            ).OrderByDescending(x => x.FoundAt).ToList();
        }

        /// <summary>Find potential matches based on category + color + keyword similarity.</summary>
        public List<LostItem> FindMatches(LostItemCategory category, string color = null, string keyword = null)
        {
            var q = _items.Where(x => x.Status == LostItemStatus.Found || x.Status == LostItemStatus.ClaimPending)
                .Where(x => x.Category == category);
            if (!string.IsNullOrWhiteSpace(color))
                q = q.Where(x => x.Color != null &&
                    x.Color.IndexOf(color, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.ToLowerInvariant();
                q = q.Where(x =>
                    (x.Description != null && x.Description.ToLowerInvariant().Contains(kw)) ||
                    (x.Brand != null && x.Brand.ToLowerInvariant().Contains(kw)));
            }
            return q.OrderByDescending(x => x.FoundAt).ToList();
        }

        // ── Disposal ──────────────────────────────────────────────

        /// <summary>Get items that have exceeded their retention period.</summary>
        public List<LostItem> GetOverdueForDisposal(DateTime? asOf = null)
        {
            var now = asOf ?? DateTime.Now;
            return _items.Where(x => x.Status == LostItemStatus.Found &&
                x.FoundAt.AddDays(x.RetentionDays) <= now)
                .OrderBy(x => x.FoundAt).ToList();
        }

        /// <summary>Mark item as disposed.</summary>
        public LostItem DisposeItem(int id, string staffId)
        {
            var item = GetById(id);
            if (item.Status != LostItemStatus.Found)
                throw new InvalidOperationException($"Only items with Found status can be disposed. Current: {item.Status}");
            item.Status = LostItemStatus.Disposed;
            item.DisposalDate = DateTime.Now;
            item.Notes = (item.Notes ?? "") + $" | Disposed by {staffId} on {DateTime.Now:yyyy-MM-dd}";
            return item;
        }

        /// <summary>Mark item as donated.</summary>
        public LostItem DonateItem(int id, string staffId, string donationNotes = null)
        {
            var item = GetById(id);
            if (item.Status != LostItemStatus.Found)
                throw new InvalidOperationException($"Only items with Found status can be donated. Current: {item.Status}");
            item.Status = LostItemStatus.Donated;
            item.DisposalDate = DateTime.Now;
            var note = $"Donated by {staffId} on {DateTime.Now:yyyy-MM-dd}";
            if (!string.IsNullOrWhiteSpace(donationNotes)) note += $": {donationNotes}";
            item.Notes = (item.Notes ?? "") + $" | {note}";
            return item;
        }

        /// <summary>Batch dispose all overdue items.</summary>
        public List<LostItem> BatchDispose(string staffId, DateTime? asOf = null)
        {
            var overdue = GetOverdueForDisposal(asOf);
            foreach (var item in overdue)
            {
                item.Status = LostItemStatus.Disposed;
                item.DisposalDate = DateTime.Now;
                item.Notes = (item.Notes ?? "") + $" | Batch disposed by {staffId} on {DateTime.Now:yyyy-MM-dd}";
            }
            return overdue;
        }

        // ── Reporting ─────────────────────────────────────────────

        /// <summary>Generate a comprehensive lost &amp; found report.</summary>
        public LostAndFoundReport GenerateReport(DateTime? asOf = null)
        {
            var now = asOf ?? DateTime.Now;
            var report = new LostAndFoundReport
            {
                TotalItems = _items.Count,
                Unclaimed = _items.Count(x => x.Status == LostItemStatus.Found || x.Status == LostItemStatus.ClaimPending),
                Claimed = _items.Count(x => x.Status == LostItemStatus.Claimed),
                Disposed = _items.Count(x => x.Status == LostItemStatus.Disposed),
                Donated = _items.Count(x => x.Status == LostItemStatus.Donated),
                PendingClaims = _claims.Count(c => !c.Verified && !c.Rejected),
                OverdueForDisposal = GetOverdueForDisposal(now).Count,
                OverdueItems = GetOverdueForDisposal(now)
            };

            // Claim rate
            var resolved = _items.Count(x => x.Status == LostItemStatus.Claimed ||
                x.Status == LostItemStatus.Disposed || x.Status == LostItemStatus.Donated);
            report.ClaimRate = resolved > 0
                ? (double)_items.Count(x => x.Status == LostItemStatus.Claimed) / resolved
                : 0;

            // Average days to claim
            var claimed = _items.Where(x => x.Status == LostItemStatus.Claimed && x.ClaimedAt.HasValue).ToList();
            report.AverageDaysToClaimOrDefault = claimed.Any()
                ? claimed.Average(x => (x.ClaimedAt.Value - x.FoundAt).TotalDays)
                : 0;

            // By category
            foreach (var g in _items.GroupBy(x => x.Category))
                report.ByCategory[g.Key] = g.Count();

            // Top locations
            foreach (var g in _items.Where(x => x.LocationFound != null)
                .GroupBy(x => x.LocationFound).OrderByDescending(g => g.Count()).Take(5))
                report.TopLocations[g.Key] = g.Count();

            return report;
        }

        /// <summary>Get item count.</summary>
        public int Count => _items.Count;

        /// <summary>Get claims count.</summary>
        public int ClaimsCount => _claims.Count;
    }
}
