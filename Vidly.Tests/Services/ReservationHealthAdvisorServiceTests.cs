using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests.Services
{
    [TestClass]
    public class ReservationHealthAdvisorServiceTests
    {
        // Reference "today" used by the fake clock.
        private static readonly DateTime Today = new DateTime(2026, 5, 20);

        private static ReservationHealthAdvisorService BuildService(
            out InMemoryTestReservationRepo res,
            out InMemoryTestRentalRepo rentals,
            out InMemoryTestMovieRepo movies,
            out InMemoryTestCustomerRepo customers)
        {
            res = new InMemoryTestReservationRepo();
            rentals = new InMemoryTestRentalRepo();
            movies = new InMemoryTestMovieRepo();
            customers = new InMemoryTestCustomerRepo();
            return new ReservationHealthAdvisorService(res, rentals, movies, customers, new FakeClock(Today));
        }

        private static Movie M(int id, string name, decimal? rate = null) =>
            new Movie { Id = id, Name = name, DailyRate = rate };

        private static Reservation R(int id, int movieId, int customerId,
                                     ReservationStatus status,
                                     int reservedDaysAgo,
                                     int? expiresInDays = null,
                                     int queuePos = 1)
        {
            return new Reservation
            {
                Id = id,
                MovieId = movieId,
                CustomerId = customerId,
                Status = status,
                ReservedDate = Today.AddDays(-reservedDaysAgo),
                ExpiresDate = expiresInDays.HasValue ? Today.AddDays(expiresInDays.Value) : (DateTime?)null,
                QueuePosition = queuePos,
                MovieName = "Movie#" + movieId,
                CustomerName = "Cust#" + customerId
            };
        }

        // ── Tests ────────────────────────────────────────────────

        [TestMethod]
        public void NoReservations_HealthyReport()
        {
            var svc = BuildService(out _, out _, out _, out _);
            var report = svc.GenerateReport();

            Assert.AreEqual(0, report.Cases.Count);
            Assert.AreEqual(100, report.Summary.OverallScore);
            Assert.AreEqual('A', report.Summary.Grade);
            CollectionAssert.Contains(report.Summary.Insights, "INSUFFICIENT_DATA");
            Assert.AreEqual(1, report.Playbook.Count);
            Assert.AreEqual("queues_healthy", report.Playbook[0].Id);
        }

        [TestMethod]
        public void StaleReady_GeneratesP0AndForcesGradeF()
        {
            var svc = BuildService(out var res, out _, out var movies, out _);
            movies.Add(M(1, "The Stale Hold"));
            // Ready, expired 3 days ago.
            res.Add(R(1, movieId: 1, customerId: 10, ReservationStatus.Ready,
                      reservedDaysAgo: 10, expiresInDays: -3));

            var report = svc.GenerateReport();
            Assert.AreEqual(1, report.Cases.Count);
            var c = report.Cases[0];
            Assert.AreEqual(QueueVerdict.StalePickupReady, c.Verdict);
            Assert.AreEqual(HealthActionPriority.P0, c.Priority);
            Assert.IsTrue(c.Reasons.Any(r => r.StartsWith("READY_EXPIRED")));
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "expire_stale_holds" &&
                                                    p.Priority == HealthActionPriority.P0));
            Assert.AreEqual('F', report.Summary.Grade);
        }

        [TestMethod]
        public void BlockedByOverdueRental_FlagsP0()
        {
            var svc = BuildService(out var res, out var rentals, out var movies, out _);
            movies.Add(M(2, "The Blocked Title"));
            // Waiting reservation.
            res.Add(R(1, 2, 11, ReservationStatus.Waiting, reservedDaysAgo: 5));
            // Rental on movie 2, overdue 12 days.
            rentals.Add(new Rental
            {
                Id = 100,
                MovieId = 2,
                CustomerId = 99,
                Status = RentalStatus.Overdue,
                RentalDate = Today.AddDays(-20),
                DueDate = Today.AddDays(-12),
                DailyRate = 3m
            });

            var report = svc.GenerateReport();
            Assert.AreEqual(1, report.Cases.Count);
            Assert.AreEqual(QueueVerdict.BlockedByOverdueRental, report.Cases[0].Verdict);
            Assert.AreEqual(HealthActionPriority.P0, report.Cases[0].Priority);
            Assert.IsTrue(report.Cases[0].Reasons.Any(r => r.StartsWith("RENTAL_OVERDUE_")));
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "escalate_blocked_queues"));
        }

        [TestMethod]
        public void LongQueue_FlagsLongQueueVerdictAndPlaybook()
        {
            var svc = BuildService(out var res, out _, out var movies, out _);
            movies.Add(M(3, "Popular Hit"));
            for (int i = 1; i <= 7; i++)
                res.Add(R(i, 3, customerId: 100 + i, ReservationStatus.Waiting, reservedDaysAgo: 2, queuePos: i));

            var report = svc.GenerateReport();
            Assert.AreEqual(1, report.Cases.Count);
            Assert.AreEqual(QueueVerdict.LongQueue, report.Cases[0].Verdict);
            Assert.AreEqual(7, report.Cases[0].ActiveReservationCount);
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "add_inventory_for_long_queues"));
        }

        [TestMethod]
        public void ChronicAbandonment_FlagsHighAbandon()
        {
            var svc = BuildService(out var res, out _, out var movies, out _);
            movies.Add(M(4, "Always Abandoned"));
            // 1 active waiter so the movie earns a case.
            res.Add(R(1, 4, 50, ReservationStatus.Waiting, reservedDaysAgo: 2));
            // 4 expired + 5 fulfilled in recent history => 4/10 = 40%.
            int id = 2;
            for (int i = 0; i < 4; i++)
                res.Add(R(id++, 4, 50 + i, ReservationStatus.Expired, reservedDaysAgo: 30 + i, expiresInDays: -10));
            for (int i = 0; i < 5; i++)
                res.Add(R(id++, 4, 60 + i, ReservationStatus.Fulfilled, reservedDaysAgo: 60 + i));

            var report = svc.GenerateReport();
            Assert.AreEqual(1, report.Cases.Count);
            Assert.AreEqual(QueueVerdict.ChronicAbandonment, report.Cases[0].Verdict);
            Assert.IsTrue(report.Cases[0].AbandonRatePct >= 40);
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "purge_chronic_abandoners"));
        }

        [TestMethod]
        public void LongWaiter_FlagsChurnRisk()
        {
            var svc = BuildService(out var res, out _, out var movies, out _);
            movies.Add(M(5, "Patient Customer"));
            res.Add(R(1, 5, 70, ReservationStatus.Waiting, reservedDaysAgo: 30));

            var report = svc.GenerateReport();
            Assert.AreEqual(QueueVerdict.ChurnRisk, report.Cases[0].Verdict);
            Assert.IsTrue(report.Cases[0].MaxWaiterDays >= 30);
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "proactive_outreach_long_waiters"));
        }

        [TestMethod]
        public void PickupWindowClosingSoon_FlagsQueueStarved()
        {
            var svc = BuildService(out var res, out _, out var movies, out _);
            movies.Add(M(6, "Closing Window"));
            // Ready, expires tomorrow.
            res.Add(R(1, 6, 80, ReservationStatus.Ready, reservedDaysAgo: 5, expiresInDays: 1));

            var report = svc.GenerateReport();
            Assert.AreEqual(QueueVerdict.QueueStarved, report.Cases[0].Verdict);
            Assert.AreEqual(HealthActionPriority.P1, report.Cases[0].Priority);
            Assert.IsTrue(report.Playbook.Any(p => p.Id == "notify_pickup_window_closing"));
        }

        [TestMethod]
        public void QueueOk_WhenNothingWrong()
        {
            var svc = BuildService(out var res, out _, out var movies, out _);
            movies.Add(M(7, "Calm Movie"));
            res.Add(R(1, 7, 90, ReservationStatus.Waiting, reservedDaysAgo: 1));

            var report = svc.GenerateReport();
            Assert.AreEqual(QueueVerdict.QueueOk, report.Cases[0].Verdict);
            Assert.AreNotEqual('F', report.Summary.Grade);
        }

        [TestMethod]
        public void HighValueMovie_IncreasesRisk()
        {
            var svc1 = BuildService(out var res1, out _, out var movies1, out _);
            movies1.Add(M(8, "Cheap", 1.50m));
            res1.Add(R(1, 8, 100, ReservationStatus.Waiting, reservedDaysAgo: 30));

            var svc2 = BuildService(out var res2, out _, out var movies2, out _);
            movies2.Add(M(8, "Premium", 5.00m));
            res2.Add(R(1, 8, 100, ReservationStatus.Waiting, reservedDaysAgo: 30));

            var r1 = svc1.GenerateReport().Cases[0];
            var r2 = svc2.GenerateReport().Cases[0];
            Assert.IsTrue(r2.Risk > r1.Risk, "Premium should outscore cheap");
            Assert.IsTrue(r2.IsHighValueMovie);
            Assert.IsFalse(r1.IsHighValueMovie);
        }

        [TestMethod]
        public void RiskAppetite_AffectsOverallScore()
        {
            var svc = BuildService(out var res, out _, out var movies, out _);
            movies.Add(M(9, "Some Title"));
            // A genuine issue so cases have non-trivial risk.
            res.Add(R(1, 9, 110, ReservationStatus.Waiting, reservedDaysAgo: 30));

            var cautious = svc.GenerateReport(ReservationHealthAppetite.Cautious).Summary.OverallScore;
            var balanced = svc.GenerateReport(ReservationHealthAppetite.Balanced).Summary.OverallScore;
            var aggressive = svc.GenerateReport(ReservationHealthAppetite.Aggressive).Summary.OverallScore;

            Assert.IsTrue(cautious <= balanced, "cautious should score <= balanced");
            Assert.IsTrue(balanced <= aggressive, "balanced should score <= aggressive");
        }

        [TestMethod]
        public void Aggressive_TrimsP3WhenP0Present()
        {
            var svc = BuildService(out var res, out _, out var movies, out _);
            movies.Add(M(10, "Stale"));
            res.Add(R(1, 10, 120, ReservationStatus.Ready, reservedDaysAgo: 10, expiresInDays: -2));

            var balanced = svc.GenerateReport(ReservationHealthAppetite.Balanced);
            var aggressive = svc.GenerateReport(ReservationHealthAppetite.Aggressive);

            // Both should have expire_stale_holds; aggressive must not add p3 fallback either way.
            Assert.IsTrue(balanced.Playbook.Any(p => p.Id == "expire_stale_holds"));
            Assert.IsTrue(aggressive.Playbook.Any(p => p.Id == "expire_stale_holds"));
            Assert.IsFalse(aggressive.Playbook.Any(p => p.Priority == HealthActionPriority.P3));
        }

        [TestMethod]
        public void ToJson_IsDeterministic()
        {
            var svc = BuildService(out var res, out var rentals, out var movies, out _);
            movies.Add(M(11, "Alpha", 5m));
            movies.Add(M(12, "Beta"));
            res.Add(R(1, 11, 200, ReservationStatus.Ready, reservedDaysAgo: 5, expiresInDays: -1));
            res.Add(R(2, 12, 201, ReservationStatus.Waiting, reservedDaysAgo: 25));
            rentals.Add(new Rental { Id = 9, MovieId = 12, CustomerId = 50,
                Status = RentalStatus.Overdue, DailyRate = 3m,
                RentalDate = Today.AddDays(-20), DueDate = Today.AddDays(-10) });

            var a = svc.ToJson(svc.GenerateReport());
            var b = svc.ToJson(svc.GenerateReport());
            Assert.AreEqual(a, b);
            StringAssert.Contains(a, "\"asOfDate\"");
            StringAssert.Contains(a, "\"cases\"");
            StringAssert.Contains(a, "\"playbook\"");
            StringAssert.Contains(a, "\"summary\"");
        }

        [TestMethod]
        public void ToMarkdown_HasCanonicalSections()
        {
            var svc = BuildService(out var res, out _, out var movies, out _);
            movies.Add(M(13, "Doc Test"));
            res.Add(R(1, 13, 300, ReservationStatus.Waiting, reservedDaysAgo: 1));

            var md = svc.ToMarkdown(svc.GenerateReport());
            StringAssert.Contains(md, "## Cases");
            StringAssert.Contains(md, "## Playbook");
            StringAssert.Contains(md, "Reservation Health Advisor");
        }

        [TestMethod]
        public void ToText_RendersHeadline()
        {
            var svc = BuildService(out _, out _, out _, out _);
            var txt = svc.ToText(svc.GenerateReport());
            StringAssert.Contains(txt, "VERDICT:");
            StringAssert.Contains(txt, "queues_healthy");
        }

        [TestMethod]
        public void NullConstructorArgs_Throw()
        {
            var clock = new FakeClock(Today);
            Assert.ThrowsException<ArgumentNullException>(() => new ReservationHealthAdvisorService(
                null, new InMemoryTestRentalRepo(), new InMemoryTestMovieRepo(),
                new InMemoryTestCustomerRepo(), clock));
            Assert.ThrowsException<ArgumentNullException>(() => new ReservationHealthAdvisorService(
                new InMemoryTestReservationRepo(), null, new InMemoryTestMovieRepo(),
                new InMemoryTestCustomerRepo(), clock));
            Assert.ThrowsException<ArgumentNullException>(() => new ReservationHealthAdvisorService(
                new InMemoryTestReservationRepo(), new InMemoryTestRentalRepo(), null,
                new InMemoryTestCustomerRepo(), clock));
            Assert.ThrowsException<ArgumentNullException>(() => new ReservationHealthAdvisorService(
                new InMemoryTestReservationRepo(), new InMemoryTestRentalRepo(),
                new InMemoryTestMovieRepo(), null, clock));
            Assert.ThrowsException<ArgumentNullException>(() => new ReservationHealthAdvisorService(
                new InMemoryTestReservationRepo(), new InMemoryTestRentalRepo(),
                new InMemoryTestMovieRepo(), new InMemoryTestCustomerRepo(), null));
        }

        // ── Test doubles ─────────────────────────────────────────

        private class FakeClock : IClock
        {
            public FakeClock(DateTime today) { _today = today; }
            private readonly DateTime _today;
            public DateTime Now => _today;
            public DateTime Today => _today;
        }

        private class InMemoryTestReservationRepo : IReservationRepository
        {
            private readonly List<Reservation> _data = new List<Reservation>();
            public void Add(Reservation entity) => _data.Add(entity);
            public void Remove(int id) => _data.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Reservation> GetAll() => _data.AsReadOnly();
            public Reservation GetById(int id) => _data.FirstOrDefault(r => r.Id == id);
            public void Update(Reservation entity) { }
            public IReadOnlyList<Reservation> GetByCustomer(int customerId) =>
                _data.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public IReadOnlyList<Reservation> GetByMovie(int movieId) =>
                _data.Where(r => r.MovieId == movieId)
                     .OrderBy(r => r.QueuePosition).ToList().AsReadOnly();
            public IReadOnlyList<Reservation> GetActiveByMovie(int movieId) =>
                _data.Where(r => r.MovieId == movieId &&
                                 (r.Status == ReservationStatus.Waiting ||
                                  r.Status == ReservationStatus.Ready))
                     .OrderBy(r => r.QueuePosition).ToList().AsReadOnly();
            public Reservation GetNextInQueue(int movieId) =>
                _data.Where(r => r.MovieId == movieId && r.Status == ReservationStatus.Waiting)
                     .OrderBy(r => r.QueuePosition).FirstOrDefault();
            public bool HasActiveReservation(int customerId, int movieId) =>
                _data.Any(r => r.CustomerId == customerId && r.MovieId == movieId &&
                               (r.Status == ReservationStatus.Waiting ||
                                r.Status == ReservationStatus.Ready));
            public IReadOnlyList<Reservation> GetExpired() =>
                _data.Where(r => r.IsExpired).ToList().AsReadOnly();
            public IReadOnlyList<Reservation> Search(string query, ReservationStatus? status) =>
                _data.AsReadOnly();
        }

        private class InMemoryTestMovieRepo : IMovieRepository
        {
            private readonly List<Movie> _data = new List<Movie>();
            public void Add(Movie entity) => _data.Add(entity);
            public void Remove(int id) => _data.RemoveAll(m => m.Id == id);
            public IReadOnlyList<Movie> GetAll() => _data.AsReadOnly();
            public Movie GetById(int id) => _data.FirstOrDefault(m => m.Id == id);
            public void Update(Movie entity) { }
            public IReadOnlyList<Movie> GetByReleaseDate(int year, int month) =>
                new List<Movie>().AsReadOnly();
            public Movie GetRandom() => _data.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string query, Genre? genre, int? minRating) =>
                _data.AsReadOnly();
        }

        private class InMemoryTestRentalRepo : IRentalRepository
        {
            private readonly List<Rental> _data = new List<Rental>();
            public void Add(Rental entity) => _data.Add(entity);
            public void Remove(int id) => _data.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _data.AsReadOnly();
            public Rental GetById(int id) => _data.FirstOrDefault(r => r.Id == id);
            public void Update(Rental entity) { }
            public IReadOnlyList<Rental> GetByCustomer(int customerId) =>
                _data.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetActiveByCustomer(int customerId) =>
                _data.Where(r => r.CustomerId == customerId && r.Status != RentalStatus.Returned)
                     .ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int movieId) =>
                _data.Where(r => r.MovieId == movieId).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() =>
                _data.Where(r => r.Status != RentalStatus.Returned).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string query, RentalStatus? status) =>
                _data.AsReadOnly();
            public Rental ReturnRental(int rentalId) => GetById(rentalId);
            public bool IsMovieRentedOut(int movieId) => false;
            public Rental Checkout(Rental rental) => rental;
            public Rental Checkout(Rental rental, int maxConcurrentRentals) => rental;
            public Rental ExtendRental(int rentalId, int days) => GetById(rentalId);
            public bool IsExtended(int rentalId) => false;
            public RentalStats GetStats() => new RentalStats { TotalRentals = _data.Count };
        }

        private class InMemoryTestCustomerRepo : ICustomerRepository
        {
            private readonly List<Customer> _data = new List<Customer>();
            public void Add(Customer entity) => _data.Add(entity);
            public void Remove(int id) => _data.RemoveAll(c => c.Id == id);
            public IReadOnlyList<Customer> GetAll() => _data.AsReadOnly();
            public Customer GetById(int id) => _data.FirstOrDefault(c => c.Id == id);
            public void Update(Customer entity) { }
            public IReadOnlyList<Customer> Search(string query, MembershipType? membershipType) =>
                _data.AsReadOnly();
            public IReadOnlyList<Customer> GetByMemberSince(int year, int month) =>
                new List<Customer>().AsReadOnly();
            public CustomerStats GetStats() => new CustomerStats { TotalCustomers = _data.Count };
        }
    }
}
