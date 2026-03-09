using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class StoreAnnouncementServiceTests
    {
        private StoreAnnouncementService _service;

        [TestInitialize]
        public void Setup()
        {
            _service = new StoreAnnouncementService();
        }

        private Announcement MakeAnnouncement(string title = "Test", string body = "Body",
            AnnouncementCategory category = AnnouncementCategory.General,
            AnnouncementPriority priority = AnnouncementPriority.Normal)
        {
            return new Announcement
            {
                Title = title,
                Body = body,
                Category = category,
                Priority = priority,
                AuthorStaffId = "staff-1"
            };
        }

        // ── Create ────────────────────────────────────────────────

        [TestMethod]
        public void Create_AssignsIdAndDraftStatus()
        {
            var a = _service.Create(MakeAnnouncement());
            Assert.IsTrue(a.Id > 0);
            Assert.AreEqual(AnnouncementStatus.Draft, a.Status);
            Assert.AreEqual(0, a.ViewCount);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Create_NullThrows()
        {
            _service.Create(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Create_EmptyTitleThrows()
        {
            _service.Create(MakeAnnouncement(title: ""));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Create_EmptyBodyThrows()
        {
            _service.Create(MakeAnnouncement(body: ""));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Create_ExpiryBeforeStartThrows()
        {
            var a = MakeAnnouncement();
            a.ScheduledStart = DateTime.Now.AddDays(5);
            a.ExpiresAt = DateTime.Now.AddDays(1);
            _service.Create(a);
        }

        [TestMethod]
        public void Create_SequentialIds()
        {
            var a1 = _service.Create(MakeAnnouncement("A"));
            var a2 = _service.Create(MakeAnnouncement("B"));
            Assert.AreEqual(a1.Id + 1, a2.Id);
        }

        // ── Update ────────────────────────────────────────────────

        [TestMethod]
        public void Update_ModifiesAndSetsEditedAt()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Update(a.Id, x => x.Title = "Updated");
            var fetched = _service.GetById(a.Id);
            Assert.AreEqual("Updated", fetched.Title);
            Assert.IsNotNull(fetched.LastEditedAt);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Update_ArchivedThrows()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Publish(a.Id);
            _service.Archive(a.Id);
            _service.Update(a.Id, x => x.Title = "Nope");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Update_ClearTitleThrows()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Update(a.Id, x => x.Title = "");
        }

        // ── Publish ───────────────────────────────────────────────

        [TestMethod]
        public void Publish_ActivatesImmediately()
        {
            var a = _service.Create(MakeAnnouncement());
            var published = _service.Publish(a.Id);
            Assert.AreEqual(AnnouncementStatus.Active, published.Status);
            Assert.IsNotNull(published.PublishedAt);
        }

        [TestMethod]
        public void Publish_SchedulesForFuture()
        {
            var a = MakeAnnouncement();
            a.ScheduledStart = DateTime.Now.AddDays(1);
            a = _service.Create(a);
            var published = _service.Publish(a.Id);
            Assert.AreEqual(AnnouncementStatus.Scheduled, published.Status);
            Assert.IsNull(published.PublishedAt);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Publish_AlreadyActiveThrows()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Publish(a.Id);
            _service.Publish(a.Id);
        }

        // ── ActivateScheduled ─────────────────────────────────────

        [TestMethod]
        public void ActivateScheduled_ActivatesPastStartTime()
        {
            var a = MakeAnnouncement();
            a.ScheduledStart = DateTime.Now.AddSeconds(-1);
            a = _service.Create(a);
            _service.Publish(a.Id);

            // Force scheduled status for past time
            var activated = _service.ActivateScheduled();
            // If it was already activated by Publish (start in past), it should be active
            var fetched = _service.GetById(a.Id);
            Assert.AreEqual(AnnouncementStatus.Active, fetched.Status);
        }

        // ── ExpireStale ───────────────────────────────────────────

        [TestMethod]
        public void ExpireStale_ExpiresPassedDeadline()
        {
            var a = MakeAnnouncement();
            a.ExpiresAt = DateTime.Now.AddSeconds(-1);
            a = _service.Create(a);
            _service.Publish(a.Id);

            var expired = _service.ExpireStale();
            Assert.AreEqual(1, expired.Count);
            Assert.AreEqual(AnnouncementStatus.Expired, _service.GetById(a.Id).Status);
        }

        [TestMethod]
        public void ExpireStale_IgnoresNonExpired()
        {
            var a = MakeAnnouncement();
            a.ExpiresAt = DateTime.Now.AddDays(1);
            a = _service.Create(a);
            _service.Publish(a.Id);

            var expired = _service.ExpireStale();
            Assert.AreEqual(0, expired.Count);
        }

        // ── Archive ───────────────────────────────────────────────

        [TestMethod]
        public void Archive_SetsArchivedStatus()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Publish(a.Id);
            _service.Archive(a.Id);
            Assert.AreEqual(AnnouncementStatus.Archived, _service.GetById(a.Id).Status);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Archive_DraftThrows()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Archive(a.Id);
        }

        // ── Pinning ──────────────────────────────────────────────

        [TestMethod]
        public void Pin_PinsActiveAnnouncement()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Publish(a.Id);
            _service.Pin(a.Id, "staff-1");
            Assert.IsTrue(_service.IsPinned(a.Id));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Pin_DraftThrows()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Pin(a.Id, "staff-1");
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Pin_AlreadyPinnedThrows()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Publish(a.Id);
            _service.Pin(a.Id, "staff-1");
            _service.Pin(a.Id, "staff-1");
        }

        [TestMethod]
        public void Unpin_RemovesPin()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Publish(a.Id);
            _service.Pin(a.Id, "staff-1");
            _service.Unpin(a.Id);
            Assert.IsFalse(_service.IsPinned(a.Id));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Unpin_NotPinnedThrows()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Publish(a.Id);
            _service.Unpin(a.Id);
        }

        // ── Acknowledgment ───────────────────────────────────────

        [TestMethod]
        public void Acknowledge_RecordsAck()
        {
            var a = MakeAnnouncement();
            a.RequiresAcknowledgment = true;
            a = _service.Create(a);
            _service.Publish(a.Id);

            var ack = _service.Acknowledge(a.Id, 42);
            Assert.IsTrue(ack.Id > 0);
            Assert.IsTrue(_service.HasAcknowledged(a.Id, 42));
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Acknowledge_NotRequiredThrows()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.Publish(a.Id);
            _service.Acknowledge(a.Id, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Acknowledge_DuplicateThrows()
        {
            var a = MakeAnnouncement();
            a.RequiresAcknowledgment = true;
            a = _service.Create(a);
            _service.Publish(a.Id);

            _service.Acknowledge(a.Id, 1);
            _service.Acknowledge(a.Id, 1);
        }

        [TestMethod]
        public void GetAcknowledgments_ReturnsAll()
        {
            var a = MakeAnnouncement();
            a.RequiresAcknowledgment = true;
            a = _service.Create(a);
            _service.Publish(a.Id);

            _service.Acknowledge(a.Id, 1);
            _service.Acknowledge(a.Id, 2);
            _service.Acknowledge(a.Id, 3);

            var acks = _service.GetAcknowledgments(a.Id);
            Assert.AreEqual(3, acks.Count);
        }

        [TestMethod]
        public void GetPendingAcknowledgments_FiltersAcknowledged()
        {
            var a1 = MakeAnnouncement("A1");
            a1.RequiresAcknowledgment = true;
            a1 = _service.Create(a1);
            _service.Publish(a1.Id);

            var a2 = MakeAnnouncement("A2");
            a2.RequiresAcknowledgment = true;
            a2 = _service.Create(a2);
            _service.Publish(a2.Id);

            _service.Acknowledge(a1.Id, 1);

            var pending = _service.GetPendingAcknowledgments(1);
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual("A2", pending[0].Title);
        }

        // ── Views ─────────────────────────────────────────────────

        [TestMethod]
        public void RecordView_IncrementsCount()
        {
            var a = _service.Create(MakeAnnouncement());
            _service.RecordView(a.Id);
            _service.RecordView(a.Id);
            Assert.AreEqual(2, _service.GetById(a.Id).ViewCount);
        }

        // ── Board / Filtering ─────────────────────────────────────

        [TestMethod]
        public void GetBoard_ReturnsOnlyActive()
        {
            _service.Create(MakeAnnouncement("Draft")); // stays draft
            var a = _service.Create(MakeAnnouncement("Active"));
            _service.Publish(a.Id);

            var board = _service.GetBoard();
            Assert.AreEqual(1, board.Count);
            Assert.AreEqual("Active", board[0].Title);
        }

        [TestMethod]
        public void GetBoard_PinnedFirst()
        {
            var a1 = _service.Create(MakeAnnouncement("A"));
            _service.Publish(a1.Id);
            var a2 = _service.Create(MakeAnnouncement("B"));
            _service.Publish(a2.Id);
            _service.Pin(a2.Id, "s");

            var board = _service.GetBoard();
            Assert.AreEqual("B", board[0].Title);
        }

        [TestMethod]
        public void GetBoard_TierTargeting_MatchesCustomerTier()
        {
            var a = MakeAnnouncement("Gold Only");
            a.TargetTiers = new List<string> { "Gold", "Platinum" };
            a = _service.Create(a);
            _service.Publish(a.Id);

            var goldBoard = _service.GetBoard("Gold");
            Assert.AreEqual(1, goldBoard.Count);

            var silverBoard = _service.GetBoard("Silver");
            Assert.AreEqual(0, silverBoard.Count);
        }

        [TestMethod]
        public void GetBoard_UntargetedVisibleToAll()
        {
            var a = _service.Create(MakeAnnouncement("For Everyone"));
            _service.Publish(a.Id);

            Assert.AreEqual(1, _service.GetBoard("Gold").Count);
            Assert.AreEqual(1, _service.GetBoard("Silver").Count);
            Assert.AreEqual(1, _service.GetBoard(null).Count);
        }

        [TestMethod]
        public void GetBoard_FilterByCategory()
        {
            var a1 = _service.Create(MakeAnnouncement("Promo", category: AnnouncementCategory.Promotion));
            _service.Publish(a1.Id);
            var a2 = _service.Create(MakeAnnouncement("Policy", category: AnnouncementCategory.PolicyChange));
            _service.Publish(a2.Id);

            var board = _service.GetBoard(filter: new AnnouncementFilter { Category = AnnouncementCategory.Promotion });
            Assert.AreEqual(1, board.Count);
            Assert.AreEqual("Promo", board[0].Title);
        }

        [TestMethod]
        public void GetBoard_FilterByMinPriority()
        {
            var a1 = _service.Create(MakeAnnouncement("Low", priority: AnnouncementPriority.Low));
            _service.Publish(a1.Id);
            var a2 = _service.Create(MakeAnnouncement("Urgent", priority: AnnouncementPriority.Urgent));
            _service.Publish(a2.Id);

            var board = _service.GetBoard(filter: new AnnouncementFilter { MinPriority = AnnouncementPriority.High });
            Assert.AreEqual(1, board.Count);
            Assert.AreEqual("Urgent", board[0].Title);
        }

        [TestMethod]
        public void GetBoard_FilterByTag()
        {
            var a = MakeAnnouncement("Tagged");
            a.Tags = new List<string> { "sale", "weekend" };
            a = _service.Create(a);
            _service.Publish(a.Id);

            var a2 = _service.Create(MakeAnnouncement("Untagged"));
            _service.Publish(a2.Id);

            var board = _service.GetBoard(filter: new AnnouncementFilter { Tag = "sale" });
            Assert.AreEqual(1, board.Count);
        }

        [TestMethod]
        public void GetBoard_SearchText()
        {
            var a = _service.Create(MakeAnnouncement("Holiday Sale", "50% off all rentals"));
            _service.Publish(a.Id);
            var a2 = _service.Create(MakeAnnouncement("Normal", "Nothing special"));
            _service.Publish(a2.Id);

            var board = _service.GetBoard(filter: new AnnouncementFilter { SearchText = "holiday" });
            Assert.AreEqual(1, board.Count);
        }

        // ── GetAll ────────────────────────────────────────────────

        [TestMethod]
        public void GetAll_ReturnsEverything()
        {
            _service.Create(MakeAnnouncement("A"));
            _service.Create(MakeAnnouncement("B"));
            Assert.AreEqual(2, _service.GetAll().Count);
        }

        [TestMethod]
        public void GetAll_FiltersByStatus()
        {
            var a = _service.Create(MakeAnnouncement("A"));
            _service.Publish(a.Id);
            _service.Create(MakeAnnouncement("B")); // draft

            Assert.AreEqual(1, _service.GetAll(AnnouncementStatus.Active).Count);
            Assert.AreEqual(1, _service.GetAll(AnnouncementStatus.Draft).Count);
        }

        // ── Analytics ─────────────────────────────────────────────

        [TestMethod]
        public void GetAnalytics_CountsCorrectly()
        {
            var a1 = _service.Create(MakeAnnouncement("A", category: AnnouncementCategory.Promotion));
            _service.Publish(a1.Id);
            _service.Create(MakeAnnouncement("B")); // draft

            var analytics = _service.GetAnalytics();
            Assert.AreEqual(2, analytics.TotalAnnouncements);
            Assert.AreEqual(1, analytics.ActiveCount);
            Assert.AreEqual(1, analytics.DraftCount);
        }

        [TestMethod]
        public void GetAnalytics_CategoryBreakdown()
        {
            _service.Create(MakeAnnouncement("P1", category: AnnouncementCategory.Promotion));
            _service.Create(MakeAnnouncement("P2", category: AnnouncementCategory.Promotion));
            _service.Create(MakeAnnouncement("G1", category: AnnouncementCategory.General));

            var analytics = _service.GetAnalytics();
            Assert.AreEqual(2, analytics.ByCategory[AnnouncementCategory.Promotion]);
            Assert.AreEqual(1, analytics.ByCategory[AnnouncementCategory.General]);
        }

        [TestMethod]
        public void GetAnalytics_MostViewed()
        {
            var a1 = _service.Create(MakeAnnouncement("A"));
            var a2 = _service.Create(MakeAnnouncement("B"));
            _service.RecordView(a2.Id);
            _service.RecordView(a2.Id);

            var analytics = _service.GetAnalytics();
            Assert.AreEqual("B", analytics.MostViewedAnnouncement.Title);
        }

        [TestMethod]
        public void GetAcknowledgmentRate_Calculates()
        {
            var a = MakeAnnouncement();
            a.RequiresAcknowledgment = true;
            a = _service.Create(a);
            _service.Publish(a.Id);

            _service.Acknowledge(a.Id, 1);
            _service.Acknowledge(a.Id, 2);

            var rate = _service.GetAcknowledgmentRate(a.Id, 10);
            Assert.AreEqual(20.0, rate, 0.01);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetAcknowledgmentRate_ZeroCustomersThrows()
        {
            var a = MakeAnnouncement();
            a.RequiresAcknowledgment = true;
            a = _service.Create(a);
            _service.GetAcknowledgmentRate(a.Id, 0);
        }

        // ── Search ────────────────────────────────────────────────

        [TestMethod]
        public void Search_FindsByTitle()
        {
            _service.Create(MakeAnnouncement("Weekend Sale", "Details"));
            _service.Create(MakeAnnouncement("Normal", "Nothing"));

            var results = _service.Search("weekend");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Search_FindsByBody()
        {
            _service.Create(MakeAnnouncement("Title", "Free popcorn Friday"));
            var results = _service.Search("popcorn");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Search_FindsByTag()
        {
            var a = MakeAnnouncement("A", "B");
            a.Tags = new List<string> { "clearance" };
            _service.Create(a);

            var results = _service.Search("clearance");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Search_EmptyQueryThrows()
        {
            _service.Search("");
        }

        // ── Duplicate ─────────────────────────────────────────────

        [TestMethod]
        public void Duplicate_CreatesNewDraft()
        {
            var original = _service.Create(MakeAnnouncement("Original", "Body"));
            _service.Publish(original.Id);

            var copy = _service.Duplicate(original.Id);
            Assert.AreNotEqual(original.Id, copy.Id);
            Assert.AreEqual("Original (Copy)", copy.Title);
            Assert.AreEqual(AnnouncementStatus.Draft, copy.Status);
        }

        [TestMethod]
        public void Duplicate_PreservesMetadata()
        {
            var a = MakeAnnouncement("A", "B");
            a.Category = AnnouncementCategory.Promotion;
            a.Tags = new List<string> { "sale" };
            a.RequiresAcknowledgment = true;
            a = _service.Create(a);

            var copy = _service.Duplicate(a.Id);
            Assert.AreEqual(AnnouncementCategory.Promotion, copy.Category);
            Assert.IsTrue(copy.Tags.Contains("sale"));
            Assert.IsTrue(copy.RequiresAcknowledgment);
        }

        // ── GetById ───────────────────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(KeyNotFoundException))]
        public void GetById_NotFoundThrows()
        {
            _service.GetById(999);
        }

        // ── Edge Cases ────────────────────────────────────────────

        [TestMethod]
        public void Board_SortsByPriorityDescending()
        {
            var low = _service.Create(MakeAnnouncement("Low", priority: AnnouncementPriority.Low));
            _service.Publish(low.Id);
            var urgent = _service.Create(MakeAnnouncement("Urgent", priority: AnnouncementPriority.Urgent));
            _service.Publish(urgent.Id);
            var normal = _service.Create(MakeAnnouncement("Normal", priority: AnnouncementPriority.Normal));
            _service.Publish(normal.Id);

            var board = _service.GetBoard();
            Assert.AreEqual("Urgent", board[0].Title);
            Assert.AreEqual("Normal", board[1].Title);
            Assert.AreEqual("Low", board[2].Title);
        }

        [TestMethod]
        public void NoTierTarget_VisibleWhenNoTierProvided()
        {
            var a = _service.Create(MakeAnnouncement("Open"));
            _service.Publish(a.Id);

            var board = _service.GetBoard(null);
            Assert.AreEqual(1, board.Count);
        }

        [TestMethod]
        public void Analytics_EmptyService_ReturnsZeros()
        {
            var analytics = _service.GetAnalytics();
            Assert.AreEqual(0, analytics.TotalAnnouncements);
            Assert.AreEqual(0, analytics.ActiveCount);
            Assert.AreEqual(0, analytics.PinnedCount);
        }
    }
}
