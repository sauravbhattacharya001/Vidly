using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vidly.Models;
using Vidly.Repositories;
using Vidly.Services;

namespace Vidly.Tests
{
    [TestClass]
    public class ReservationServiceTests
    {
        // ── Test Doubles ─────────────────────────────────────────────

        private class TestCustomerRepository : ICustomerRepository
        {
            private readonly Dictionary<int, Customer> _customers = new Dictionary<int, Customer>();
            public void Add(Customer c) => _customers[c.Id] = c;
            public Customer GetById(int id) => _customers.TryGetValue(id, out var c) ? c : null;
            public IReadOnlyList<Customer> GetAll() => _customers.Values.ToList().AsReadOnly();
            public void Update(Customer c) { if (_customers.ContainsKey(c.Id)) _customers[c.Id] = c; }
            public void Remove(int id) => _customers.Remove(id);
            public IReadOnlyList<Customer> Search(string q, MembershipType? mt) => GetAll();
            public IReadOnlyList<Customer> GetByMemberSince(int y, int m) => new List<Customer>().AsReadOnly();
            public CustomerStats GetStats() => new CustomerStats { TotalCustomers = _customers.Count };
        }

        private class TestMovieRepository : IMovieRepository
        {
            private readonly Dictionary<int, Movie> _movies = new Dictionary<int, Movie>();
            public void Add(Movie m) => _movies[m.Id] = m;
            public Movie GetById(int id) => _movies.TryGetValue(id, out var m) ? m : null;
            public IReadOnlyList<Movie> GetAll() => _movies.Values.ToList().AsReadOnly();
            public void Update(Movie m) { if (_movies.ContainsKey(m.Id)) _movies[m.Id] = m; }
            public void Remove(int id) => _movies.Remove(id);
            public IReadOnlyList<Movie> GetByReleaseDate(int y, int m) => new List<Movie>().AsReadOnly();
            public Movie GetRandom() => _movies.Values.FirstOrDefault();
            public IReadOnlyList<Movie> Search(string q, Genre? g, int? r) => GetAll();
        }

        private class TestRentalRepository : IRentalRepository
        {
            private readonly List<Rental> _rentals = new List<Rental>();
            private int _nextId = 1;
            public void Add(Rental r) { r.Id = _nextId++; _rentals.Add(r); }
            public Rental GetById(int id) => _rentals.FirstOrDefault(r => r.Id == id);
            public IReadOnlyList<Rental> GetAll() => _rentals.AsReadOnly();
            public IReadOnlyList<Rental> GetByCustomer(int customerId) =>
                _rentals.Where(r => r.CustomerId == customerId).ToList().AsReadOnly();
            public void Update(Rental r) { var i = _rentals.FindIndex(x => x.Id == r.Id); if (i >= 0) _rentals[i] = r; }
            public void Remove(int id) => _rentals.RemoveAll(r => r.Id == id);
            public IReadOnlyList<Rental> GetActiveByCustomer(int cid) =>
                _rentals.Where(r => r.CustomerId == cid && r.Status != RentalStatus.Returned).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetByMovie(int mid) =>
                _rentals.Where(r => r.MovieId == mid).ToList().AsReadOnly();
            public IReadOnlyList<Rental> GetOverdue() =>
                _rentals.Where(r => r.Status == RentalStatus.Overdue).ToList().AsReadOnly();
            public IReadOnlyList<Rental> Search(string q, RentalStatus? s) => GetAll();
            public Rental ReturnRental(int id)
            {
                var r = GetById(id);
                if (r != null) { r.Status = RentalStatus.Returned; r.ReturnDate = DateTime.Today; }
                return r;
            }
            public bool IsMovieRentedOut(int mid) =>
                _rentals.Any(r => r.MovieId == mid && r.Status != RentalStatus.Returned);
            public Rental Checkout(Rental rental) { Add(rental); return rental; }

