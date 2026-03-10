using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class LostAndFoundServiceTests
    {
        private LostAndFoundService _svc;

        [TestInitialize]
        public void Setup()
        {
            _svc = new LostAndFoundService();
        }

        private LostItem MakeItem(string desc = "Blue umbrella", LostItemCategory cat = LostItemCategory.Umbrella,
            string location = "Lobby", string staff = "S1", string color = "Blue", string brand = null)
        {
            return new LostItem
            {
                Description = desc,
                Category = cat,
                LocationFound = location,
                FoundByStaffId = staff,
                Color = color,
                Brand = brand
            };
        }

        // ── Registration ──────────────────────────────────────────

        [TestMethod]
        public void RegisterItem_AssignsIdAndStatus()
        {
            var item = _svc.RegisterItem(MakeItem());
            Assert.AreEqual(1, item.Id);
            Assert.AreEqual(LostItemStatus.Found, item.Status);
            Assert.AreEqual(30, item.RetentionDays);
            Assert.AreEqual(1, _svc.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RegisterItem_RequiresDescription()
        {
            _svc.RegisterItem(MakeItem(desc: ""));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RegisterItem_RequiresLocation()
        {
            _svc.RegisterItem(MakeItem(location: ""));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RegisterItem_RequiresStaff()
        {
            _svc.RegisterItem(MakeItem(staff: ""));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void RegisterItem_RejectsNull()
        {
            _svc.RegisterItem(null);
        }

        [TestMethod]
        public void RegisterItem_SequentialIds()
        {
            var a = _svc.RegisterItem(MakeItem());
            var b = _svc.RegisterItem(MakeItem(desc: "Hat"));
            Assert.AreEqual(1, a.Id);
            Assert.AreEqual(2, b.Id);
        }

        // ── GetById ───────────────────────────────────────────────

        [TestMethod]
        public void GetById_ReturnsCorrectItem()
        {
            var item = _svc.RegisterItem(MakeItem());
            Assert.AreEqual(item.Id, _svc.GetById(item.Id).Id);
        }

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void GetById_ThrowsForMissing()
        {
            _svc.GetById(999);
        }

        // ── ListItems ─────────────────────────────────────────────

        [TestMethod]
        public void ListItems_FiltersByStatus()
        {
            _svc.RegisterItem(MakeItem());
            _svc.RegisterItem(MakeItem(desc: "Keys"));
            var item3 = _svc.RegisterItem(MakeItem(desc: "Wallet"));
            _svc.SubmitClaim(item3.Id, 1, "My black wallet");
            _svc.ApproveClaim(1, "S2");

            var found = _svc.ListItems(status: LostItemStatus.Found);
            Assert.AreEqual(2, found.Count);
            var claimed = _svc.ListItems(status: LostItemStatus.Claimed);
            Assert.AreEqual(1, claimed.Count);
        }

        [TestMethod]
        public void ListItems_FiltersByCategory()
        {
            _svc.RegisterItem(MakeItem(cat: LostItemCategory.Electronics));
            _svc.RegisterItem(MakeItem(cat: LostItemCategory.Umbrella));
            var result = _svc.ListItems(category: LostItemCategory.Electronics);
            Assert.AreEqual(1, result.Count);
        }

        [TestMethod]
        public void ListItems_FiltersByLocation()
        {
            _svc.RegisterItem(MakeItem(location: "Theater 1"));
            _svc.RegisterItem(MakeItem(location: "Lobby"));
            var result = _svc.ListItems(locationContains: "theater");
            Assert.AreEqual(1, result.Count);
        }

        // ── UpdateItem ────────────────────────────────────────────

        [TestMethod]
        public void UpdateItem_ModifiesDetails()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.UpdateItem(item.Id, i => i.StorageBin = "Bin-A3");
            Assert.AreEqual("Bin-A3", _svc.GetById(item.Id).StorageBin);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void UpdateItem_BlocksClaimedItems()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 1, "Mine");
            _svc.ApproveClaim(1, "S1");
            _svc.UpdateItem(item.Id, i => i.Notes = "test");
        }

        // ── Claiming ──────────────────────────────────────────────

        [TestMethod]
        public void SubmitClaim_CreatesClaimAndUpdateStatus()
        {
            var item = _svc.RegisterItem(MakeItem());
            var claim = _svc.SubmitClaim(item.Id, 10, "It's my blue umbrella");
            Assert.AreEqual(1, claim.Id);
            Assert.AreEqual(LostItemStatus.ClaimPending, _svc.GetById(item.Id).Status);
            Assert.IsFalse(claim.Verified);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SubmitClaim_RequiresDescription()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 10, "");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitClaim_BlocksDuplicateByCustomer()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 10, "Mine");
            _svc.SubmitClaim(item.Id, 10, "Really mine");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitClaim_BlocksClaimedItems()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 1, "Mine");
            _svc.ApproveClaim(1, "S1");
            _svc.SubmitClaim(item.Id, 2, "No, mine");
        }

        [TestMethod]
        public void ApproveClaim_MarksClaimedAndRejectsOthers()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 1, "My umbrella");
            _svc.SubmitClaim(item.Id, 2, "My umbrella too");
            _svc.ApproveClaim(1, "S1");

            Assert.AreEqual(LostItemStatus.Claimed, _svc.GetById(item.Id).Status);
            Assert.AreEqual(1, _svc.GetById(item.Id).ClaimedByCustomerId);

            var claims = _svc.GetClaimsForItem(item.Id);
            Assert.IsTrue(claims.First(c => c.Id == 1).Verified);
            Assert.IsTrue(claims.First(c => c.Id == 2).Rejected);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void ApproveClaim_BlocksAlreadyApproved()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 1, "Mine");
            _svc.ApproveClaim(1, "S1");
            _svc.ApproveClaim(1, "S2");
        }

        [TestMethod]
        public void RejectClaim_RevertsToFound()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 1, "Mine");
            _svc.RejectClaim(1, "Description doesn't match");

            Assert.AreEqual(LostItemStatus.Found, _svc.GetById(item.Id).Status);
            Assert.IsTrue(_svc.GetClaimsForItem(item.Id).First().Rejected);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void RejectClaim_BlocksApprovedClaim()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 1, "Mine");
            _svc.ApproveClaim(1, "S1");
            _svc.RejectClaim(1, "Oops");
        }

        [TestMethod]
        public void GetClaimsByCustomer_ReturnsAll()
        {
            var a = _svc.RegisterItem(MakeItem());
            var b = _svc.RegisterItem(MakeItem(desc: "Hat"));
            _svc.SubmitClaim(a.Id, 5, "My umbrella");
            _svc.SubmitClaim(b.Id, 5, "My hat");
            Assert.AreEqual(2, _svc.GetClaimsByCustomer(5).Count);
        }

        // ── Search ────────────────────────────────────────────────

        [TestMethod]
        public void Search_FindsByDescription()
        {
            _svc.RegisterItem(MakeItem(desc: "Red scarf"));
            _svc.RegisterItem(MakeItem(desc: "Blue hat"));
            Assert.AreEqual(1, _svc.Search("scarf").Count);
        }

        [TestMethod]
        public void Search_FindsByColor()
        {
            _svc.RegisterItem(MakeItem(color: "Navy Blue"));
            Assert.AreEqual(1, _svc.Search("navy").Count);
        }

        [TestMethod]
        public void Search_FindsByBrand()
        {
            _svc.RegisterItem(MakeItem(brand: "Samsung"));
            Assert.AreEqual(1, _svc.Search("samsung").Count);
        }

        [TestMethod]
        public void Search_EmptyKeywordReturnsEmpty()
        {
            _svc.RegisterItem(MakeItem());
            Assert.AreEqual(0, _svc.Search("").Count);
        }

        // ── FindMatches ───────────────────────────────────────────

        [TestMethod]
        public void FindMatches_ByCategory()
        {
            _svc.RegisterItem(MakeItem(cat: LostItemCategory.Electronics));
            _svc.RegisterItem(MakeItem(cat: LostItemCategory.Umbrella));
            var matches = _svc.FindMatches(LostItemCategory.Electronics);
            Assert.AreEqual(1, matches.Count);
        }

        [TestMethod]
        public void FindMatches_ByColorAndKeyword()
        {
            _svc.RegisterItem(MakeItem(desc: "iPhone 15", cat: LostItemCategory.Electronics, color: "Black", brand: "Apple"));
            _svc.RegisterItem(MakeItem(desc: "Galaxy S24", cat: LostItemCategory.Electronics, color: "White", brand: "Samsung"));
            var matches = _svc.FindMatches(LostItemCategory.Electronics, color: "Black");
            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual("iPhone 15", matches[0].Description);
        }

        [TestMethod]
        public void FindMatches_ExcludesClaimedAndDisposed()
        {
            var item = _svc.RegisterItem(MakeItem(cat: LostItemCategory.Keys));
            _svc.SubmitClaim(item.Id, 1, "My keys");
            _svc.ApproveClaim(1, "S1");
            Assert.AreEqual(0, _svc.FindMatches(LostItemCategory.Keys).Count);
        }

        // ── Disposal ──────────────────────────────────────────────

        [TestMethod]
        public void GetOverdueForDisposal_FindsExpiredItems()
        {
            var item = _svc.RegisterItem(MakeItem());
            item.FoundAt = DateTime.Now.AddDays(-31);
            item.RetentionDays = 30;
            Assert.AreEqual(1, _svc.GetOverdueForDisposal().Count);
        }

        [TestMethod]
        public void DisposeItem_ChangesStatus()
        {
            var item = _svc.RegisterItem(MakeItem());
            var disposed = _svc.DisposeItem(item.Id, "S1");
            Assert.AreEqual(LostItemStatus.Disposed, disposed.Status);
            Assert.IsNotNull(disposed.DisposalDate);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void DisposeItem_BlocksNonFoundItems()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 1, "Mine");
            _svc.ApproveClaim(1, "S1");
            _svc.DisposeItem(item.Id, "S2");
        }

        [TestMethod]
        public void DonateItem_ChangesStatus()
        {
            var item = _svc.RegisterItem(MakeItem());
            var donated = _svc.DonateItem(item.Id, "S1", "Local shelter");
            Assert.AreEqual(LostItemStatus.Donated, donated.Status);
            Assert.IsTrue(donated.Notes.Contains("Local shelter"));
        }

        [TestMethod]
        public void BatchDispose_DisposesAllOverdue()
        {
            var a = _svc.RegisterItem(MakeItem());
            a.FoundAt = DateTime.Now.AddDays(-40);
            var b = _svc.RegisterItem(MakeItem(desc: "Hat"));
            b.FoundAt = DateTime.Now.AddDays(-35);
            var c = _svc.RegisterItem(MakeItem(desc: "Keys"));
            // c is recent, should not be disposed

            var disposed = _svc.BatchDispose("S1");
            Assert.AreEqual(2, disposed.Count);
            Assert.AreEqual(LostItemStatus.Found, _svc.GetById(c.Id).Status);
        }

        // ── Reporting ─────────────────────────────────────────────

        [TestMethod]
        public void GenerateReport_CorrectCounts()
        {
            _svc.RegisterItem(MakeItem());
            var b = _svc.RegisterItem(MakeItem(desc: "Wallet", cat: LostItemCategory.Wallet));
            _svc.SubmitClaim(b.Id, 1, "My wallet");
            _svc.ApproveClaim(1, "S1");
            var c = _svc.RegisterItem(MakeItem(desc: "Old hat"));
            _svc.DisposeItem(c.Id, "S1");

            var report = _svc.GenerateReport();
            Assert.AreEqual(3, report.TotalItems);
            Assert.AreEqual(1, report.Unclaimed);
            Assert.AreEqual(1, report.Claimed);
            Assert.AreEqual(1, report.Disposed);
        }

        [TestMethod]
        public void GenerateReport_ClaimRate()
        {
            var a = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(a.Id, 1, "Mine");
            _svc.ApproveClaim(1, "S1");
            var b = _svc.RegisterItem(MakeItem(desc: "Hat"));
            _svc.DisposeItem(b.Id, "S1");

            var report = _svc.GenerateReport();
            Assert.AreEqual(0.5, report.ClaimRate, 0.01);
        }

        [TestMethod]
        public void GenerateReport_CategoryBreakdown()
        {
            _svc.RegisterItem(MakeItem(cat: LostItemCategory.Electronics));
            _svc.RegisterItem(MakeItem(cat: LostItemCategory.Electronics));
            _svc.RegisterItem(MakeItem(cat: LostItemCategory.Umbrella));

            var report = _svc.GenerateReport();
            Assert.AreEqual(2, report.ByCategory[LostItemCategory.Electronics]);
            Assert.AreEqual(1, report.ByCategory[LostItemCategory.Umbrella]);
        }

        [TestMethod]
        public void GenerateReport_TopLocations()
        {
            _svc.RegisterItem(MakeItem(location: "Lobby"));
            _svc.RegisterItem(MakeItem(location: "Lobby"));
            _svc.RegisterItem(MakeItem(location: "Theater 1"));

            var report = _svc.GenerateReport();
            Assert.AreEqual(2, report.TopLocations["Lobby"]);
            Assert.AreEqual(1, report.TopLocations["Theater 1"]);
        }

        [TestMethod]
        public void GenerateReport_EmptyStore()
        {
            var report = _svc.GenerateReport();
            Assert.AreEqual(0, report.TotalItems);
            Assert.AreEqual(0, report.ClaimRate);
        }

        // ── Edge Cases ────────────────────────────────────────────

        [TestMethod]
        public void RejectClaim_AllowsNewClaimBySameCustomer()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 1, "First try");
            _svc.RejectClaim(1, "Wrong");
            // Should be able to submit again after rejection
            var claim2 = _svc.SubmitClaim(item.Id, 1, "Better description");
            Assert.AreEqual(2, claim2.Id);
        }

        [TestMethod]
        public void MultipleClaimsPendingRevertsOnLastReject()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.SubmitClaim(item.Id, 1, "Mine");
            _svc.SubmitClaim(item.Id, 2, "Also mine");
            _svc.RejectClaim(1, "No");
            // Still one pending claim
            Assert.AreEqual(LostItemStatus.ClaimPending, _svc.GetById(item.Id).Status);
            _svc.RejectClaim(2, "No");
            Assert.AreEqual(LostItemStatus.Found, _svc.GetById(item.Id).Status);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void SubmitClaim_DisposedItem()
        {
            var item = _svc.RegisterItem(MakeItem());
            _svc.DisposeItem(item.Id, "S1");
            _svc.SubmitClaim(item.Id, 1, "Mine");
        }

        [TestMethod]
        public void CustomRetentionDays()
        {
            var item = _svc.RegisterItem(MakeItem());
            item.RetentionDays = 7;
            item.FoundAt = DateTime.Now.AddDays(-8);
            Assert.AreEqual(1, _svc.GetOverdueForDisposal().Count);
        }
    }
}
