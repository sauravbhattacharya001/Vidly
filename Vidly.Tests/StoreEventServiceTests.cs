using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class StoreEventServiceTests
    {
        private StoreEventService _service;

        [TestInitialize]
        public void Setup()
        {
            _service = new StoreEventService();
        }

        private StoreEvent CreateTestEvent(
            string title = "Movie Night",
            StoreEventType type = StoreEventType.MovieScreening,
            int capacity = 50,
            decimal? ticketPrice = null,
            int? featuredMovieId = null,
            Genre? genre = null,
            MembershipType? minMembership = null)
        {
            return _service.CreateEvent(new StoreEvent
            {
                Title = title,
                Description = "A fun event",
                EventType = type,
                StartTime = DateTime.Now.AddDays(7),
                EndTime = DateTime.Now.AddDays(7).AddHours(3),
                Capacity = capacity,
                TicketPrice = ticketPrice,
                FeaturedMovieId = featuredMovieId,
                Genre = genre,
                MinimumMembership = minMembership
            });
        }

        // ── CreateEvent ──────────────────────────────────────────

        [TestMethod]
        public void CreateEvent_ValidEvent_ReturnsWithId()
        {
            var ev = CreateTestEvent();
            Assert.IsTrue(ev.Id > 0);
            Assert.AreEqual(StoreEventStatus.Upcoming, ev.Status);
        }

        [TestMethod]
        public void CreateEvent_SequentialIds()
        {
            var ev1 = CreateTestEvent("Event 1");
            var ev2 = CreateTestEvent("Event 2");
            Assert.AreEqual(ev1.Id + 1, ev2.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateEvent_NullTitle_Throws()
        {
            _service.CreateEvent(new StoreEvent
            {
                Title = null,
                StartTime = DateTime.Now.AddDays(1),
                EndTime = DateTime.Now.AddDays(1).AddHours(2),
                Capacity = 10
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateEvent_EndBeforeStart_Throws()
        {
            _service.CreateEvent(new StoreEvent
            {
                Title = "Bad Times",
                StartTime = DateTime.Now.AddDays(2),
                EndTime = DateTime.Now.AddDays(1),
                Capacity = 10
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateEvent_ZeroCapacity_Throws()
        {
            _service.CreateEvent(new StoreEvent
            {
                Title = "No Room",
                StartTime = DateTime.Now.AddDays(1),
                EndTime = DateTime.Now.AddDays(1).AddHours(2),
                Capacity = 0
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateEvent_NegativePrice_Throws()
        {
            _service.CreateEvent(new StoreEvent
            {
                Title = "Negative",
                StartTime = DateTime.Now.AddDays(1),
                EndTime = DateTime.Now.AddDays(1).AddHours(2),
                Capacity = 10,
                TicketPrice = -5m
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void CreateEvent_NullArgument_Throws()
        {
            _service.CreateEvent(null);
        }

        // ── GetEvents / GetById / GetUpcoming ────────────────────

        [TestMethod]
        public void GetEvents_NoFilter_ReturnsAll()
        {
            CreateTestEvent("A");
            CreateTestEvent("B");
            Assert.AreEqual(2, _service.GetEvents().Count);
        }

        [TestMethod]
        public void GetEvents_FilterByStatus()
        {
            var ev = CreateTestEvent("Active");
            CreateTestEvent("Also Active");
            _service.CancelEvent(ev.Id);

            Assert.AreEqual(1, _service.GetEvents(StoreEventStatus.Upcoming).Count);
            Assert.AreEqual(1, _service.GetEvents(StoreEventStatus.Cancelled).Count);
        }

        [TestMethod]
        public void GetById_Exists_ReturnsEvent()
        {
            var ev = CreateTestEvent();
            var found = _service.GetById(ev.Id);
            Assert.IsNotNull(found);
            Assert.AreEqual(ev.Title, found.Title);
        }

        [TestMethod]
        public void GetById_NotExists_ReturnsNull()
        {
            Assert.IsNull(_service.GetById(999));
        }

        [TestMethod]
        public void GetUpcoming_SortsByStartTime()
        {
            _service.CreateEvent(new StoreEvent
            {
                Title = "Later",
                StartTime = DateTime.Now.AddDays(14),
                EndTime = DateTime.Now.AddDays(14).AddHours(2),
                Capacity = 10
            });
            _service.CreateEvent(new StoreEvent
            {
                Title = "Sooner",
                StartTime = DateTime.Now.AddDays(3),
                EndTime = DateTime.Now.AddDays(3).AddHours(2),
                Capacity = 10
            });

            var upcoming = _service.GetUpcoming();
            Assert.AreEqual("Sooner", upcoming[0].Title);
            Assert.AreEqual("Later", upcoming[1].Title);
        }

        // ── CancelEvent / CompleteEvent ──────────────────────────

        [TestMethod]
        public void CancelEvent_SetsStatusAndCancelsRsvps()
        {
            var ev = CreateTestEvent(capacity: 10);
            _service.Rsvp(ev.Id, 1);
            _service.CancelEvent(ev.Id);

            Assert.AreEqual(StoreEventStatus.Cancelled, _service.GetById(ev.Id).Status);
            Assert.AreEqual(0, _service.GetEventRsvps(ev.Id).Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CancelEvent_CompletedEvent_Throws()
        {
            var ev = CreateTestEvent();
            _service.CompleteEvent(ev.Id);
            _service.CancelEvent(ev.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CancelEvent_NotFound_Throws()
        {
            _service.CancelEvent(999);
        }

        [TestMethod]
        public void CompleteEvent_SetsStatus()
        {
            var ev = CreateTestEvent();
            _service.CompleteEvent(ev.Id);
            Assert.AreEqual(StoreEventStatus.Completed, _service.GetById(ev.Id).Status);
        }

        // ── RSVP ────────────────────────────────────────────────

        [TestMethod]
        public void Rsvp_ValidRequest_ReturnsRsvp()
        {
            var ev = CreateTestEvent(capacity: 10);
            var rsvp = _service.Rsvp(ev.Id, 1);

            Assert.IsTrue(rsvp.Id > 0);
            Assert.AreEqual(ev.Id, rsvp.EventId);
            Assert.AreEqual(1, rsvp.CustomerId);
            Assert.AreEqual(1, rsvp.GuestCount);
        }

        [TestMethod]
        public void Rsvp_WithGuests_CountsCorrectly()
        {
            var ev = CreateTestEvent(capacity: 10);
            _service.Rsvp(ev.Id, 1, guestCount: 3);

            Assert.AreEqual(7, _service.GetRemainingCapacity(ev.Id));
        }

        [TestMethod]
        public void Rsvp_AtCapacity_AutoMarksSoldOut()
        {
            var ev = CreateTestEvent(capacity: 2);
            _service.Rsvp(ev.Id, 1);
            _service.Rsvp(ev.Id, 2);

            Assert.AreEqual(StoreEventStatus.Soldout, _service.GetById(ev.Id).Status);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Rsvp_SoldOutEvent_Throws()
        {
            var ev = CreateTestEvent(capacity: 1);
            _service.Rsvp(ev.Id, 1);
            _service.Rsvp(ev.Id, 2);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Rsvp_DuplicateCustomer_Throws()
        {
            var ev = CreateTestEvent(capacity: 10);
            _service.Rsvp(ev.Id, 1);
            _service.Rsvp(ev.Id, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Rsvp_ExceedsCapacity_Throws()
        {
            var ev = CreateTestEvent(capacity: 3);
            _service.Rsvp(ev.Id, 1, guestCount: 2);
            _service.Rsvp(ev.Id, 2, guestCount: 3);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Rsvp_CancelledEvent_Throws()
        {
            var ev = CreateTestEvent();
            _service.CancelEvent(ev.Id);
            _service.Rsvp(ev.Id, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Rsvp_CompletedEvent_Throws()
        {
            var ev = CreateTestEvent();
            _service.CompleteEvent(ev.Id);
            _service.Rsvp(ev.Id, 1);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Rsvp_InsufficientMembership_Throws()
        {
            var ev = CreateTestEvent(minMembership: MembershipType.Gold);
            _service.Rsvp(ev.Id, 1, customerMembership: MembershipType.Silver);
        }

        [TestMethod]
        public void Rsvp_SufficientMembership_Succeeds()
        {
            var ev = CreateTestEvent(minMembership: MembershipType.Silver);
            var rsvp = _service.Rsvp(ev.Id, 1, customerMembership: MembershipType.Gold);
            Assert.IsNotNull(rsvp);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Rsvp_ZeroGuestCount_Throws()
        {
            var ev = CreateTestEvent();
            _service.Rsvp(ev.Id, 1, guestCount: 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Rsvp_NotFoundEvent_Throws()
        {
            _service.Rsvp(999, 1);
        }

        // ── CancelRsvp ──────────────────────────────────────────

        [TestMethod]
        public void CancelRsvp_ReopensCapacity()
        {
            var ev = CreateTestEvent(capacity: 2);
            _service.Rsvp(ev.Id, 1);
            _service.Rsvp(ev.Id, 2);

            Assert.AreEqual(StoreEventStatus.Soldout, _service.GetById(ev.Id).Status);

            _service.CancelRsvp(ev.Id, 1);
            Assert.AreEqual(StoreEventStatus.Upcoming, _service.GetById(ev.Id).Status);
            Assert.AreEqual(1, _service.GetRemainingCapacity(ev.Id));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CancelRsvp_NoActiveRsvp_Throws()
        {
            var ev = CreateTestEvent();
            _service.CancelRsvp(ev.Id, 999);
        }

        [TestMethod]
        public void CancelRsvp_AllowsReRsvp()
        {
            var ev = CreateTestEvent(capacity: 5);
            _service.Rsvp(ev.Id, 1);
            _service.CancelRsvp(ev.Id, 1);
            var rsvp = _service.Rsvp(ev.Id, 1);
            Assert.IsNotNull(rsvp);
        }

        // ── Attendance ──────────────────────────────────────────

        [TestMethod]
        public void RecordAttendance_MarksAttended()
        {
            var ev = CreateTestEvent();
            _service.Rsvp(ev.Id, 1);
            _service.RecordAttendance(ev.Id, 1);

            var rsvps = _service.GetEventRsvps(ev.Id);
            Assert.IsTrue(rsvps[0].Attended);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void RecordAttendance_NoRsvp_Throws()
        {
            var ev = CreateTestEvent();
            _service.RecordAttendance(ev.Id, 999);
        }

        // ── GetCustomerEvents ───────────────────────────────────

        [TestMethod]
        public void GetCustomerEvents_ReturnsRsvpedEvents()
        {
            var ev1 = CreateTestEvent("Event A");
            var ev2 = CreateTestEvent("Event B");
            CreateTestEvent("Event C");

            _service.Rsvp(ev1.Id, 1);
            _service.Rsvp(ev2.Id, 1);

            var events = _service.GetCustomerEvents(1);
            Assert.AreEqual(2, events.Count);
        }

        [TestMethod]
        public void GetCustomerEvents_ExcludesCancelled()
        {
            var ev = CreateTestEvent("Event A");
            _service.Rsvp(ev.Id, 1);
            _service.CancelRsvp(ev.Id, 1);

            Assert.AreEqual(0, _service.GetCustomerEvents(1).Count);
        }

        // ── RemainingCapacity ───────────────────────────────────

        [TestMethod]
        public void GetRemainingCapacity_Correct()
        {
            var ev = CreateTestEvent(capacity: 10);
            _service.Rsvp(ev.Id, 1, guestCount: 3);
            _service.Rsvp(ev.Id, 2, guestCount: 2);

            Assert.AreEqual(5, _service.GetRemainingCapacity(ev.Id));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetRemainingCapacity_NotFound_Throws()
        {
            _service.GetRemainingCapacity(999);
        }

        // ── Recommendations ─────────────────────────────────────

        [TestMethod]
        public void GetRecommendations_GenreMatch_HigherScore()
        {
            CreateTestEvent("Action Night", genre: Genre.Action);
            CreateTestEvent("Comedy Night", genre: Genre.Comedy);

            var movies = new List<Movie>
            {
                new Movie { Id = 10, Genre = Genre.Action },
                new Movie { Id = 11, Genre = Genre.Action },
                new Movie { Id = 12, Genre = Genre.Action }
            };
            var rentals = new List<Rental>
            {
                new Rental { CustomerId = 1, MovieId = 10 },
                new Rental { CustomerId = 1, MovieId = 11 },
                new Rental { CustomerId = 1, MovieId = 12 }
            };

            var suggestions = _service.GetRecommendations(1, rentals, movies);
            Assert.IsTrue(suggestions.Count >= 1);
            Assert.AreEqual("Action Night", suggestions[0].Event.Title);
            Assert.IsTrue(suggestions[0].RelevanceScore > 0);
        }

        [TestMethod]
        public void GetRecommendations_FeaturedMovieMatch()
        {
            CreateTestEvent("Featured Match", featuredMovieId: 42);

            var movies = new List<Movie> { new Movie { Id = 42 } };
            var rentals = new List<Rental>
            {
                new Rental { CustomerId = 1, MovieId = 42 }
            };

            var suggestions = _service.GetRecommendations(1, rentals, movies);
            Assert.IsTrue(suggestions.Count >= 1);
            Assert.IsTrue(suggestions.Any(s => s.Reason.Contains("featured movie")));
        }

        [TestMethod]
        public void GetRecommendations_FreeEventBoost()
        {
            CreateTestEvent("Free Event", ticketPrice: 0m);

            var suggestions = _service.GetRecommendations(1, new List<Rental>(), new List<Movie>());
            Assert.IsTrue(suggestions.Count >= 1);
            Assert.IsTrue(suggestions.Any(s => s.Reason.Contains("Free event")));
        }

        [TestMethod]
        public void GetRecommendations_RespectsLimit()
        {
            for (int i = 0; i < 10; i++)
                CreateTestEvent($"Event {i}", ticketPrice: 0m);

            var suggestions = _service.GetRecommendations(
                1, new List<Rental>(), new List<Movie>(), limit: 3);
            Assert.AreEqual(3, suggestions.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GetRecommendations_NullRentals_Throws()
        {
            _service.GetRecommendations(1, null, new List<Movie>());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GetRecommendations_NullMovies_Throws()
        {
            _service.GetRecommendations(1, new List<Rental>(), null);
        }

        // ── GetEventsInRange ────────────────────────────────────

        [TestMethod]
        public void GetEventsInRange_FiltersCorrectly()
        {
            _service.CreateEvent(new StoreEvent
            {
                Title = "Soon",
                StartTime = DateTime.Now.AddDays(2),
                EndTime = DateTime.Now.AddDays(2).AddHours(2),
                Capacity = 10
            });
            _service.CreateEvent(new StoreEvent
            {
                Title = "Far Away",
                StartTime = DateTime.Now.AddDays(30),
                EndTime = DateTime.Now.AddDays(30).AddHours(2),
                Capacity = 10
            });

            var events = _service.GetEventsInRange(7);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual("Soon", events[0].Title);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GetEventsInRange_ZeroDays_Throws()
        {
            _service.GetEventsInRange(0);
        }

        // ── Stats ───────────────────────────────────────────────

        [TestMethod]
        public void GetStats_EmptyService_ReturnsZeros()
        {
            var stats = _service.GetStats();
            Assert.AreEqual(0, stats.TotalEvents);
            Assert.AreEqual(0, stats.TotalRsvps);
            Assert.AreEqual(0m, stats.TotalRevenue);
        }

        [TestMethod]
        public void GetStats_WithEvents_CountsCorrectly()
        {
            var ev = CreateTestEvent(capacity: 10, ticketPrice: 15m);
            _service.Rsvp(ev.Id, 1, guestCount: 2);
            _service.Rsvp(ev.Id, 2, guestCount: 1);

            var stats = _service.GetStats();
            Assert.AreEqual(1, stats.TotalEvents);
            Assert.AreEqual(2, stats.TotalRsvps);
            Assert.AreEqual(45m, stats.TotalRevenue);
        }

        [TestMethod]
        public void GetStats_AttendanceRate_CompletedOnly()
        {
            var ev = CreateTestEvent(capacity: 10);
            _service.Rsvp(ev.Id, 1);
            _service.Rsvp(ev.Id, 2);
            _service.RecordAttendance(ev.Id, 1);
            _service.CompleteEvent(ev.Id);

            var stats = _service.GetStats();
            Assert.AreEqual(50.0, stats.AverageAttendanceRate);
        }

        [TestMethod]
        public void GetStats_TypeBreakdown_Populated()
        {
            CreateTestEvent("Screening", type: StoreEventType.MovieScreening);
            CreateTestEvent("Trivia", type: StoreEventType.TriviaNight);

            var stats = _service.GetStats();
            Assert.IsTrue(stats.TypeBreakdown.Count >= 2);
        }

        [TestMethod]
        public void GetStats_MostPopularEvent()
        {
            var ev1 = CreateTestEvent("Popular", capacity: 50);
            CreateTestEvent("Less Popular", capacity: 50);

            _service.Rsvp(ev1.Id, 1);
            _service.Rsvp(ev1.Id, 2);
            _service.Rsvp(ev1.Id, 3);

            var stats = _service.GetStats();
            Assert.AreEqual("Popular", stats.MostPopularEvent);
        }
    }
}