            public Rental Checkout(Rental rental, int maxConcurrentRentals)
            {
                return Checkout(rental);
            }
            public RentalStats GetStats() => new RentalStats { TotalRentals = _rentals.Count };
        }

        // ── Setup ────────────────────────────────────────────────────

        private TestCustomerRepository _customers;
        private TestMovieRepository _movies;
        private TestRentalRepository _rentals;
        private InMemoryReservationRepository _reservations;
        private ReservationService _service;

        [TestInitialize]
        public void Setup()
        {
            _customers = new TestCustomerRepository();
            _movies = new TestMovieRepository();
            _rentals = new TestRentalRepository();
            _reservations = new InMemoryReservationRepository();
            _service = new ReservationService(_reservations, _rentals, _movies, _customers);

            // Seed data
            _customers.Add(new Customer { Id = 1, Name = "Alice", MembershipType = MembershipType.Gold });
            _customers.Add(new Customer { Id = 2, Name = "Bob", MembershipType = MembershipType.Silver });
            _customers.Add(new Customer { Id = 3, Name = "Charlie", MembershipType = MembershipType.Basic });

            _movies.Add(new Movie { Id = 10, Name = "Inception", Genre = Genre.SciFi, Rating = 5 });
            _movies.Add(new Movie { Id = 20, Name = "Titanic", Genre = Genre.Drama, Rating = 4 });
            _movies.Add(new Movie { Id = 30, Name = "Toy Story", Genre = Genre.Comedy, Rating = 5 });

            // Rent out Inception so it can be reserved
            _rentals.Add(new Rental
            {
                CustomerId = 3,
                CustomerName = "Charlie",
                MovieId = 10,
                MovieName = "Inception",
                RentalDate = DateTime.Today.AddDays(-3),
                DueDate = DateTime.Today.AddDays(4),
                DailyRate = 2.99m,
                Status = RentalStatus.Active
            });
        }

        // ── Place Reservation ────────────────────────────────────────

        [TestMethod]
        public void PlaceReservation_CreatesReservation_ForRentedMovie()
        {
            var result = _service.PlaceReservation(1, 10);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.CustomerId);
            Assert.AreEqual(10, result.MovieId);
            Assert.AreEqual("Alice", result.CustomerName);
            Assert.AreEqual("Inception", result.MovieName);
            Assert.AreEqual(ReservationStatus.Waiting, result.Status);
            Assert.AreEqual(1, result.QueuePosition);
            Assert.AreEqual(DateTime.Today, result.ReservedDate);
            Assert.IsNull(result.ExpiresDate);
            Assert.IsNull(result.FulfilledDate);
        }

