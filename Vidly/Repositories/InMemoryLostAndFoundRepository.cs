using System;
using System.Collections.Generic;
using System.Linq;
using Vidly.Models;

namespace Vidly.Repositories
{
    public class InMemoryLostAndFoundRepository : ILostAndFoundRepository
    {
        private static readonly List<LostItem> _items = new List<LostItem>();
        private static readonly List<LostItemClaim> _claims = new List<LostItemClaim>();
        private static int _nextItemId = 1;
        private static int _nextClaimId = 1;
        private static bool _seeded;

        public InMemoryLostAndFoundRepository()
        {
            if (!_seeded)
            {
                _seeded = true;
                SeedData();
            }
        }

        private void SeedData()
        {
            var items = new[]
            {
                new LostItem { Description = "Black leather wallet with initials 'JM'", Category = LostItemCategory.Wallet, LocationFound = "Counter Area", Color = "Black", Brand = "Coach", StorageBin = "BIN-A1", Notes = "Contains library card", FoundByStaffId = "S001", FoundAt = DateTime.Now.AddDays(-5) },
                new LostItem { Description = "Blue umbrella, compact folding", Category = LostItemCategory.Umbrella, LocationFound = "Entrance", Color = "Blue", StorageBin = "BIN-B2", FoundByStaffId = "S002", FoundAt = DateTime.Now.AddDays(-12) },
                new LostItem { Description = "iPhone 15 in clear case", Category = LostItemCategory.Electronics, LocationFound = "Aisle 3", Color = "Silver", Brand = "Apple", StorageBin = "BIN-C1", Notes = "Lock screen shows dog photo", FoundByStaffId = "S001", FoundAt = DateTime.Now.AddDays(-2) },
                new LostItem { Description = "Child's stuffed rabbit, well-worn", Category = LostItemCategory.Toy, LocationFound = "Kids Section", Color = "Pink", StorageBin = "BIN-A3", FoundByStaffId = "S003", FoundAt = DateTime.Now.AddDays(-1) },
                new LostItem { Description = "Prescription sunglasses, tortoiseshell", Category = LostItemCategory.Glasses, LocationFound = "Restroom", Color = "Brown", Brand = "Ray-Ban", StorageBin = "BIN-B1", FoundByStaffId = "S002", FoundAt = DateTime.Now.AddDays(-35), RetentionDays = 30 },
                new LostItem { Description = "Red wool scarf", Category = LostItemCategory.Clothing, LocationFound = "Seating Area", Color = "Red", StorageBin = "BIN-A2", FoundByStaffId = "S001", FoundAt = DateTime.Now.AddDays(-20) },
                new LostItem { Description = "Car key fob, Toyota", Category = LostItemCategory.Keys, LocationFound = "Counter Area", Color = "Black", Brand = "Toyota", StorageBin = "BIN-C2", FoundByStaffId = "S003", FoundAt = DateTime.Now.AddDays(-8) },
            };

            foreach (var item in items)
                Add(item);

            // Mark one as claimed
            _items[0].Status = LostItemStatus.Claimed;
            _items[0].ClaimedAt = DateTime.Now.AddDays(-3);
            _items[0].ClaimedByCustomerId = 1;
        }

        public IEnumerable<LostItem> GetAll() => _items.ToList();

        public IEnumerable<LostItem> GetByStatus(LostItemStatus status) =>
            _items.Where(i => i.Status == status).ToList();

        public IEnumerable<LostItem> GetByCategory(LostItemCategory category) =>
            _items.Where(i => i.Category == category).ToList();

        public LostItem GetById(int id) => _items.FirstOrDefault(i => i.Id == id);

        public void Add(LostItem item)
        {
            item.Id = _nextItemId++;
            if (item.FoundAt == default) item.FoundAt = DateTime.Now;
            if (item.Status == default) item.Status = LostItemStatus.Found;
            _items.Add(item);
        }

        public void Update(LostItem item)
        {
            var idx = _items.FindIndex(i => i.Id == item.Id);
            if (idx >= 0) _items[idx] = item;
        }

        public void Remove(int id) => _items.RemoveAll(i => i.Id == id);

        public IEnumerable<LostItemClaim> GetClaimsForItem(int itemId) =>
            _claims.Where(c => c.ItemId == itemId).ToList();

        public LostItemClaim GetClaimById(int id) => _claims.FirstOrDefault(c => c.Id == id);

        public void AddClaim(LostItemClaim claim)
        {
            claim.Id = _nextClaimId++;
            if (claim.ClaimDate == default) claim.ClaimDate = DateTime.Now;
            _claims.Add(claim);
        }

        public void UpdateClaim(LostItemClaim claim)
        {
            var idx = _claims.FindIndex(c => c.Id == claim.Id);
            if (idx >= 0) _claims[idx] = claim;
        }

        public LostAndFoundReport GetReport()
        {
            var report = new LostAndFoundReport
            {
                TotalItems = _items.Count,
                Unclaimed = _items.Count(i => i.Status == LostItemStatus.Found),
                Claimed = _items.Count(i => i.Status == LostItemStatus.Claimed),
                Disposed = _items.Count(i => i.Status == LostItemStatus.Disposed),
                Donated = _items.Count(i => i.Status == LostItemStatus.Donated),
                PendingClaims = _claims.Count(c => !c.Verified && !c.Rejected),
                OverdueForDisposal = GetOverdueItems().Count(),
            };

            var claimedItems = _items.Where(i => i.ClaimedAt.HasValue).ToList();
            report.AverageDaysToClaimOrDefault = claimedItems.Any()
                ? claimedItems.Average(i => (i.ClaimedAt.Value - i.FoundAt).TotalDays)
                : 0;
            report.ClaimRate = _items.Any()
                ? (double)report.Claimed / _items.Count
                : 0;

            foreach (var cat in Enum.GetValues(typeof(LostItemCategory)).Cast<LostItemCategory>())
            {
                var count = _items.Count(i => i.Category == cat);
                if (count > 0) report.ByCategory[cat] = count;
            }

            foreach (var grp in _items.GroupBy(i => i.LocationFound).OrderByDescending(g => g.Count()).Take(5))
                report.TopLocations[grp.Key] = grp.Count();

            report.OverdueItems = GetOverdueItems().ToList();
            return report;
        }

        public IEnumerable<LostItem> GetOverdueItems() =>
            _items.Where(i => i.Status == LostItemStatus.Found &&
                              (DateTime.Now - i.FoundAt).TotalDays > i.RetentionDays).ToList();

        public IEnumerable<LostItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return GetAll();
            var q = query.ToLowerInvariant();
            return _items.Where(i =>
                (i.Description != null && i.Description.ToLowerInvariant().Contains(q)) ||
                (i.Color != null && i.Color.ToLowerInvariant().Contains(q)) ||
                (i.Brand != null && i.Brand.ToLowerInvariant().Contains(q)) ||
                (i.LocationFound != null && i.LocationFound.ToLowerInvariant().Contains(q)) ||
                (i.Notes != null && i.Notes.ToLowerInvariant().Contains(q))
            ).ToList();
        }
    }
}
