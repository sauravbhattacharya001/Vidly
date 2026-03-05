using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class WaitlistServiceTests
    {
        private InMemoryMovieRepository _movieRepo;
        private InMemoryCustomerRepository _customerRepo;
        private InMemoryRentalRepository _rentalRepo;
        private WaitlistService _service;

        [TestInitialize]
        public void Setup()
        {
            _movieRepo = new InMemoryMovieRepository();
            _customerRepo = new InMemoryCustomerRepository();
            _rentalRepo = new InMemoryRentalRepository();
            _service = new WaitlistService(_movieRepo, _customerRepo, _rentalRepo);
        }

        private Customer AddCustomer(string name)
        {
            var c = new Customer { Name = name, Email = $"{name.ToLower()}@test.com" };
            _customerRepo.Add(c); // assigns c.Id
            return c;
        }

        private Movie AddMovie(string name, Genre genre = Genre.Action)
        {
            var m = new Movie { Name = name, Genre = genre, ReleaseDate = DateTime.Today.AddMonths(-6) };
            _movieRepo.Add(m); // assigns m.Id
            return m;
        }

        // --- Join Waitlist ---

        [TestMethod]
        public void JoinWaitlist_ValidInput_CreatesEntry()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            var entry = _service.JoinWaitlist(c.Id, m.Id);

            Assert.AreEqual(c.Id, entry.CustomerId);
            Assert.AreEqual(m.Id, entry.MovieId);
            Assert.AreEqual("Alice", entry.CustomerName);
            Assert.AreEqual("Inception", entry.MovieName);
            Assert.AreEqual(WaitlistStatus.Waiting, entry.Status);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void JoinWaitlist_InvalidCustomer_Throws()
        {
            var m = AddMovie("Inception");
            _service.JoinWaitlist(999, m.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void JoinWaitlist_InvalidMovie_Throws()
        {
            var c = AddCustomer("Alice");
            _service.JoinWaitlist(c.Id, 999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void JoinWaitlist_Duplicate_Throws()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c.Id, m.Id);
            _service.JoinWaitlist(c.Id, m.Id);
        }

        [TestMethod]
        public void JoinWaitlist_WithNote_SavesNote()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            var entry = _service.JoinWaitlist(c.Id, m.Id, note: "Birthday gift");

            Assert.AreEqual("Birthday gift", entry.Note);
        }

        [TestMethod]
        public void JoinWaitlist_WithNotificationPreference_Saves()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            var entry = _service.JoinWaitlist(c.Id, m.Id, notification: NotificationMethod.Both);

            Assert.AreEqual(NotificationMethod.Both, entry.PreferredNotification);
        }

        [TestMethod]
        public void JoinWaitlist_CustomPickupWindow_Saved()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            var entry = _service.JoinWaitlist(c.Id, m.Id, pickupWindowHours: 24);

            Assert.AreEqual(24, entry.PickupWindowHours);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void JoinWaitlist_ExceedsMaxEntries_Throws()
        {
            var c = AddCustomer("Alice");
            _service.MaxEntriesPerCustomer = 2;

            var m1 = AddMovie("Movie1");
            var m2 = AddMovie("Movie2");
            var m3 = AddMovie("Movie3");

            _service.JoinWaitlist(c.Id, m1.Id);
            _service.JoinWaitlist(c.Id, m2.Id);
            _service.JoinWaitlist(c.Id, m3.Id); // Should throw
        }

        // --- Leave Waitlist ---

        [TestMethod]
        public void LeaveWaitlist_ValidEntry_CancelsIt()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c.Id, m.Id);
            var cancelled = _service.LeaveWaitlist(c.Id, m.Id);

            Assert.AreEqual(WaitlistStatus.Cancelled, cancelled.Status);
            Assert.IsNotNull(cancelled.CancelledDate);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void LeaveWaitlist_NotOnList_Throws()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.LeaveWaitlist(c.Id, m.Id);
        }

        [TestMethod]
        public void LeaveWaitlist_CanRejoinAfterCancel()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c.Id, m.Id);
            _service.LeaveWaitlist(c.Id, m.Id);
            var entry = _service.JoinWaitlist(c.Id, m.Id);

            Assert.AreEqual(WaitlistStatus.Waiting, entry.Status);
        }

        // --- Position ---

        [TestMethod]
        public void GetPosition_FirstInLine_Returns1()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c.Id, m.Id);

            Assert.AreEqual(1, _service.GetPosition(c.Id, m.Id));
        }

        [TestMethod]
        public void GetPosition_ThirdInLine_Returns3()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Charlie");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);
            _service.JoinWaitlist(c3.Id, m.Id);

            Assert.AreEqual(3, _service.GetPosition(c3.Id, m.Id));
        }

        [TestMethod]
        public void GetPosition_NotOnList_Returns0()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");

            Assert.AreEqual(0, _service.GetPosition(c.Id, m.Id));
        }

        [TestMethod]
        public void GetPosition_AfterCancellation_MovesUp()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Charlie");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);
            _service.JoinWaitlist(c3.Id, m.Id);

            _service.LeaveWaitlist(c1.Id, m.Id);

            Assert.AreEqual(1, _service.GetPosition(c2.Id, m.Id));
            Assert.AreEqual(2, _service.GetPosition(c3.Id, m.Id));
        }

        // --- GetWaitlistForMovie ---

        [TestMethod]
        public void GetWaitlistForMovie_ReturnsInFIFOOrder()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);

            var list = _service.GetWaitlistForMovie(m.Id);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("Alice", list[0].CustomerName);
            Assert.AreEqual("Bob", list[1].CustomerName);
        }

        [TestMethod]
        public void GetWaitlistForMovie_ExcludesCancelled()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);
            _service.LeaveWaitlist(c1.Id, m.Id);

            var list = _service.GetWaitlistForMovie(m.Id);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("Bob", list[0].CustomerName);
        }

        // --- Customer Waitlist ---

        [TestMethod]
        public void GetCustomerWaitlist_ReturnsAllStatuses()
        {
            var c = AddCustomer("Alice");
            var m1 = AddMovie("Movie1");
            var m2 = AddMovie("Movie2");
            _service.JoinWaitlist(c.Id, m1.Id);
            _service.JoinWaitlist(c.Id, m2.Id);
            _service.LeaveWaitlist(c.Id, m1.Id);

            var list = _service.GetCustomerWaitlist(c.Id);
            Assert.AreEqual(2, list.Count);
        }

        [TestMethod]
        public void GetActiveCustomerWaitlist_OnlyWaitingAndNotified()
        {
            var c = AddCustomer("Alice");
            var m1 = AddMovie("Movie1");
            var m2 = AddMovie("Movie2");
            _service.JoinWaitlist(c.Id, m1.Id);
            _service.JoinWaitlist(c.Id, m2.Id);
            _service.LeaveWaitlist(c.Id, m1.Id);

            var list = _service.GetActiveCustomerWaitlist(c.Id);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("Movie2", list[0].MovieName);
        }

        // --- NotifyNext ---

        [TestMethod]
        public void NotifyNext_NotifiesFirstInLine()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);

            var notified = _service.NotifyNext(m.Id);
            Assert.AreEqual("Alice", notified.CustomerName);
            Assert.AreEqual(WaitlistStatus.Notified, notified.Status);
            Assert.IsNotNull(notified.NotifiedDate);
        }

        [TestMethod]
        public void NotifyNext_NoOneWaiting_ReturnsNull()
        {
            var m = AddMovie("Inception");
            Assert.IsNull(_service.NotifyNext(m.Id));
        }

        [TestMethod]
        public void NotifyNext_SkipsCancelledEntries()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);
            _service.LeaveWaitlist(c1.Id, m.Id);

            var notified = _service.NotifyNext(m.Id);
            Assert.AreEqual("Bob", notified.CustomerName);
        }

        // --- MarkFulfilled ---

        [TestMethod]
        public void MarkFulfilled_SetsStatusAndDate()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c.Id, m.Id);
            _service.NotifyNext(m.Id);

            var fulfilled = _service.MarkFulfilled(c.Id, m.Id);
            Assert.AreEqual(WaitlistStatus.Fulfilled, fulfilled.Status);
            Assert.IsNotNull(fulfilled.FulfilledDate);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void MarkFulfilled_NotNotified_Throws()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c.Id, m.Id);
            _service.MarkFulfilled(c.Id, m.Id); // Still waiting, not notified
        }

        // --- EstimateWaitHours ---

        [TestMethod]
        public void EstimateWaitHours_NotOnList_Returns0()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");

            Assert.AreEqual(0, _service.EstimateWaitHours(c.Id, m.Id));
        }

        [TestMethod]
        public void EstimateWaitHours_NoHistory_DefaultsTo72HoursPerPosition()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c.Id, m.Id);

            var hours = _service.EstimateWaitHours(c.Id, m.Id);
            Assert.AreEqual(72, hours);
        }

        [TestMethod]
        public void EstimateWaitHours_WithHistory_UsesAverage()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie("Inception");

            // Create rental history with ~48h average
            for (int i = 0; i < 5; i++)
            {
                _rentalRepo.Add(new Rental
                {
                    CustomerId = c1.Id,
                    MovieId = m.Id,
                    RentalDate = DateTime.Today.AddDays(-(10 + i * 3)),
                    DueDate = DateTime.Today.AddDays(-(10 + i * 3) + 3),
                    ReturnDate = DateTime.Today.AddDays(-(10 + i * 3) + 2),
                    DailyRate = 5m,
                    Status = RentalStatus.Returned
                });
            }

            _service.JoinWaitlist(c2.Id, m.Id);
            var hours = _service.EstimateWaitHours(c2.Id, m.Id);
            Assert.IsTrue(hours > 0);
            Assert.IsTrue(hours < 72); // Should be ~48h, less than default
        }

        // --- IsOnWaitlist ---

        [TestMethod]
        public void IsOnWaitlist_OnList_ReturnsTrue()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c.Id, m.Id);

            Assert.IsTrue(_service.IsOnWaitlist(c.Id, m.Id));
        }

        [TestMethod]
        public void IsOnWaitlist_NotOnList_ReturnsFalse()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");

            Assert.IsFalse(_service.IsOnWaitlist(c.Id, m.Id));
        }

        [TestMethod]
        public void IsOnWaitlist_AfterCancel_ReturnsFalse()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c.Id, m.Id);
            _service.LeaveWaitlist(c.Id, m.Id);

            Assert.IsFalse(_service.IsOnWaitlist(c.Id, m.Id));
        }

        // --- GetTotalWaiting ---

        [TestMethod]
        public void GetTotalWaiting_CountsAllMovies()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m1 = AddMovie("Movie1");
            var m2 = AddMovie("Movie2");
            _service.JoinWaitlist(c1.Id, m1.Id);
            _service.JoinWaitlist(c2.Id, m1.Id);
            _service.JoinWaitlist(c1.Id, m2.Id);

            Assert.AreEqual(3, _service.GetTotalWaiting());
        }

        // --- CancelAllForMovie ---

        [TestMethod]
        public void CancelAllForMovie_CancelsAllWaiting()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);

            var count = _service.CancelAllForMovie(m.Id);
            Assert.AreEqual(2, count);
            Assert.AreEqual(0, _service.GetWaitlistForMovie(m.Id).Count);
        }

        [TestMethod]
        public void CancelAllForMovie_AlsoCancelsNotified()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie("Inception");
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);
            _service.NotifyNext(m.Id);

            var count = _service.CancelAllForMovie(m.Id);
            Assert.AreEqual(2, count);
        }

        // --- GetById ---

        [TestMethod]
        public void GetById_ExistingEntry_ReturnsIt()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            var entry = _service.JoinWaitlist(c.Id, m.Id);

            var found = _service.GetById(entry.Id);
            Assert.IsNotNull(found);
            Assert.AreEqual(entry.Id, found.Id);
        }

        [TestMethod]
        public void GetById_NonExistent_ReturnsNull()
        {
            Assert.IsNull(_service.GetById(999));
        }

        // --- Report ---

        [TestMethod]
        public void GetReport_EmptyWaitlist_ReturnsZeros()
        {
            var report = _service.GetReport();
            Assert.AreEqual(0, report.TotalEntries);
            Assert.AreEqual(0, report.ActivelyWaiting);
        }

        [TestMethod]
        public void GetReport_WithMixedStatuses_CalculatesCorrectly()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Charlie");
            var m1 = AddMovie("Movie1");
            var m2 = AddMovie("Movie2");

            _service.JoinWaitlist(c1.Id, m1.Id);
            _service.JoinWaitlist(c2.Id, m1.Id);
            _service.JoinWaitlist(c3.Id, m2.Id);
            _service.NotifyNext(m1.Id);
            _service.MarkFulfilled(c1.Id, m1.Id);
            _service.LeaveWaitlist(c3.Id, m2.Id);

            var report = _service.GetReport();
            Assert.AreEqual(3, report.TotalEntries);
            Assert.AreEqual(1, report.ActivelyWaiting); // Bob
            Assert.AreEqual(1, report.Fulfilled);
            Assert.AreEqual(1, report.Cancelled);
            Assert.IsTrue(report.TextSummary.Contains("Waitlist Report"));
        }

        [TestMethod]
        public void GetReport_MostWaitlistedMovies_Ordered()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Charlie");
            var m1 = AddMovie("Popular");
            var m2 = AddMovie("Niche");

            _service.JoinWaitlist(c1.Id, m1.Id);
            _service.JoinWaitlist(c2.Id, m1.Id);
            _service.JoinWaitlist(c3.Id, m1.Id);
            _service.JoinWaitlist(c1.Id, m2.Id);

            var report = _service.GetReport();
            Assert.AreEqual("Popular", report.MostWaitlistedMovies[0].MovieName);
            Assert.AreEqual(3, report.MostWaitlistedMovies[0].WaitingCount);
        }

        // --- Stock Recommendations ---

        [TestMethod]
        public void GetStockRecommendations_HighDemand_Recommended()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var c3 = AddCustomer("Charlie");
            var m = AddMovie("HotMovie");
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);
            _service.JoinWaitlist(c3.Id, m.Id);

            var recs = _service.GetStockRecommendations(minWaiting: 2);
            Assert.AreEqual(1, recs.Count);
            Assert.AreEqual("HotMovie", recs[0].MovieName);
        }

        [TestMethod]
        public void GetStockRecommendations_BelowThreshold_NotRecommended()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("NicheMovie");
            _service.JoinWaitlist(c.Id, m.Id);

            var recs = _service.GetStockRecommendations(minWaiting: 2);
            Assert.AreEqual(0, recs.Count);
        }

        // --- Full Lifecycle ---

        [TestMethod]
        public void FullLifecycle_JoinNotifyFulfill()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie("Inception");

            // Both join
            _service.JoinWaitlist(c1.Id, m.Id);
            _service.JoinWaitlist(c2.Id, m.Id);
            Assert.AreEqual(1, _service.GetPosition(c1.Id, m.Id));
            Assert.AreEqual(2, _service.GetPosition(c2.Id, m.Id));

            // Movie returned — notify first
            var notified = _service.NotifyNext(m.Id);
            Assert.AreEqual("Alice", notified.CustomerName);
            Assert.AreEqual(1, _service.GetPosition(c2.Id, m.Id));

            // Alice picks it up
            _service.MarkFulfilled(c1.Id, m.Id);

            // Movie returned again — notify Bob
            var notified2 = _service.NotifyNext(m.Id);
            Assert.AreEqual("Bob", notified2.CustomerName);
            _service.MarkFulfilled(c2.Id, m.Id);

            // No one waiting
            Assert.IsNull(_service.NotifyNext(m.Id));
            Assert.AreEqual(0, _service.GetTotalWaiting());
        }

        [TestMethod]
        public void FullLifecycle_NotifyExpireThenNext()
        {
            var c1 = AddCustomer("Alice");
            var c2 = AddCustomer("Bob");
            var m = AddMovie("Inception");

            _service.JoinWaitlist(c1.Id, m.Id, pickupWindowHours: 0); // instant expiry
            _service.JoinWaitlist(c2.Id, m.Id);

            _service.NotifyNext(m.Id);

            // Force expiration by processing
            var expired = _service.ProcessExpirations();
            Assert.AreEqual(1, expired.Count);
            Assert.AreEqual("Alice", expired[0].CustomerName);

            // Bob should now be notified
            var bobEntry = _service.GetCustomerWaitlist(c2.Id)
                .FirstOrDefault(e => e.Status == WaitlistStatus.Notified);
            Assert.IsNotNull(bobEntry);
        }

        // --- DefaultPickupWindowHours ---

        [TestMethod]
        public void DefaultPickupWindowHours_AppliedToNewEntries()
        {
            _service.DefaultPickupWindowHours = 24;
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            var entry = _service.JoinWaitlist(c.Id, m.Id);

            Assert.AreEqual(24, entry.PickupWindowHours);
        }

        // --- WaitlistEntry.WaitDuration ---

        [TestMethod]
        public void WaitlistEntry_WaitDuration_Positive()
        {
            var c = AddCustomer("Alice");
            var m = AddMovie("Inception");
            var entry = _service.JoinWaitlist(c.Id, m.Id);

            Assert.IsTrue(entry.WaitDuration.TotalSeconds >= 0);
        }

        // --- Multiple Movies ---

        [TestMethod]
        public void SameCustomer_DifferentMovies_IndependentWaitlists()
        {
            var c = AddCustomer("Alice");
            var m1 = AddMovie("Movie1");
            var m2 = AddMovie("Movie2");
            _service.JoinWaitlist(c.Id, m1.Id);
            _service.JoinWaitlist(c.Id, m2.Id);

            Assert.AreEqual(1, _service.GetPosition(c.Id, m1.Id));
            Assert.AreEqual(1, _service.GetPosition(c.Id, m2.Id));
            Assert.AreEqual(2, _service.GetActiveCustomerWaitlist(c.Id).Count);
        }
    }
}