        [TestMethod]
        public void PlaceReservation_AssignsSequentialQueuePositions()
        {
            _service.PlaceReservation(1, 10); // position 1
            _service.PlaceReservation(2, 10); // position 2

            var queue = _service.GetMovieQueue(10);
            Assert.AreEqual(2, queue.Count);
            Assert.AreEqual(1, queue[0].QueuePosition);
            Assert.AreEqual(2, queue[1].QueuePosition);
            Assert.AreEqual("Alice", queue[0].CustomerName);
            Assert.AreEqual("Bob", queue[1].CustomerName);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void PlaceReservation_Throws_WhenMovieIsAvailable()
        {
            // Toy Story is not rented out
            _service.PlaceReservation(1, 30);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void PlaceReservation_Throws_WhenCustomerNotFound()
        {
            _service.PlaceReservation(999, 10);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void PlaceReservation_Throws_WhenMovieNotFound()
        {
            _service.PlaceReservation(1, 999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void PlaceReservation_Throws_WhenDuplicateReservation()
        {
            _service.PlaceReservation(1, 10);
            _service.PlaceReservation(1, 10); // duplicate
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void PlaceReservation_Throws_WhenCustomerLimitReached()
        {
            // Rent out 6 movies
            for (int i = 0; i < 6; i++)
            {
                var mid = 100 + i;
                _movies.Add(new Movie { Id = mid, Name = $"Movie {mid}" });
                _rentals.Add(new Rental
                {
                    CustomerId = 2,
                    MovieId = mid,
                    RentalDate = DateTime.Today,
                    DueDate = DateTime.Today.AddDays(7),
                    DailyRate = 1.99m,
                    Status = RentalStatus.Active
                });
            }

            // Reserve 5 (max)
            for (int i = 0; i < 5; i++)
                _service.PlaceReservation(1, 100 + i);

            // 6th should fail
            _service.PlaceReservation(1, 105);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void PlaceReservation_Throws_WhenQueueIsFull()
        {
            // Add 10 customers and reserve
            for (int i = 0; i < 10; i++)
            {
                var cid = 100 + i;
                _customers.Add(new Customer { Id = cid, Name = $"Customer {cid}" });
                _service.PlaceReservation(cid, 10);
            }

            // 11th should fail
            _customers.Add(new Customer { Id = 200, Name = "OverflowCustomer" });
            _service.PlaceReservation(200, 10);
        }

        // ── Cancel Reservation ───────────────────────────────────────

        [TestMethod]
        public void CancelReservation_SetsCancelledStatus()
        {
            var reservation = _service.PlaceReservation(1, 10);
            var result = _service.CancelReservation(reservation.Id);

            Assert.AreEqual(ReservationStatus.Cancelled, result.Status);
        }

        [TestMethod]
        public void CancelReservation_CompactsQueue()
        {
            _service.PlaceReservation(1, 10); // pos 1
            var bob = _service.PlaceReservation(2, 10); // pos 2

            _service.CancelReservation(1); // cancel Alice (pos 1)

            var queue = _service.GetMovieQueue(10);
            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual(1, queue[0].QueuePosition); // Bob moved to 1
            Assert.AreEqual("Bob", queue[0].CustomerName);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CancelReservation_Throws_WhenNotFound()
        {
            _service.CancelReservation(999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CancelReservation_Throws_WhenAlreadyCancelled()
        {
            var reservation = _service.PlaceReservation(1, 10);
            _service.CancelReservation(reservation.Id);
            _service.CancelReservation(reservation.Id); // already cancelled
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CancelReservation_Throws_WhenAlreadyFulfilled()
        {
            var reservation = _service.PlaceReservation(1, 10);
            // Manually set to fulfilled
            reservation.Status = ReservationStatus.Fulfilled;
            _reservations.Update(reservation);

            _service.CancelReservation(reservation.Id);
        }

        // ── Notify Next In Queue ─────────────────────────────────────

        [TestMethod]
        public void NotifyNextInQueue_ActivatesFirstWaiting()
        {
            _service.PlaceReservation(1, 10);
            _service.PlaceReservation(2, 10);

            var result = _service.NotifyNextInQueue(10);

            Assert.IsNotNull(result);
            Assert.AreEqual("Alice", result.CustomerName);
            Assert.AreEqual(ReservationStatus.Ready, result.Status);
            Assert.IsNotNull(result.ExpiresDate);
            Assert.AreEqual(
                DateTime.Today.AddDays(ReservationService.PickupWindowDays),
                result.ExpiresDate.Value);
        }

        [TestMethod]
        public void NotifyNextInQueue_ReturnsNull_WhenNoWaiting()
        {
            var result = _service.NotifyNextInQueue(10);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void NotifyNextInQueue_SkipsReadyReservations()
        {
            _service.PlaceReservation(1, 10);
            _service.PlaceReservation(2, 10);

            // Notify first — Alice becomes Ready
            _service.NotifyNextInQueue(10);

            // Notify again — should get Bob, not Alice again
            // But Alice is Ready (not Waiting), so Bob is next Waiting
            var result = _service.NotifyNextInQueue(10);
            Assert.IsNotNull(result);
            Assert.AreEqual("Bob", result.CustomerName);
        }

        // ── Fulfill Reservation ──────────────────────────────────────

        [TestMethod]
        public void FulfillReservation_SetsFulfilledStatus()
        {
            var reservation = _service.PlaceReservation(1, 10);
            _service.NotifyNextInQueue(10); // set to Ready

            var result = _service.FulfillReservation(reservation.Id);

            Assert.AreEqual(ReservationStatus.Fulfilled, result.Status);
            Assert.AreEqual(DateTime.Today, result.FulfilledDate);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void FulfillReservation_Throws_WhenNotReady()
        {
            var reservation = _service.PlaceReservation(1, 10);
            // Still Waiting, not Ready
            _service.FulfillReservation(reservation.Id);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void FulfillReservation_Throws_WhenNotFound()
        {
            _service.FulfillReservation(999);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void FulfillReservation_Throws_WhenPickupExpired()
        {
            var reservation = _service.PlaceReservation(1, 10);
            _service.NotifyNextInQueue(10);

            // Simulate expired window
            reservation = _reservations.GetById(reservation.Id);
            reservation.ExpiresDate = DateTime.Today.AddDays(-1);
            _reservations.Update(reservation);

            _service.FulfillReservation(reservation.Id);
        }

        [TestMethod]
        public void FulfillReservation_CompactsRemainingQueue()
        {
            _service.PlaceReservation(1, 10); // pos 1
            _service.PlaceReservation(2, 10); // pos 2

            _service.NotifyNextInQueue(10); // Alice -> Ready
            _service.FulfillReservation(1); // Alice fulfilled

            var queue = _service.GetMovieQueue(10);
            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual(1, queue[0].QueuePosition); // Bob at pos 1
        }

        // ── Queue Queries ────────────────────────────────────────────

        [TestMethod]
        public void GetCustomerReservations_ReturnsAllForCustomer()
        {
            // Rent out another movie
            _rentals.Add(new Rental
            {
                CustomerId = 2, MovieId = 20, RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7), DailyRate = 1.99m,
                Status = RentalStatus.Active
            });

            _service.PlaceReservation(1, 10);
            _service.PlaceReservation(1, 20);

            var result = _service.GetCustomerReservations(1);
            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void GetMovieQueue_ReturnsOnlyActiveReservations()
        {
            _service.PlaceReservation(1, 10);
            _service.PlaceReservation(2, 10);
            _service.CancelReservation(1); // cancel Alice

            var queue = _service.GetMovieQueue(10);
            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual("Bob", queue[0].CustomerName);
        }

        [TestMethod]
        public void GetQueuePosition_ReturnsCorrectPosition()
        {
            _service.PlaceReservation(1, 10);
            _service.PlaceReservation(2, 10);

            Assert.AreEqual(1, _service.GetQueuePosition(1, 10));
            Assert.AreEqual(2, _service.GetQueuePosition(2, 10));
        }

        [TestMethod]
        public void GetQueuePosition_ReturnsZero_WhenNoReservation()
        {
            Assert.AreEqual(0, _service.GetQueuePosition(1, 10));
        }

        [TestMethod]
        public void HasReservations_ReturnsTrue_WhenActiveExist()
        {
            _service.PlaceReservation(1, 10);
            Assert.IsTrue(_service.HasReservations(10));
        }

        [TestMethod]
        public void HasReservations_ReturnsFalse_WhenNone()
        {
            Assert.IsFalse(_service.HasReservations(10));
        }

        [TestMethod]
        public void HasReservations_ReturnsFalse_WhenAllCancelled()
        {
            var r = _service.PlaceReservation(1, 10);
            _service.CancelReservation(r.Id);
            Assert.IsFalse(_service.HasReservations(10));
        }

        // ── Wait Time Estimation ─────────────────────────────────────

        [TestMethod]
        public void EstimateWaitDays_ReturnsNegativeOne_WhenNoReservation()
        {
            Assert.AreEqual(-1, _service.EstimateWaitDays(1, 10));
        }

        [TestMethod]
        public void EstimateWaitDays_UsesAverageRentalDuration()
        {
            // Add completed rental history for the movie
            _rentals.Add(new Rental
            {
                CustomerId = 2, MovieId = 10,
                RentalDate = DateTime.Today.AddDays(-10),
                ReturnDate = DateTime.Today.AddDays(-5), // 5 days
                DueDate = DateTime.Today.AddDays(-3),
                DailyRate = 2.99m,
                Status = RentalStatus.Returned
            });

            _service.PlaceReservation(1, 10); // position 1

            var estimate = _service.EstimateWaitDays(1, 10);
            // avg = 5 days, position 1 → 5 days
            Assert.AreEqual(5, estimate);
        }

        [TestMethod]
        public void EstimateWaitDays_DefaultsToSevenDays_WhenNoHistory()
        {
            _service.PlaceReservation(1, 10);
            var estimate = _service.EstimateWaitDays(1, 10);
            // No completed rentals → default 7 days × position 1
            Assert.AreEqual(7, estimate);
        }

        [TestMethod]
        public void EstimateWaitDays_ScalesWithPosition()
        {
            _service.PlaceReservation(1, 10); // position 1
            _service.PlaceReservation(2, 10); // position 2

            var est1 = _service.EstimateWaitDays(1, 10);
            var est2 = _service.EstimateWaitDays(2, 10);

            Assert.IsTrue(est2 > est1);
            Assert.AreEqual(7, est1);  // 7 * 1
            Assert.AreEqual(14, est2); // 7 * 2
        }

        // ── Search ───────────────────────────────────────────────────

        [TestMethod]
        public void Search_FindsByCustomerName()
        {
            _service.PlaceReservation(1, 10);
            var results = _service.Search("Alice");
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("Alice", results[0].CustomerName);
        }

        [TestMethod]
        public void Search_FindsByMovieName()
        {
            _service.PlaceReservation(1, 10);
            var results = _service.Search("Inception");
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Search_FiltersByStatus()
        {
            var r = _service.PlaceReservation(1, 10);
            _service.CancelReservation(r.Id);

            var waiting = _service.Search(null, ReservationStatus.Waiting);
            Assert.AreEqual(0, waiting.Count);

            var cancelled = _service.Search(null, ReservationStatus.Cancelled);
            Assert.AreEqual(1, cancelled.Count);
        }

        // ── Statistics ───────────────────────────────────────────────

        [TestMethod]
        public void GetStats_ReturnsCorrectCounts()
        {
            // Rent out Titanic too
            _rentals.Add(new Rental
            {
                CustomerId = 1, MovieId = 20, RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7), DailyRate = 1.99m,
                Status = RentalStatus.Active
            });

            _service.PlaceReservation(1, 10); // waiting
            _service.PlaceReservation(2, 10); // waiting
            _service.PlaceReservation(3, 20); // waiting

            _service.NotifyNextInQueue(10); // Alice → ready
            _service.FulfillReservation(1); // Alice → fulfilled

            var stats = _service.GetStats();

            Assert.AreEqual(3, stats.TotalReservations);
            Assert.AreEqual(2, stats.WaitingCount);  // Bob (movie 10) + Charlie (movie 20)
            Assert.AreEqual(0, stats.ReadyCount);     // Alice was fulfilled
            Assert.AreEqual(1, stats.FulfilledCount); // Alice
        }

        [TestMethod]
        public void GetStats_CalculatesFulfillmentRate()
        {
            _service.PlaceReservation(1, 10);
            _service.PlaceReservation(2, 10);

            _service.NotifyNextInQueue(10);
            _service.FulfillReservation(1); // fulfilled
            _service.CancelReservation(2); // cancelled

            var stats = _service.GetStats();
            // 1 fulfilled / (1 fulfilled + 1 cancelled) = 50%
            Assert.AreEqual(50.0, stats.FulfillmentRate);
        }

        [TestMethod]
        public void GetStats_TracksMostReservedMovies()
        {
            // Rent out Titanic too
            _rentals.Add(new Rental
            {
                CustomerId = 1, MovieId = 20, RentalDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(7), DailyRate = 1.99m,
                Status = RentalStatus.Active
            });

            _service.PlaceReservation(1, 10);
            _service.PlaceReservation(2, 10);
            _service.PlaceReservation(3, 20);

            var stats = _service.GetStats();
            Assert.IsTrue(stats.MostReservedMovies.Count > 0);
            Assert.AreEqual("Inception", stats.MostReservedMovies[0].MovieName);
            Assert.AreEqual(2, stats.MostReservedMovies[0].TotalReservations);
        }

        // ── Queue Summary ────────────────────────────────────────────

        [TestMethod]
        public void GetQueueSummary_ShowsWaitingCustomers()
        {
            _service.PlaceReservation(1, 10);
            _service.PlaceReservation(2, 10);

            var summary = _service.GetQueueSummary(10);

            Assert.IsTrue(summary.Contains("Inception"));
            Assert.IsTrue(summary.Contains("2 waiting"));
            Assert.IsTrue(summary.Contains("Alice"));
            Assert.IsTrue(summary.Contains("Bob"));
        }

        [TestMethod]
        public void GetQueueSummary_ShowsReadyStatus()
        {
            _service.PlaceReservation(1, 10);
            _service.NotifyNextInQueue(10);

            var summary = _service.GetQueueSummary(10);
            Assert.IsTrue(summary.Contains("READY"));
        }

        [TestMethod]
        public void GetQueueSummary_ReturnsNoReservationsMessage()
        {
            var summary = _service.GetQueueSummary(10);
            Assert.IsTrue(summary.Contains("No active reservations"));
        }

        [TestMethod]
        public void GetQueueSummary_ReturnsNotFound_ForBadMovie()
        {
            var summary = _service.GetQueueSummary(999);
            Assert.AreEqual("Movie not found.", summary);
        }

        // ── Process Expired ──────────────────────────────────────────

        [TestMethod]
        public void ProcessExpiredReservations_ExpiresOverdue()
        {
            _service.PlaceReservation(1, 10);
            _service.NotifyNextInQueue(10);

            // Simulate expired window
            var r = _reservations.GetById(1);
            r.ExpiresDate = DateTime.Today.AddDays(-1);
            _reservations.Update(r);

            var count = _service.ProcessExpiredReservations();
            Assert.AreEqual(1, count);

            r = _reservations.GetById(1);
            Assert.AreEqual(ReservationStatus.Expired, r.Status);
        }

        [TestMethod]
        public void ProcessExpiredReservations_ReturnsZero_WhenNoneExpired()
        {
            _service.PlaceReservation(1, 10);
            Assert.AreEqual(0, _service.ProcessExpiredReservations());
        }

        // ── Model Properties ─────────────────────────────────────────

        [TestMethod]
        public void Reservation_IsExpired_ReturnsFalse_WhenWaiting()
        {
            var r = new Reservation { Status = ReservationStatus.Waiting };
            Assert.IsFalse(r.IsExpired);
        }

        [TestMethod]
        public void Reservation_IsExpired_ReturnsTrue_WhenReadyAndPastExpiry()
        {
            var r = new Reservation
            {
                Status = ReservationStatus.Ready,
                ExpiresDate = DateTime.Today.AddDays(-1)
            };
            Assert.IsTrue(r.IsExpired);
        }

        [TestMethod]
        public void Reservation_IsExpired_ReturnsFalse_WhenReadyAndNotExpired()
        {
            var r = new Reservation
            {
                Status = ReservationStatus.Ready,
                ExpiresDate = DateTime.Today.AddDays(1)
            };
            Assert.IsFalse(r.IsExpired);
        }

        [TestMethod]
        public void Reservation_DaysWaiting_CalculatesFromReservedDate()
        {
            var r = new Reservation
            {
                ReservedDate = DateTime.Today.AddDays(-5),
                Status = ReservationStatus.Waiting
            };
            Assert.AreEqual(5, r.DaysWaiting);
        }

        [TestMethod]
        public void Reservation_DaysWaiting_UseFulfilledDate_WhenAvailable()
        {
            var r = new Reservation
            {
                ReservedDate = DateTime.Today.AddDays(-10),
                FulfilledDate = DateTime.Today.AddDays(-3),
                Status = ReservationStatus.Fulfilled
            };
            Assert.AreEqual(7, r.DaysWaiting);
        }

        // ── Constructor Validation ───────────────────────────────────

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_Throws_WhenReservationRepoNull()
        {
            new ReservationService(null, _rentals, _movies, _customers);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_Throws_WhenRentalRepoNull()
        {
            new ReservationService(_reservations, null, _movies, _customers);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_Throws_WhenMovieRepoNull()
        {
            new ReservationService(_reservations, _rentals, null, _customers);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_Throws_WhenCustomerRepoNull()
        {
            new ReservationService(_reservations, _rentals, _movies, null);
        }

        // ── InMemoryReservationRepository ────────────────────────────

        [TestMethod]
        public void Repository_Add_AssignsId()
        {
            var r = new Reservation { CustomerId = 1, MovieId = 10 };
            _reservations.Add(r);
            Assert.IsTrue(r.Id > 0);
        }

        [TestMethod]
        public void Repository_GetById_ReturnsNull_WhenNotFound()
        {
            Assert.IsNull(_reservations.GetById(999));
        }

        [TestMethod]
        public void Repository_Update_ModifiesExisting()
        {
            var r = new Reservation { CustomerId = 1, MovieId = 10, Status = ReservationStatus.Waiting };
            _reservations.Add(r);

            r.Status = ReservationStatus.Cancelled;
            _reservations.Update(r);

            var fetched = _reservations.GetById(r.Id);
            Assert.AreEqual(ReservationStatus.Cancelled, fetched.Status);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Repository_Update_Throws_WhenNotFound()
        {
            _reservations.Update(new Reservation { Id = 999 });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Repository_Add_Throws_WhenNull()
        {
            _reservations.Add(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Repository_Update_Throws_WhenNull()
        {
            _reservations.Update(null);
        }

        [TestMethod]
        public void Repository_Remove_DeletesReservation()
        {
            var r = new Reservation { CustomerId = 1, MovieId = 10 };
            _reservations.Add(r);
            _reservations.Remove(r.Id);
            Assert.IsNull(_reservations.GetById(r.Id));
        }

        [TestMethod]
        public void Repository_Remove_NoOp_WhenNotFound()
        {
            _reservations.Remove(999); // should not throw
        }

        [TestMethod]
        public void Repository_GetAll_ReturnsAll()
        {
            _reservations.Add(new Reservation { CustomerId = 1, MovieId = 10 });
            _reservations.Add(new Reservation { CustomerId = 2, MovieId = 20 });
            Assert.AreEqual(2, _reservations.GetAll().Count);
        }

        [TestMethod]
        public void Repository_Search_ByCaseInsensitiveName()
        {
            _reservations.Add(new Reservation { CustomerId = 1, MovieId = 10, CustomerName = "Alice", MovieName = "Inception" });
            var results = _reservations.Search("alice", null);
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Repository_Search_ByMovieName()
        {
            _reservations.Add(new Reservation { CustomerId = 1, MovieId = 10, CustomerName = "Alice", MovieName = "Inception" });
            var results = _reservations.Search("inception", null);
            Assert.AreEqual(1, results.Count);
        }

        [TestMethod]
        public void Repository_Search_WithStatusFilter()
        {
            _reservations.Add(new Reservation { CustomerId = 1, MovieId = 10, Status = ReservationStatus.Waiting, CustomerName = "A" });
            _reservations.Add(new Reservation { CustomerId = 2, MovieId = 10, Status = ReservationStatus.Cancelled, CustomerName = "B" });

            var waiting = _reservations.Search(null, ReservationStatus.Waiting);
            Assert.AreEqual(1, waiting.Count);
        }

        [TestMethod]
        public void Repository_GetExpired_ReturnsOnlyExpired()
        {
            _reservations.Add(new Reservation
            {
                CustomerId = 1, MovieId = 10,
                Status = ReservationStatus.Ready,
                ExpiresDate = DateTime.Today.AddDays(-1)
            });
            _reservations.Add(new Reservation
            {
                CustomerId = 2, MovieId = 10,
                Status = ReservationStatus.Ready,
                ExpiresDate = DateTime.Today.AddDays(1) // not expired
            });

            var expired = _reservations.GetExpired();
            Assert.AreEqual(1, expired.Count);
        }

        [TestMethod]
        public void Repository_HasActiveReservation_ReturnsFalse_WhenCancelled()
        {
            _reservations.Add(new Reservation
            {
                CustomerId = 1, MovieId = 10,
                Status = ReservationStatus.Cancelled
            });
            Assert.IsFalse(_reservations.HasActiveReservation(1, 10));
        }

        [TestMethod]
        public void Repository_GetNextInQueue_ReturnsLowestPosition()
        {
            _reservations.Add(new Reservation { CustomerId = 2, MovieId = 10, Status = ReservationStatus.Waiting, QueuePosition = 2, CustomerName = "Bob" });
            _reservations.Add(new Reservation { CustomerId = 1, MovieId = 10, Status = ReservationStatus.Waiting, QueuePosition = 1, CustomerName = "Alice" });

            var next = _reservations.GetNextInQueue(10);
            Assert.AreEqual("Alice", next.CustomerName);
        }

        [TestMethod]
        public void Repository_GetNextInQueue_ReturnsNull_WhenEmpty()
        {
            Assert.IsNull(_reservations.GetNextInQueue(10));
        }

        // ── Full Lifecycle ───────────────────────────────────────────

        [TestMethod]
        public void FullLifecycle_ReserveNotifyFulfill()
        {
            // 1. Place reservation
            var r = _service.PlaceReservation(1, 10);
            Assert.AreEqual(ReservationStatus.Waiting, r.Status);
            Assert.AreEqual(1, r.QueuePosition);

            // 2. Movie returned → notify
            var notified = _service.NotifyNextInQueue(10);
            Assert.AreEqual(ReservationStatus.Ready, notified.Status);
            Assert.IsNotNull(notified.ExpiresDate);

            // 3. Customer picks up
            var fulfilled = _service.FulfillReservation(notified.Id);
            Assert.AreEqual(ReservationStatus.Fulfilled, fulfilled.Status);
            Assert.IsNotNull(fulfilled.FulfilledDate);

            // Queue should be empty
            Assert.AreEqual(0, _service.GetMovieQueue(10).Count);
        }

        [TestMethod]
        public void FullLifecycle_MultipleCustomersInQueue()
        {
            // 3 customers reserve
            _service.PlaceReservation(1, 10); // pos 1
            _service.PlaceReservation(2, 10); // pos 2
            _customers.Add(new Customer { Id = 4, Name = "Diana" });
            _service.PlaceReservation(4, 10); // pos 3

            // Movie returned → Alice notified
            var notified = _service.NotifyNextInQueue(10);
            Assert.AreEqual("Alice", notified.CustomerName);

            // Alice picks up
            _service.FulfillReservation(notified.Id);

            // Queue compacted: Bob=1, Diana=2
            var queue = _service.GetMovieQueue(10);
            Assert.AreEqual(2, queue.Count);
            Assert.AreEqual(1, queue[0].QueuePosition);
            Assert.AreEqual("Bob", queue[0].CustomerName);
            Assert.AreEqual(2, queue[1].QueuePosition);
            Assert.AreEqual("Diana", queue[1].CustomerName);

            // Movie returned again → Bob notified
            var bob = _service.NotifyNextInQueue(10);
            Assert.AreEqual("Bob", bob.CustomerName);
            Assert.AreEqual(ReservationStatus.Ready, bob.Status);
        }
    }
}
